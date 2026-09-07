using System.Text;
using UglyToad.PdfPig;

namespace Dottor.BlazorAI.Web.Services;

public sealed class DocumentTextExtractor
{
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
