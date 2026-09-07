using System.Text;
using UglyToad.PdfPig;

namespace Dottor.BlazorAI.Web.Services;

/// <summary>
/// Extracts the raw text from an uploaded PDF using the PdfPig library. This is the first step of the
/// document processing pipeline and feeds the text to the AI summarization/categorization steps.
/// </summary>
/// <remarks>
/// Data flow reference: used by the pipeline of <b>Demo 3</b> (<c>/demo3</c>, inline) and
/// <b>Demo 4</b> (<c>/demo4</c>, background) as the "ExtractText" step.
/// </remarks>
public sealed class DocumentTextExtractor
{
    /// <summary>
    /// Opens the PDF, concatenates the text of every page and returns it.
    /// </summary>
    /// <param name="pdfContent">The raw bytes of the PDF document.</param>
    /// <returns>The full text content extracted from the PDF.</returns>
    /// <exception cref="InvalidDataException">Thrown when the PDF does not contain any extractable text.</exception>
    public string Extract(byte[] pdfContent)
    {
        using var pdf = PdfDocument.Open(pdfContent);
        var text = new StringBuilder();

        foreach (var page in pdf.GetPages())
        {
            text.AppendLine(page.Text);
        }

        if (string.IsNullOrWhiteSpace(text.ToString()))
        {
            throw new InvalidDataException("Il PDF non contiene testo estraibile.");
        }

        return text.ToString();
    }
}
