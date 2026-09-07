using Dottor.BlazorAI.Web.Pipeline;

namespace Dottor.BlazorAI.Web.Background;

public sealed class DocumentProcessingWorker(
    DocumentProcessingQueue queue,
    DocumentUpdateHub updates,
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentProcessingWorker> logger) : BackgroundService
{
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
