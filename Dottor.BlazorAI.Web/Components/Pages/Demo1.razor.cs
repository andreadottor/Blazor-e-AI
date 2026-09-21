namespace Dottor.BlazorAI.Web.Components.Pages;

using Markdig;
using Markdown.ColorCode;

public partial class Demo1
{
    private string prompt = "Spiegami in modo semplice perché IAsyncEnumerable è utile in una UI Blazor.";
    private string? answer;
    private string? error;
    private bool isLoading;

    private static readonly MarkdownPipeline markdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseColorCode()
        .Build();

    private async Task AskAsync()
    {
        isLoading = true;
        answer = null;
        error = null;

        try
        {
            answer = await Chat.AskAsync(prompt, CancellationToken.None);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            isLoading = false;
        }
    }
}
