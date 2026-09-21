namespace Dottor.BlazorAI.Web.Components.Pages;

using Dottor.BlazorAI.Web.Data;
using Dottor.BlazorAI.Web.Pipeline;
using Dottor.BlazorAI.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

public partial class Demo4
{
    [Parameter] public Guid? Id { get; set; }

    private readonly List<PipelineUpdate> updates = [];
    private CancellationTokenSource? watchCancellation;
    private Guid? loadedId;
    private Guid? documentId;
    private Guid? jobId;
    private string? fileName;
    private string? summary;
    private string? category;
    private string? imagePrompt;
    private string? error;
    private DocumentStatus? persistentStatus;
    private bool imageAvailable;
    private bool approvalPending;
    private bool approvalResponsePending;
    private bool isListening;
    private bool isStarting;
    private bool subscribeAfterRender;

    protected override async Task OnParametersSetAsync()
    {
        if (Id is null)
        {
            if (loadedId is not null)
            {
                Reset();
            }

            return;
        }

        if (Id == loadedId)
        {
            return;
        }

        StopWatching();
        loadedId = Id;
        var document = await Documents.GetAsync(Id.Value);
        if (document is null)
        {
            error = "Documento non trovato.";
            return;
        }

        RestoreFromDatabase(document);
        subscribeAfterRender = IsActiveStatus(document.Status);
    }

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (subscribeAfterRender && documentId is not null)
        {
            subscribeAfterRender = false;
            StartWatching(documentId.Value);
        }

        return Task.CompletedTask;
    }

    private async Task StartAsync(InputFileChangeEventArgs args)
    {
        Reset();
        isStarting = true;
        fileName = args.File.Name;

        try
        {
            await using var stream = args.File.OpenReadStream(DocumentService.MaxPdfSize);
            documentId = await Documents.SaveUploadAsync(args.File.Name, args.File.ContentType, stream);
            jobId = await Jobs.StartAsync(documentId.Value);
            Navigation.NavigateTo($"/documents/{documentId}", replace: true);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            if (documentId is not null && jobId is null)
            {
                await Documents.MarkFailedAsync(documentId.Value, ex.Message);
            }
        }
        finally
        {
            isStarting = false;
        }
    }

    private void StartWatching(Guid id)
    {
        watchCancellation = new CancellationTokenSource();
        isListening = true;
        _ = WatchAsync(id, watchCancellation.Token);
        StateHasChanged();
    }

    private async Task WatchAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in Jobs.WatchAsync(id, cancellationToken))
            {
                await InvokeAsync(() =>
                {
                    Apply(update);
                    StateHasChanged();
                });
            }

            var document = await Documents.GetAsync(id, CancellationToken.None);
            if (document is not null)
            {
                await InvokeAsync(() => RestoreFromDatabase(document));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelling the subscription never cancels the BackgroundService job.
        }
        catch (Exception ex)
        {
            await InvokeAsync(() => error = ex.Message);
        }
        finally
        {
            await InvokeAsync(() =>
            {
                isListening = false;
                StateHasChanged();
            });
        }
    }

    private void RestoreFromDatabase(Document document)
    {
        updates.Clear();
        documentId = document.Id;
        fileName = document.FileName;
        persistentStatus = document.Status;
        summary = document.Summary;
        category = document.Category;
        imagePrompt = document.ApprovedImagePrompt ?? document.GeneratedImagePrompt;
        imageAvailable = document.GeneratedImage is not null;
        approvalPending = document.ApprovalStatus == ApprovalStatus.Pending;
        error = document.ErrorMessage;

        AddRestored("Upload", PipelineUpdateStatus.Completed, "PDF salvato nel database");

        if (document.ExtractedText is not null) 
            AddRestored("ExtractText", PipelineUpdateStatus.Completed);

        if (document.Summary is not null) 
            AddRestored("Summary", PipelineUpdateStatus.Completed, summary: document.Summary);
        
        if (document.Category is not null) 
            AddRestored("Category", PipelineUpdateStatus.Completed, category: document.Category);
        
        if (document.GeneratedImagePrompt is not null)
            AddRestored("ImagePrompt", PipelineUpdateStatus.Completed, imagePrompt: document.GeneratedImagePrompt);

        if (document.ApprovalStatus == ApprovalStatus.Pending)
            AddRestored("Approval", PipelineUpdateStatus.WaitingForApproval, imagePrompt: document.GeneratedImagePrompt);
        else if (document.ApprovalStatus == ApprovalStatus.Approved)
            AddRestored("Approval", PipelineUpdateStatus.Completed, imagePrompt: document.ApprovedImagePrompt);
        else if (document.ApprovalStatus == ApprovalStatus.Rejected)
            AddRestored("Approval", PipelineUpdateStatus.Rejected);

        if (document.GeneratedImage is not null)
            AddRestored("Image", PipelineUpdateStatus.Completed, imageAvailable: true);
        
        if (document.Status == DocumentStatus.Completed)
            AddRestored("Complete", PipelineUpdateStatus.Completed);
        else if (document.Status == DocumentStatus.Rejected)
            AddRestored("Rejected", PipelineUpdateStatus.Rejected);
        else if (document.Status == DocumentStatus.Cancelled)
            AddRestored("Pipeline", PipelineUpdateStatus.Cancelled, "Elaborazione annullata.");
        else if (document.Status == DocumentStatus.Failed)
            AddRestored("Pipeline", PipelineUpdateStatus.Failed, document.ErrorMessage);

        StateHasChanged();
    }

    private void AddRestored(
        string step,
        PipelineUpdateStatus status,
        string? message = null,
        string? summary = null,
        string? category = null,
        bool imageAvailable = false,
        string? imagePrompt = null) =>
        updates.Add(new(step, status, message, summary, category, imageAvailable, imagePrompt));

    private void Apply(PipelineUpdate update)
    {
        var index = updates.FindIndex(x => x.Step == update.Step);
        if (index < 0) updates.Add(update); else updates[index] = update;
        summary = update.Summary ?? summary;
        category = update.Category ?? category;
        imagePrompt = update.ImagePrompt ?? imagePrompt;
        imageAvailable |= update.ImageAvailable;
        approvalPending = update.Status == PipelineUpdateStatus.WaitingForApproval;

        persistentStatus = update switch
        {
            { Status: PipelineUpdateStatus.WaitingForApproval } => DocumentStatus.WaitingForApproval,
            { Step: "Approval", Status: PipelineUpdateStatus.Completed } => DocumentStatus.Approved,
            { Status: PipelineUpdateStatus.Rejected } => DocumentStatus.Rejected,
            { Step: "Complete", Status: PipelineUpdateStatus.Completed }
                when persistentStatus != DocumentStatus.Rejected => DocumentStatus.Completed,
            { Status: PipelineUpdateStatus.Failed } => DocumentStatus.Failed,
            { Status: PipelineUpdateStatus.Cancelled } => DocumentStatus.Cancelled,
            _ => persistentStatus is DocumentStatus.Uploaded ? DocumentStatus.Processing : persistentStatus
        };

        if (update.Status == PipelineUpdateStatus.Failed) error = update.Message;
    }

    private Task ApproveAsync(string prompt) => RespondAsync(true, prompt);

    private Task RejectAsync() => RespondAsync(false, null);

    private async Task RespondAsync(bool approved, string? prompt)
    {
        if (documentId is null || approvalResponsePending) return;

        approvalResponsePending = true;
        error = null;
        try
        {
            await Jobs.RespondAsync(documentId.Value, approved, prompt, CancellationToken.None);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            approvalResponsePending = false;
        }
    }

    private void Reset()
    {
        StopWatching();
        updates.Clear();
        loadedId = null;
        documentId = null;
        jobId = null;
        fileName = null;
        summary = null;
        category = null;
        imagePrompt = null;
        error = null;
        persistentStatus = null;
        imageAvailable = false;
        approvalPending = false;
        approvalResponsePending = false;
        subscribeAfterRender = false;
    }

    private void StopWatching()
    {
        watchCancellation?.Cancel();
        watchCancellation?.Dispose();
        watchCancellation = null;
        isListening = false;
    }

    private static bool IsActiveStatus(DocumentStatus? status) =>
        status is DocumentStatus.Uploaded or DocumentStatus.Processing or
            DocumentStatus.WaitingForApproval or DocumentStatus.Approved;

    private static string? StatusName(DocumentStatus? status) => status switch
    {
        DocumentStatus.Uploaded => "Caricato",
        DocumentStatus.Processing => "In elaborazione",
        DocumentStatus.WaitingForApproval => "In attesa di approvazione",
        DocumentStatus.Approved => "Approvato",
        DocumentStatus.Completed => "Completato",
        DocumentStatus.Rejected => "Rifiutato",
        DocumentStatus.Cancelled => "Annullato",
        DocumentStatus.Failed => "Non riuscito",
        _ => null
    };

    public ValueTask DisposeAsync()
    {
        StopWatching();
        return ValueTask.CompletedTask;
    }
}
