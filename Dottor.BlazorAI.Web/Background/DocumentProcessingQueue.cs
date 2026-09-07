using System.Threading.Channels;

namespace Dottor.BlazorAI.Web.Background;

public sealed record DocumentProcessingRequest(Guid JobId, Guid DocumentId);

public sealed class DocumentProcessingQueue
{
    private readonly Channel<DocumentProcessingRequest> _queue =
        Channel.CreateUnbounded<DocumentProcessingRequest>();

    public ValueTask EnqueueAsync(DocumentProcessingRequest request, CancellationToken cancellationToken) =>
        _queue.Writer.WriteAsync(request, cancellationToken);

    public IAsyncEnumerable<DocumentProcessingRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        _queue.Reader.ReadAllAsync(cancellationToken);
}
