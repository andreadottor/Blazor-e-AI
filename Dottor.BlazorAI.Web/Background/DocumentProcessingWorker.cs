using Dottor.BlazorAI.Web.Pipeline;

namespace Dottor.BlazorAI.Web.Background;

/// <summary>
/// Hosted background service that consumes the <see cref="DocumentProcessingQueue"/> and runs the
/// <see cref="DocumentPipelineService"/> for each queued document, republishing every pipeline update
/// to the <see cref="DocumentUpdateHub"/> so the UI can follow the progress in real time.
/// </summary>
/// <remarks>
/// Data flow reference: this is the consumer side of <b>Demo 4</b> (<c>/demo4</c>). It decouples the
/// long-running AI pipeline from the Blazor circuit/request lifetime.
/// </remarks>
public sealed class DocumentProcessingWorker(
    DocumentProcessingQueue queue,
    DocumentUpdateHub updates,
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentProcessingWorker> logger) : BackgroundService
{
    /// <summary>
    /// Continuously dequeues processing requests and, in a fresh DI scope, runs the document pipeline,
    /// forwarding each <see cref="PipelineUpdate"/> to the hub and always signalling completion at the end.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            logger.LogInformation("Starting job {JobId} for document {DocumentId}", request.JobId, request.DocumentId);
            try
            {
                using var scope = scopeFactory.CreateScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<DocumentPipelineService>();
                var failed = false;

                await foreach (var update in pipeline.ProcessAsync(request.DocumentId, stoppingToken))
                {
                    failed |= update.Status == PipelineUpdateStatus.Failed;
                    await updates.PublishAsync(request.JobId, update, stoppingToken);
                }

                if (!failed)
                {
                    logger.LogInformation("Completed job {JobId}", request.JobId);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Cancelled job {JobId}", request.JobId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed job {JobId}", request.JobId);
                await updates.PublishAsync(
                    request.JobId,
                    new("Pipeline", PipelineUpdateStatus.Failed, ex.Message),
                    CancellationToken.None);
            }
            finally
            {
                updates.Complete(request.JobId);
            }
        }
    }
}
