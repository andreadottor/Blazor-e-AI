using Dottor.BlazorAI.Web.Pipeline;

namespace Dottor.BlazorAI.Web.Background;

/// <summary>
/// Facade used by the UI to start a background document processing job and to observe its progress.
/// It coordinates the <see cref="DocumentProcessingQueue"/> (work handoff) and the
/// <see cref="DocumentUpdateHub"/> (progress relay).
/// </summary>
/// <remarks>
/// Data flow reference: injected in <b>Demo 4</b> (<c>/demo4</c>). The page calls <see cref="StartAsync"/>
/// right after uploading the PDF and then consumes <see cref="WatchAsync"/> to render live progress.
/// </remarks>
public sealed class DocumentJobService(
    DocumentProcessingQueue queue,
    DocumentUpdateHub updates,
    DocumentPipelineService pipeline)
{
    /// <summary>
    /// Creates a new job, registers its update channel and enqueues the document for background processing.
    /// </summary>
    /// <param name="documentId">Identifier of the document to process.</param>
    /// <param name="cancellationToken">Token used to cancel the enqueue operation.</param>
    /// <returns>The identifier of the created job, used to watch its progress.</returns>
    public async Task<Guid> StartAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var jobId = Guid.NewGuid();
        updates.Create(jobId);
        await queue.EnqueueAsync(new(jobId, documentId), cancellationToken);
        return jobId;
    }

    /// <summary>Streams the progress updates produced for the given job.</summary>
    /// <param name="jobId">Identifier returned by <see cref="StartAsync"/>.</param>
    /// <param name="cancellationToken">Token used to stop watching.</param>
    public IAsyncEnumerable<PipelineUpdate> WatchAsync(Guid jobId, CancellationToken cancellationToken) =>
        updates.WatchAsync(jobId, cancellationToken);

    /// <summary>Forwards a human response to the same workflow run used by the background worker.</summary>
    public Task RespondAsync(
        Guid documentId,
        bool approved,
        string? prompt,
        CancellationToken cancellationToken = default) =>
        pipeline.RespondAsync(documentId, approved, prompt, cancellationToken);
}
