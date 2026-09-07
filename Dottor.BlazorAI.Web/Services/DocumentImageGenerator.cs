using OpenAI.Images;

namespace Dottor.BlazorAI.Web.Services;

public sealed class DocumentImageGenerator(ImageClient imageClient, IConfiguration configuration)
{
    public async Task<(byte[] Content, string ContentType)> GenerateAsync(
        string summary,
        string category,
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

        var prompt = $"""
            Crea un'illustrazione editoriale pulita, senza testo e adatta a rappresentare un documento.
            Categoria: {category}
            Riassunto: {summary}
            """;

        var options = new ImageGenerationOptions
        {
            ResponseFormat = GeneratedImageFormat.Bytes,
            Size = GeneratedImageSize.W1024xH1024
        };

        var result = await imageClient.GenerateImageAsync(prompt, options, cancellationToken);
        return (result.Value.ImageBytes.ToArray(), "image/png");
    }
}
