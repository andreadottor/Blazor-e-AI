using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Dottor.BlazorAI.Web.Services;

/// <summary>
/// Thin wrapper around <see cref="IChatClient"/> (Microsoft.Extensions.AI) that exposes the two
/// fundamental ways of consuming a Large Language Model: waiting for the whole answer or receiving
/// it token by token.
/// </summary>
/// <remarks>
/// Data flow reference:
/// <list type="bullet">
///   <item><description><b>Demo 1</b> (<c>/demo1</c>) uses <see cref="AskAsync"/> to wait for the complete response.</description></item>
///   <item><description><b>Demo 2</b> (<c>/demo2</c>) uses <see cref="AskStreamingAsync"/> to stream the response with <see cref="IAsyncEnumerable{T}"/>.</description></item>
/// </list>
/// It is also reused internally by the document pipeline (Demo 3 and Demo 4) to summarize and categorize the extracted text.
/// </remarks>
public sealed class ChatService(IChatClient chatClient, IConfiguration configuration)
{
    /// <summary>
    /// Sends the <paramref name="prompt"/> to the model and returns the full text only after the
    /// generation has completed. Used by <b>Demo 1</b> (<c>/demo1</c>).
    /// </summary>
    /// <param name="prompt">The user prompt sent to the model.</param>
    /// <param name="ct">Token used to cancel the request.</param>
    /// <returns>The complete text produced by the model.</returns>
    public async Task<string> AskAsync(string prompt, CancellationToken ct)
    {
        EnsureConfigured();
        var response = await chatClient.GetResponseAsync(prompt, cancellationToken: ct);
        return response.Text;
    }

    /// <summary>
    /// Sends the <paramref name="prompt"/> to the model and yields the response incrementally as the
    /// model produces it, so the UI can render tokens as they arrive. Used by <b>Demo 2</b> (<c>/demo2</c>).
    /// </summary>
    /// <param name="prompt">The user prompt sent to the model.</param>
    /// <param name="ct">Token used to cancel the streaming enumeration.</param>
    /// <returns>An asynchronous stream of text chunks that make up the full answer.</returns>
    public async IAsyncEnumerable<string> AskStreamingAsync(string prompt, [EnumeratorCancellation] CancellationToken ct)
    {
        EnsureConfigured();

        await foreach (var update in chatClient.GetStreamingResponseAsync(prompt, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    /// <summary>
    /// Guards every call by ensuring the OpenAI/Foundry secrets required by the demos are configured.
    /// </summary>
    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(configuration["AI:OpenAI:ApiKey"]))
        {
            throw new InvalidOperationException("Configura i secret AI:OpenAI dell'AppHost per eseguire le demo AI.");
        }
    }
}
