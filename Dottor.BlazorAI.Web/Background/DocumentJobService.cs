using Dottor.BlazorAI.Web.Pipeline;

namespace Dottor.BlazorAI.Web.Background;

public sealed class DocumentJobService(
    DocumentProcessingQueue queue,
    DocumentUpdateHub updates)
{
    public async Task<Guid> StartAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var jobId = Guid.NewGuid();
        updates.Create(jobId);
        await queue.EnqueueAsync(new(jobId, documentId), cancellationToken);
        return jobId;
    }

    public IAsyncEnumerable<PipelineUpdate> WatchAsync(Guid jobId, CancellationToken cancellationToken) =>
        updates.WatchAsync(jobId, cancellationToken);
}
