using System.Collections.Concurrent;
using System.Threading.Channels;
using Dottor.BlazorAI.Web.Pipeline;

namespace Dottor.BlazorAI.Web.Background;

/// <summary>
/// In-memory hub that connects a background job to the single Blazor component watching its progress.
/// Each job owns one <see cref="Channel{T}"/> of <see cref="PipelineUpdate"/> messages, so this is a
/// one-writer/one-reader relay rather than a general pub/sub abstraction.
/// </summary>
/// <remarks>
/// Data flow reference: used by <b>Demo 4</b> (<c>/demo4</c>). The <see cref="DocumentProcessingWorker"/>
/// publishes updates and the page streams them via <see cref="WatchAsync"/>.
/// </remarks>
public sealed class DocumentUpdateHub
{
    // Each job has a single UI watcher: this is not a pub/sub abstraction.
    private readonly ConcurrentDictionary<Guid, Channel<PipelineUpdate>> _jobs = new();

    /// <summary>Registers a new job and creates the channel that will carry its progress updates.</summary>
    /// <exception cref="InvalidOperationException">Thrown when a job with the same id already exists.</exception>
    public void Create(Guid jobId)
    {
        if (!_jobs.TryAdd(jobId, Channel.CreateUnbounded<PipelineUpdate>()))
        {
            throw new InvalidOperationException("Job already exists.");
        }
    }

    /// <summary>Publishes a progress update for the given job (called by the background worker).</summary>
    public ValueTask PublishAsync(Guid jobId, PipelineUpdate update, CancellationToken cancellationToken) =>
        Get(jobId).Writer.WriteAsync(update, cancellationToken);

    /// <summary>Signals that no more updates will be produced for the given job.</summary>
    public void Complete(Guid jobId) => Get(jobId).Writer.TryComplete();

    /// <summary>
    /// Streams the progress updates of a job to the UI until the job completes, removing the channel afterwards.
    /// </summary>
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

    /// <summary>Resolves the channel for a job or throws when the job is unknown or already consumed.</summary>
    private Channel<PipelineUpdate> Get(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var channel)
            ? channel
            : throw new KeyNotFoundException("Job not found or already being watched.");
}
