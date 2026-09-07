using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Dottor.BlazorAI.Web.Services;

public sealed class ChatService(IChatClient chatClient, IConfiguration configuration)
{
    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var response = await chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
        return response.Text;
    }

    public async IAsyncEnumerable<string> AskStreamingAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureConfigured();

        await foreach (var update in chatClient.GetStreamingResponseAsync(
            prompt,
            cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(configuration["AI:OpenAI:ApiKey"]))
        {
            throw new InvalidOperationException(
                "Configura i secret AI:OpenAI dell'AppHost per eseguire le demo AI.");
        }
    }
}
