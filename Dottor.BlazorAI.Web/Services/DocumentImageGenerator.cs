using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dottor.BlazorAI.Web.Services;

/// <summary>
/// Generates an illustrative image that represents a document, by calling the Azure AI Foundry (MAI)
/// image generation REST endpoint derived from the configured OpenAI endpoint.
/// </summary>
/// <remarks>
/// Data flow reference: used by the pipeline of <b>Demo 3</b> (<c>/demo3</c>, inline) and
/// <b>Demo 4</b> (<c>/demo4</c>, background) as the "Image" step, after the summary and category
/// have been produced.
/// </remarks>
public sealed class DocumentImageGenerator(IConfiguration configuration) : IDisposable
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(3) };

    /// <summary>
    /// Uses the exact human-approved <paramref name="prompt"/>, requests an image from the model and
    /// returns its bytes together with the content type.
    /// </summary>
    /// <param name="prompt">The prompt approved in the workflow's human-in-the-loop step.</param>
    /// <param name="cancellationToken">Token used to cancel the HTTP request.</param>
    /// <returns>The generated image bytes and their content type (PNG).</returns>
    /// <exception cref="InvalidOperationException">Thrown when the AI configuration is missing or the model does not return an image.</exception>
    public async Task<(byte[] Content, string ContentType)> GenerateAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["AI:OpenAI:ApiKey"]))
        {
            throw new InvalidOperationException(
                "Configura i secret AI:OpenAI dell'AppHost per generare immagini.");
        }

        if (string.IsNullOrWhiteSpace(configuration["AI:OpenAI:ImageModel"]))
        {
            throw new InvalidOperationException(
                "Configura AI:OpenAI:ImageModel con un deployment Foundry compatibile con image generation.");
        }

        var endpoint = GetImageEndpoint();
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("api-key", configuration["AI:OpenAI:ApiKey"]);
        var requestJson = JsonSerializer.Serialize(new
        {
            model = configuration["AI:OpenAI:ImageModel"],
            prompt,
            width = 1024,
            height = 1024
        });
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Generazione immagine non riuscita ({(int)response.StatusCode}): {responseJson}");
        }

        var result = JsonSerializer.Deserialize<MaiImageResponse>(responseJson, JsonSerializerOptions.Web);
        var imageBase64 = result?.Data.FirstOrDefault()?.Base64Json;
        if (string.IsNullOrWhiteSpace(imageBase64))
        {
            throw new InvalidOperationException("Il modello non ha restituito un'immagine.");
        }

        return (Convert.FromBase64String(imageBase64), "image/png");
    }

    /// <summary>
    /// Resolves the Foundry (MAI) image generation endpoint starting from the configured OpenAI/Foundry endpoint.
    /// </summary>
    private Uri GetImageEndpoint()
    {
        var configuredEndpoint = configuration["AI:OpenAI:Endpoint"];
        if (!Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("Configura AI:OpenAI:Endpoint con l'endpoint della risorsa Foundry.");
        }

        const string openAIHostSuffix = ".openai.azure.com";
        const string foundryHostSuffix = ".services.ai.azure.com";
        string imageHost;

        if (endpoint.Host.EndsWith(foundryHostSuffix, StringComparison.OrdinalIgnoreCase))
        {
            imageHost = endpoint.Host;
        }
        else if (endpoint.Host.EndsWith(openAIHostSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var resourceName = endpoint.Host[..^openAIHostSuffix.Length];
            imageHost = $"{resourceName}{foundryHostSuffix}";
        }
        else
        {
            throw new InvalidOperationException(
                "L'endpoint configurato non consente di ricavare l'endpoint MAI della risorsa Foundry.");
        }

        return new Uri($"https://{imageHost}/mai/v1/images/generations");
    }

    private sealed record MaiImageResponse(MaiImageResult[] Data);

    private sealed record MaiImageResult(
        [property: JsonPropertyName("b64_json")] string Base64Json);

    /// <summary>
    /// Releases the underlying <see cref="HttpClient"/>.
    /// </summary>
    public void Dispose() => _httpClient.Dispose();
}
