using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Dottor.BlazorAI.Web.Services;
using Microsoft.Agents.AI.Workflows;

namespace Dottor.BlazorAI.Web.Pipeline;

public sealed class DocumentPipelineService(
                        DocumentService documents, 
                        DocumentTextExtractor textExtractor, 
                        ChatService chat, 
                        DocumentImageGenerator imageGenerator, 
                        ILogger<DocumentPipelineService> logger)
{
    private readonly ConcurrentDictionary<Guid, PendingApproval> _pendingApprovals = new();

    public async IAsyncEnumerable<PipelineUpdate> ProcessAsync(Guid documentId, [EnumeratorCancellation] CancellationToken ct)
    {
        await documents.MarkProcessingAsync(documentId, ct);
        yield return new("Upload", PipelineUpdateStatus.Completed, "PDF salvato nel database");

        var workflow = BuildWorkflow();
        await using var run = await InProcessExecution.RunStreamingAsync(workflow, new PipelineState(documentId), cancellationToken: ct);
        await using var events = run.WatchStreamAsync(ct).GetAsyncEnumerator(ct);
        var failureReported = false;

        try
        {
            while (true)
            {
                WorkflowEvent? workflowEvent = null;
                Exception? moveError = null;
                var hasEvent = false;

                try
                {
                    hasEvent = await events.MoveNextAsync();
                    if (hasEvent)
                    {
                        workflowEvent = events.Current;
                    }
                }
                catch (Exception ex)
                {
                    moveError = ex;
                }

                // 1. Cancellazione
                if (moveError is OperationCanceledException)
                {
                    await documents.MarkCancelledAsync(documentId, CancellationToken.None);
                    logger.LogInformation("Document pipeline cancelled for {DocumentId}", documentId);
                    yield return new("Pipeline", PipelineUpdateStatus.Cancelled, "Elaborazione annullata.");
                    yield break;
                }

                // 2. Errore
                if (moveError is not null)
                {
                    var message = ErrorMessage(moveError);
                    await documents.MarkFailedAsync(documentId, message, CancellationToken.None);
                    logger.LogError(moveError, "Document pipeline failed for {DocumentId}", documentId);
                    if (!failureReported)
                    {
                        yield return new("Pipeline", PipelineUpdateStatus.Failed, message);
                    }
                    yield break;
                }

                if (!hasEvent)
                {
                    yield break;
                }

                // 3. Richiesta di approvazione (human-in-the-loop)
                if (workflowEvent is RequestInfoEvent requestEvent &&
                    requestEvent.Request.TryGetDataAs<ImageApprovalRequest>(out var request))
                {
                    if (!_pendingApprovals.TryAdd(documentId, new(run, requestEvent.Request)))
                    {
                        throw new InvalidOperationException("Esiste già una richiesta di approvazione per il documento.");
                    }

                    yield return new(
                        "Approval",
                        PipelineUpdateStatus.WaitingForApproval,
                        "In attesa di approvazione",
                        ImagePrompt: request.Prompt,
                        RequestId: requestEvent.Request.RequestId);
                    continue;
                }

                // 4. Evento normale → update
                var update = Map(workflowEvent!);
                if (update is not null)
                {
                    if (update.Status == PipelineUpdateStatus.Failed && failureReported)
                    {
                        continue;
                    }

                    failureReported = update.Status == PipelineUpdateStatus.Failed;
                    yield return update;
                }
            }
        }
        finally
        {
            _pendingApprovals.TryRemove(documentId, out _);
        }
    }

    public async Task RespondAsync(Guid documentId, bool approved, string? prompt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!_pendingApprovals.TryGetValue(documentId, out var pending))
        {
            throw new InvalidOperationException("Il workflow non è in attesa di approvazione.");
        }

        var effectivePrompt = approved ? prompt?.Trim() : null;
        if (approved && string.IsNullOrWhiteSpace(effectivePrompt))
        {
            throw new InvalidOperationException("Il prompt approvato non può essere vuoto.");
        }

        var response = pending.Request.CreateResponse(new ImageApprovalDecision(documentId, approved, effectivePrompt));
        await pending.Run.SendResponseAsync(response);
        _pendingApprovals.TryRemove(documentId, out _);
    }

    private Workflow BuildWorkflow()
    {
        Func<PipelineState, CancellationToken, ValueTask<PipelineState>> extract       = ExtractTextAsync;
        Func<PipelineState, CancellationToken, ValueTask<PipelineState>> summarize     = SummarizeAsync;
        Func<PipelineState, CancellationToken, ValueTask<PipelineState>> categorize    = CategorizeAsync;
        Func<PipelineState, CancellationToken, ValueTask<ImageApprovalRequest>> createPrompt   = GenerateImagePromptAsync;
        Func<ImageApprovalDecision, CancellationToken, ValueTask<PipelineState>> applyApproval = ApplyApprovalAsync;
        Func<PipelineState, CancellationToken, ValueTask<PipelineState>> generateImage = GenerateImageAsync;
        Func<PipelineState, CancellationToken, ValueTask<PipelineState>> reject        = RejectAsync;
        Func<PipelineState, CancellationToken, ValueTask<PipelineState>> complete      = CompleteAsync;

        var extractExecutor       = extract.BindAsExecutor("ExtractText");
        var summaryExecutor       = summarize.BindAsExecutor("Summary");
        var categoryExecutor      = categorize.BindAsExecutor("Category");
        var promptExecutor        = createPrompt.BindAsExecutor("ImagePrompt");
        var approvalPort          = RequestPort.Create<ImageApprovalRequest, ImageApprovalDecision>("ImageApproval");
        var approvalExecutor      = approvalPort.BindAsExecutor();
        var applyApprovalExecutor = applyApproval.BindAsExecutor("Approval");
        var imageExecutor         = generateImage.BindAsExecutor("Image");
        var rejectExecutor        = reject.BindAsExecutor("Rejected");
        var completeExecutor      = complete.BindAsExecutor("Complete");

        return new WorkflowBuilder(extractExecutor)
            .AddEdge(extractExecutor, summaryExecutor)
            .AddEdge(summaryExecutor, categoryExecutor)
            .AddEdge(categoryExecutor, promptExecutor)
            .AddEdge(promptExecutor, approvalExecutor)
            .AddEdge(approvalExecutor, applyApprovalExecutor)
            .AddEdge<PipelineState>(applyApprovalExecutor, imageExecutor, state => state?.IsApproved == true)
            .AddEdge<PipelineState>(applyApprovalExecutor, rejectExecutor, state => state?.IsApproved == false)
            .AddEdge(imageExecutor, completeExecutor)
            .AddEdge(rejectExecutor, completeExecutor)
            .WithOutputFrom(completeExecutor)
            .Build();
    }

    private async ValueTask<PipelineState> ExtractTextAsync(PipelineState state, CancellationToken ct)
    {
        return await RunStepAsync("ExtractText", state.DocumentId, async () =>
        {
            var document = await documents.GetAsync(state.DocumentId, ct)
                ?? throw new InvalidOperationException("Documento non trovato.");
            var text = textExtractor.Extract(document.PdfContent);
            await documents.SaveExtractedTextAsync(state.DocumentId, text, ct);
            return state with { ExtractedText = text };
        });
    }

    private async ValueTask<PipelineState> SummarizeAsync(PipelineState state, CancellationToken ct)
    {
        return await RunStepAsync("Summary", state.DocumentId, async () =>
        {
            var summary = await chat.AskAsync($"""
                Riassumi in italiano il documento seguente in massimo 120 parole.

                {state.ExtractedText}
                """, ct);

            await documents.SaveSummaryAsync(state.DocumentId, summary, ct);
            return state with { Summary = summary };
        });
    }

    private async ValueTask<PipelineState> CategorizeAsync(PipelineState state, CancellationToken ct)
    {
        return await RunStepAsync("Category", state.DocumentId, async () =>
        {
            var category = await chat.AskAsync($"""
                Classifica questo documento con una sola categoria breve in italiano.
                Rispondi soltanto con la categoria.

                {state.Summary}
                """, ct);
            category = category.Trim().Trim('"');

            await documents.SaveCategoryAsync(state.DocumentId, category, ct);
            return state with { Category = category };
        });
    }

    private async ValueTask<ImageApprovalRequest> GenerateImagePromptAsync(PipelineState state, CancellationToken ct)
    {
        return await RunStepAsync("ImagePrompt", state.DocumentId, async () =>
        {
            var document = await documents.GetAsync(state.DocumentId, ct)
                ?? throw new InvalidOperationException("Documento non trovato.");
            var prompt = await chat.AskAsync($"""
                Crea un prompt in inglese per un modello di generazione immagini.
                Il risultato deve descrivere una singola illustrazione editoriale pulita, significativa,
                senza testo, loghi o watermark. Rispondi esclusivamente con il prompt.

                Nome documento: {document.FileName}
                Categoria: {state.Category}
                Riassunto: {state.Summary}
                """, ct);
            prompt = prompt.Trim().Trim('"');

            await documents.SaveImagePromptAsync(state.DocumentId, prompt, ct);
            return new ImageApprovalRequest(state.DocumentId, prompt);
        });
    }

    private async ValueTask<PipelineState> ApplyApprovalAsync(ImageApprovalDecision decision, CancellationToken ct)
    {
        return await RunStepAsync("Approval", decision.DocumentId, async () =>
        {
            var document = await documents.GetAsync(decision.DocumentId, ct) ?? throw new InvalidOperationException("Documento non trovato.");

            if (!decision.Approved)
            {
                await documents.RejectImagePromptAsync(decision.DocumentId, ct);
                return new PipelineState(
                    decision.DocumentId, 
                    document.ExtractedText, 
                    document.Summary, 
                    document.Category, 
                    false);
            }

            await documents.ApproveImagePromptAsync(decision.DocumentId, decision.Prompt!, ct);
            return new PipelineState(
                decision.DocumentId,
                document.ExtractedText,
                document.Summary,
                document.Category,
                true,
                decision.Prompt);
        });
    }

    private async ValueTask<PipelineState> GenerateImageAsync(PipelineState state, CancellationToken ct)
    {
        return await RunStepAsync("Image", state.DocumentId, async () =>
        {
            var image = await imageGenerator.GenerateAsync(state.ApprovedImagePrompt!, ct);
            await documents.SaveImageAsync(state.DocumentId, image.Content, image.ContentType, ct);
            return state;
        });
    }

    private ValueTask<PipelineState> RejectAsync(PipelineState state, CancellationToken ct) 
        => new(RunStepAsync("Rejected", state.DocumentId, () => Task.FromResult(state)));

    private async ValueTask<PipelineState> CompleteAsync(PipelineState state, CancellationToken ct)
    {
        return await RunStepAsync("Complete", state.DocumentId, async () =>
        {
            if (state.IsApproved)
            {
                await documents.MarkCompletedAsync(state.DocumentId, ct);
            }

            return state;
        });
    }

    private async Task<T> RunStepAsync<T>(string step, Guid documentId, Func<Task<T>> action)
    {
        logger.LogInformation("Starting {Step} for document {DocumentId}", step, documentId);
        try
        {
            var result = await action();
            logger.LogInformation("Completed {Step} for document {DocumentId}", step, documentId);
            return result;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Cancelled {Step} for document {DocumentId}", step, documentId);
            await documents.MarkCancelledAsync(documentId, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed {Step} for document {DocumentId}", step, documentId);
            await documents.MarkFailedAsync(documentId, ex.Message, CancellationToken.None);
            throw;
        }
    }

    private static PipelineUpdate? Map(WorkflowEvent workflowEvent) => workflowEvent switch
    {
        ExecutorInvokedEvent started when started.ExecutorId != "ImageApproval" 
                                         => new(started.ExecutorId, PipelineUpdateStatus.Started, $"{DisplayName(started.ExecutorId)}..."),
        ExecutorCompletedEvent completed => Completed(completed.ExecutorId, completed.Data),
        ExecutorFailedEvent failed       => new(failed.ExecutorId, PipelineUpdateStatus.Failed, ErrorMessage(failed.Data)),
        WorkflowErrorEvent error         => new("Pipeline", PipelineUpdateStatus.Failed, ErrorMessage(error.Exception)),
        _ => null
    };

    private static PipelineUpdate? Completed(string step, object? data)
    {
        if (step == "ImageApproval")
        {
            return null;
        }

        var state = data as PipelineState;
        var request = data as ImageApprovalRequest;
        var status = (step is "Approval" or "Rejected") && state?.IsApproved == false
            ? PipelineUpdateStatus.Rejected
            : PipelineUpdateStatus.Completed;

        return new(
            step,
            status,
            DisplayName(step),
            step == "Summary" ? state?.Summary : null,
            step == "Category" ? state?.Category : null,
            step == "Image",
            step == "ImagePrompt" ? request?.Prompt : null);
    }

    private static string DisplayName(string step) => step switch
    {
        "ExtractText"   => "Testo estratto",
        "Summary"       => "Riassunto completato",
        "Category"      => "Categorizzazione completata",
        "ImagePrompt"   => "Prompt immagine generato",
        "Approval"      => "Approvazione ricevuta",
        "Image"         => "Immagine generata",
        "Rejected"      => "Generazione immagine rifiutata",
        "Complete"      => "Elaborazione completata",
        _ => step
    };

    private static string ErrorMessage(object? error) 
        => error switch
            {
                Exception exception => exception.GetBaseException().Message,
                null                => "Pipeline non riuscita",
                _                   => error.ToString() ?? "Pipeline non riuscita"
            };

    private sealed record PipelineState(
        Guid DocumentId,
        string? ExtractedText = null,
        string? Summary = null,
        string? Category = null,
        bool IsApproved = false,
        string? ApprovedImagePrompt = null);

    private sealed record ImageApprovalRequest(Guid DocumentId, string Prompt);

    private sealed record ImageApprovalDecision(Guid DocumentId, bool Approved, string? Prompt);

    private sealed record PendingApproval(StreamingRun Run, ExternalRequest Request);
}
