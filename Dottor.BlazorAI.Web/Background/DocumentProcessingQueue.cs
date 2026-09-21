using System.Threading.Channels;

namespace Dottor.BlazorAI.Web.Background;

/// <summary>
/// Message enqueued for background processing: it links a background <paramref name="JobId"/> to the
/// <paramref name="DocumentId"/> that must be processed.
/// </summary>
/// <param name="JobId">Identifier used to track UI progress updates for this run.</param>
/// <param name="DocumentId">Identifier of the document to process.</param>
public sealed record DocumentProcessingRequest(Guid JobId, Guid DocumentId);

/// <summary>
/// Producer/consumer queue built on an unbounded <see cref="Channel{T}"/>. The Blazor page pushes work
/// items and the <see cref="DocumentProcessingWorker"/> consumes them out of the request lifetime.
/// </summary>
/// <remarks>
/// Data flow reference: this is the core of the background processing shown in <b>Demo 4</b> (<c>/demo4</c>).
/// </remarks>
public sealed class DocumentProcessingQueue
{
    private readonly Channel<DocumentProcessingRequest> _queue = Channel.CreateUnbounded<DocumentProcessingRequest>();

    /// <summary>Adds a processing request to the queue.</summary>
    public ValueTask EnqueueAsync(DocumentProcessingRequest request, CancellationToken ct) =>
        _queue.Writer.WriteAsync(request, ct);

    /// <summary>Asynchronously reads all queued requests until the channel is completed or cancelled.</summary>
    public IAsyncEnumerable<DocumentProcessingRequest> ReadAllAsync(CancellationToken ct) =>
        _queue.Reader.ReadAllAsync(ct);
}
