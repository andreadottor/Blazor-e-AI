using Dottor.BlazorAI.Web.Data;
using Dottor.BlazorAI.Web.Pipeline;
using Dottor.BlazorAI.Web.Services;

namespace Dottor.BlazorAI.Web.Background;

/// <summary>
/// Runs the same pipeline used by Demo 3 outside the Blazor component lifetime. The pipeline persists
/// state first; this worker then fans updates out to connected pages and notifies an absent user only
/// for approval, completion, or failure.
/// </summary>
public sealed class DocumentProcessingWorker(DocumentProcessingQueue queue, DocumentUpdateHub updates, IServiceScopeFactory scopeFactory, ILogger<DocumentProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            await ProcessAsync(request, stoppingToken);
        }
    }

    private async Task ProcessAsync(DocumentProcessingRequest request, CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting job {JobId} for document {DocumentId}", request.JobId, request.DocumentId);
        var failureHandled = false;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var pipeline      = scope.ServiceProvider.GetRequiredService<DocumentPipelineService>();
            var documents     = scope.ServiceProvider.GetRequiredService<DocumentService>();
            var notifications = scope.ServiceProvider.GetRequiredService<DocumentNotificationService>();

            await foreach (var update in pipeline.ProcessAsync(request.DocumentId, stoppingToken))
            {
                // Presence is intentionally a process-local snapshot. A subscriber may connect or leave
                // before SMTP is called; that small race is acceptable for this conference demo.
                var hasSubscribers = updates.HasSubscribers(request.DocumentId);
                await updates.PublishAsync(request.DocumentId, update, stoppingToken);

                if (!hasSubscribers && IsAttentionEvent(update))
                {
                    logger.LogInformation("No active subscriber for document {DocumentId} at {Status}", request.DocumentId, update.Status);
                }

                if (update.Status == PipelineUpdateStatus.WaitingForApproval && !hasSubscribers)
                {
                    await TryNotifyAsync(
                            request.DocumentId,
                            documents,
                            notifications.SendApprovalRequiredAsync,
                            "approval",
                            stoppingToken);
                }
                else if (update.Status == PipelineUpdateStatus.Failed)
                {
                    failureHandled = true;
                    if (!hasSubscribers)
                    {
                        await TryNotifyAsync(
                            request.DocumentId,
                            documents,
                            notifications.SendFailedAsync,
                            "failure",
                            stoppingToken);
                    }
                }
                else if (update.Step == "Complete" && update.Status == PipelineUpdateStatus.Completed)
                {
                    var document = await documents.GetAsync(request.DocumentId, stoppingToken);
                    if (document?.Status == DocumentStatus.Completed)
                    {
                        if (!hasSubscribers)
                        {
                            await TryNotifyAsync(
                                document,
                                notifications.SendCompletedAsync,
                                "completion",
                                stoppingToken);
                        }

                        logger.LogInformation("Completed job {JobId}", request.JobId);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Cancelled job {JobId}", request.JobId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed job {JobId}", request.JobId);
            using var scope = scopeFactory.CreateScope();
            var documents = scope.ServiceProvider.GetRequiredService<DocumentService>();
            var notifications = scope.ServiceProvider.GetRequiredService<DocumentNotificationService>();
            await documents.MarkFailedAsync(request.DocumentId, ex.GetBaseException().Message, CancellationToken.None);

            var hasSubscribers = updates.HasSubscribers(request.DocumentId);
            await updates.PublishAsync(
                request.DocumentId,
                new("Pipeline", PipelineUpdateStatus.Failed, ex.GetBaseException().Message),
                CancellationToken.None);

            if (!hasSubscribers && !failureHandled)
            {
                logger.LogInformation("No active subscriber for failed document {DocumentId}", request.DocumentId);
                await TryNotifyAsync(
                    request.DocumentId,
                    documents,
                    notifications.SendFailedAsync,
                    "failure",
                    CancellationToken.None);
            }
        }
        finally
        {
            updates.Complete(request.DocumentId);
        }
    }

    private static bool IsAttentionEvent(PipelineUpdate update) =>
        update.Status is PipelineUpdateStatus.WaitingForApproval or PipelineUpdateStatus.Failed ||
        update.Step == "Complete" && update.Status == PipelineUpdateStatus.Completed;

    private async Task TryNotifyAsync(Guid documentId, DocumentService documents, Func<Document, CancellationToken, Task> send, string notificationKind, CancellationToken ct)
    {
        var document = await documents.GetAsync(documentId, ct);
        if (document is not null)
        {
            await TryNotifyAsync(document, send, notificationKind, ct);
        }
    }

    private async Task TryNotifyAsync(Document document, Func<Document, CancellationToken, Task> send, string notificationKind, CancellationToken ct)
    {
        try
        {
            await send(document, ct);
            logger.LogInformation("{NotificationKind} email sent for document {DocumentId}", notificationKind, document.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A local SMTP problem must not change the already-persisted pipeline outcome.
            logger.LogError(ex, "Could not send {NotificationKind} email for document {DocumentId}", notificationKind, document.Id);
        }
    }
}
