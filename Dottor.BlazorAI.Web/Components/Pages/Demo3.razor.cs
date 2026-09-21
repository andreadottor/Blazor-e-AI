namespace Dottor.BlazorAI.Web.Components.Pages;

using Dottor.BlazorAI.Web.Pipeline;
using Dottor.BlazorAI.Web.Services;
using Microsoft.AspNetCore.Components.Forms;

public partial class Demo3
{
    private readonly List<PipelineUpdate> updates = [];
    private CancellationTokenSource? cancellation;
    private Guid? documentId;
    private string? fileName;
    private string? summary;
    private string? category;
    private string? imagePrompt;
    private string? error;
    private bool imageAvailable;
    private bool approvalPending;
    private bool approvalResponsePending;
    private bool isProcessing;

    private async Task ProcessAsync(InputFileChangeEventArgs args)
    {
        Reset();
        cancellation = new CancellationTokenSource();
        isProcessing = true;
        fileName = args.File.Name;

        try
        {
            await using var stream = args.File.OpenReadStream(DocumentService.MaxPdfSize, cancellation.Token);
            documentId = await Documents.SaveUploadAsync(
                args.File.Name, 
                args.File.ContentType, 
                stream, 
                cancellation.Token);

            await foreach (var update in Pipeline.ProcessAsync(documentId.Value, cancellation.Token))
            {
                Apply(update);
                StateHasChanged();
            }
        }
        catch (OperationCanceledException)
        {
            error = "Elaborazione annullata.";
        }
        catch (Exception ex)
        {
            error = ex.Message;
            if (documentId is not null)
            {
                await Documents.MarkFailedAsync(documentId.Value, ex.Message);
            }
        }
        finally
        {
            isProcessing = false;
        }
    }

    private void Apply(PipelineUpdate update)
    {
        var index = updates.FindIndex(x => x.Step == update.Step);
        if (index < 0) updates.Add(update); else updates[index] = update;
        summary = update.Summary ?? summary;
        category = update.Category ?? category;
        imagePrompt = update.ImagePrompt ?? imagePrompt;
        imageAvailable |= update.ImageAvailable;
        approvalPending = update.Status == PipelineUpdateStatus.WaitingForApproval;
        if (update.Status == PipelineUpdateStatus.Failed) error = update.Message;
    }

    private async Task ApproveAsync(string prompt) => await RespondAsync(true, prompt);

    private async Task RejectAsync() => await RespondAsync(false, null);

    private async Task RespondAsync(bool approved, string? prompt)
    {
        if (documentId is null || approvalResponsePending) return;

        approvalResponsePending = true;
        error = null;
        try
        {
            await Pipeline.RespondAsync(documentId.Value, approved, prompt, cancellation?.Token ?? default);
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
        cancellation?.Cancel();
        cancellation?.Dispose();
        updates.Clear();
        documentId = null;
        summary = null;
        category = null;
        imagePrompt = null;
        error = null;
        imageAvailable = false;
        approvalPending = false;
        approvalResponsePending = false;
    }

    public ValueTask DisposeAsync()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        return ValueTask.CompletedTask;
    }
}
