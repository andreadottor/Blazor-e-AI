using System.Collections.Concurrent;
using System.Threading.Channels;
using Dottor.BlazorAI.Web.Pipeline;

namespace Dottor.BlazorAI.Web.Background;

public sealed class DocumentUpdateHub
{
    // Ogni job ha un solo watcher UI: non è un'astrazione pub/sub.
    private readonly ConcurrentDictionary<Guid, Channel<PipelineUpdate>> _jobs = new();

    public void Create(Guid jobId)
    {
        if (!_jobs.TryAdd(jobId, Channel.CreateUnbounded<PipelineUpdate>()))
        {
            throw new InvalidOperationException("Job già esistente.");
        }
    }

    public ValueTask PublishAsync(Guid jobId, PipelineUpdate update, CancellationToken cancellationToken) =>
        Get(jobId).Writer.WriteAsync(update, cancellationToken);

    public void Complete(Guid jobId) => Get(jobId).Writer.TryComplete();

    public async IAsyncEnumerable<PipelineUpdate> WatchAsync(
        Guid jobId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Get(jobId);
        try
        {
            await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return update;
            }
        }
        finally
        {
            if (channel.Reader.Completion.IsCompleted)
            {
                _jobs.TryRemove(jobId, out _);
            }
        }
    }

    private Channel<PipelineUpdate> Get(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var channel)
            ? channel
            : throw new KeyNotFoundException("Job non trovato o già osservato.");
}
