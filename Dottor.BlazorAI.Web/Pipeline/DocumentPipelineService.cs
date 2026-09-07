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
    public async IAsyncEnumerable<PipelineUpdate> ProcessAsync(
        Guid documentId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new("Upload", PipelineUpdateStatus.Completed, "PDF salvato nel database");
        await documents.MarkProcessingAsync(documentId, cancellationToken);

        var workflow = BuildWorkflow();
        await using var run = await InProcessExecution.RunStreamingAsync(
            workflow,
            new PipelineState(documentId),
            cancellationToken: cancellationToken);
        await using var events = run.WatchStreamAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        var failureReported = false;

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

    private Workflow BuildWorkflow()
    {
        Func<PipelineState, CancellationToken, ValueTask<PipelineState>> extract = ExtractTextAsync;
        Func<PipelineState, CancellationToken, ValueTask<PipelineState>> summarize = SummarizeAsync;
        Func<PipelineState, CancellationToken, ValueTask<PipelineState>> categorize = CategorizeAsync;
        Func<PipelineState, CancellationToken, ValueTask<PipelineState>> generateImage = GenerateImageAsync;
        Func<PipelineState, CancellationToken, ValueTask<PipelineState>> complete = CompleteAsync;

        var extractExecutor = extract.BindAsExecutor("ExtractText");
        var summaryExecutor = summarize.BindAsExecutor("Summary");
        var categoryExecutor = categorize.BindAsExecutor("Category");
        var imageExecutor = generateImage.BindAsExecutor("Image");
        var completeExecutor = complete.BindAsExecutor("Complete");

        return new WorkflowBuilder(extractExecutor)
            .AddEdge(extractExecutor, summaryExecutor)
            .AddEdge(summaryExecutor, categoryExecutor)
            .AddEdge(categoryExecutor, imageExecutor)
            .AddEdge(imageExecutor, completeExecutor)
            .WithOutputFrom(completeExecutor)
            .Build();
    }

    private async ValueTask<PipelineState> ExtractTextAsync(
        PipelineState state,
        CancellationToken cancellationToken)
    {
        return await RunStepAsync("ExtractText", state.DocumentId, async () =>
        {
            var document = await documents.GetAsync(state.DocumentId, cancellationToken)
                ?? throw new InvalidOperationException("Documento non trovato.");
            var text = textExtractor.Extract(document.PdfContent);
            await documents.SaveExtractedTextAsync(state.DocumentId, text, cancellationToken);
            return state with { ExtractedText = text };
        });
    }

    private async ValueTask<PipelineState> SummarizeAsync(
        PipelineState state,
        CancellationToken cancellationToken)
    {
        return await RunStepAsync("Summary", state.DocumentId, async () =>
        {
            var summary = await chat.AskAsync($"""
                Riassumi in italiano il documento seguente in massimo 120 parole.

                {state.ExtractedText}
                """, cancellationToken);
            await documents.SaveSummaryAsync(state.DocumentId, summary, cancellationToken);
            return state with { Summary = summary };
        });
    }

    private async ValueTask<PipelineState> CategorizeAsync(
        PipelineState state,
        CancellationToken cancellationToken)
    {
        return await RunStepAsync("Category", state.DocumentId, async () =>
        {
            var category = await chat.AskAsync($"""
                Classifica questo documento con una sola categoria breve in italiano.
                Rispondi soltanto con la categoria.

                {state.Summary}
                """, cancellationToken);
            category = category.Trim().Trim('"');
            await documents.SaveCategoryAsync(state.DocumentId, category, cancellationToken);
            return state with { Category = category };
        });
    }

    private async ValueTask<PipelineState> GenerateImageAsync(
        PipelineState state,
        CancellationToken cancellationToken)
    {
        return await RunStepAsync("Image", state.DocumentId, async () =>
        {
            var image = await imageGenerator.GenerateAsync(
                state.Summary!, state.Category!, cancellationToken);
            await documents.SaveImageAsync(
                state.DocumentId, image.Content, image.ContentType, cancellationToken);
            return state;
        });
    }

    private async ValueTask<PipelineState> CompleteAsync(
        PipelineState state,
        CancellationToken cancellationToken)
    {
        return await RunStepAsync("Complete", state.DocumentId, async () =>
        {
            await documents.MarkCompletedAsync(state.DocumentId, cancellationToken);
            return state;
        });
    }

    private async Task<PipelineState> RunStepAsync(
        string step,
        Guid documentId,
        Func<Task<PipelineState>> action)
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
            await documents.MarkFailedAsync(documentId, "Elaborazione annullata.", CancellationToken.None);
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
        ExecutorInvokedEvent started => new(
            started.ExecutorId, PipelineUpdateStatus.Started, $"{DisplayName(started.ExecutorId)}..."),
        ExecutorCompletedEvent completed => Completed(completed.ExecutorId, completed.Data as PipelineState),
        ExecutorFailedEvent failed => new(
            failed.ExecutorId, PipelineUpdateStatus.Failed, ErrorMessage(failed.Data)),
        WorkflowErrorEvent error => new(
            "Pipeline", PipelineUpdateStatus.Failed, ErrorMessage(error.Exception)),
        _ => null
    };

    private static PipelineUpdate Completed(string step, PipelineState? state) => new(
        step,
        PipelineUpdateStatus.Completed,
        DisplayName(step),
        step == "Summary" ? state?.Summary : null,
        step == "Category" ? state?.Category : null,
        step == "Image");

    private static string DisplayName(string step) => step switch
    {
        "ExtractText" => "Testo estratto",
        "Summary" => "Riassunto",
        "Category" => "Categorizzazione",
        "Image" => "Immagine generata",
        "Complete" => "Elaborazione completata",
        _ => step
    };

    private static string ErrorMessage(object? error) => error switch
    {
        Exception exception => exception.GetBaseException().Message,
        null => "Pipeline non riuscita",
        _ => error.ToString() ?? "Pipeline non riuscita"
    };

    private sealed record PipelineState(
        Guid DocumentId,
        string? ExtractedText = null,
        string? Summary = null,
        string? Category = null);
}
