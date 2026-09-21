using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Dottor.BlazorAI.Web.Pipeline;

namespace Dottor.BlazorAI.Web.Background;

/// <summary>
/// In-memory fan-out for live updates. SQL Server owns the current state; these channels only carry
/// updates produced after a UI subscriber connects.
/// </summary>
public sealed class DocumentUpdateHub(ILogger<DocumentUpdateHub> logger)
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<PipelineUpdate>>> _documents = new();

    public bool HasSubscribers(Guid documentId) 
        => _documents.TryGetValue(documentId, out var subscribers) && !subscribers.IsEmpty;

    public async IAsyncEnumerable<PipelineUpdate> SubscribeAsync(Guid documentId, [EnumeratorCancellation] CancellationToken ct)
    {
        var subscriptionId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<PipelineUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        var subscribers = _documents.GetOrAdd(documentId, _ => new());
        subscribers[subscriptionId] = channel;
        logger.LogInformation("Subscriber {SubscriptionId} added for document {DocumentId}; active subscribers: {SubscriberCount}", subscriptionId, documentId, subscribers.Count);

        try
        {
            await foreach (var update in channel.Reader.ReadAllAsync(ct))
            {
                yield return update;
            }
        }
        finally
        {
            subscribers.TryRemove(subscriptionId, out _);
            if (subscribers.IsEmpty)
            {
                _documents.TryRemove(new KeyValuePair<Guid, ConcurrentDictionary<Guid, Channel<PipelineUpdate>>>(documentId, subscribers));
            }

            logger.LogInformation("Subscriber {SubscriptionId} removed for document {DocumentId}; active subscribers: {SubscriberCount}", subscriptionId, documentId, subscribers.Count);
        }
    }

    public Task<int> PublishAsync(Guid documentId, PipelineUpdate update, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var delivered = 0;

        if (_documents.TryGetValue(documentId, out var subscribers))
        {
            foreach (var channel in subscribers.Values)
            {
                if (channel.Writer.TryWrite(update))
                {
                    delivered++;
                }
            }
        }

        logger.LogInformation("Published {Step}/{Status} for document {DocumentId} to {SubscriberCount} subscriber(s)", update.Step, update.Status, documentId, delivered);
        return Task.FromResult(delivered);
    }

    public void Complete(Guid documentId)
    {
        if (!_documents.TryRemove(documentId, out var subscribers))
        {
            return;
        }

        foreach (var channel in subscribers.Values)
        {
            channel.Writer.TryComplete();
        }
    }
}
