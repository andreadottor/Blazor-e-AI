namespace Dottor.BlazorAI.Web.Components.Pages;

using Markdig;
using Markdown.ColorCode;

public partial class Demo2
{
    private string prompt = "Spiegami in modo semplice perché IAsyncEnumerable è utile in una UI Blazor.";
    private string answer = string.Empty;
    private string? error;
    private bool isStreaming;
    private CancellationTokenSource? cancellation;

    private static readonly MarkdownPipeline markdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseColorCode()
        .Build();

    private async Task AskAsync()
    {
        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        answer = string.Empty;
        error = null;
        isStreaming = true;

        try
        {
            await foreach (var text in Chat.AskStreamingAsync(prompt, cancellation.Token))
            {
                answer += text;
                StateHasChanged();
            }
        }
        catch (OperationCanceledException)
        {
            error = "Generazione interrotta.";
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            isStreaming = false;
        }
    }

    private void Stop() => cancellation?.Cancel();

    public ValueTask DisposeAsync()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        return ValueTask.CompletedTask;
    }
}
