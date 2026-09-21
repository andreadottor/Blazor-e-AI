using System.Net;
using System.Net.Mail;
using Dottor.BlazorAI.Web.Data;

namespace Dottor.BlazorAI.Web.Services;

/// <summary>Sends the three attention-worthy notifications used by Demo 4 through local SMTP.</summary>
public sealed class DocumentNotificationService(IConfiguration configuration)
{
    public Task SendApprovalRequiredAsync(Document document, CancellationToken ct) =>
        SendAsync(
            document,
            $"Approvazione richiesta per {document.FileName}",
            $"Il documento <strong>{WebUtility.HtmlEncode(document.FileName)}</strong> richiede la tua approvazione.",
            ct);

    public Task SendCompletedAsync(Document document, CancellationToken ct) =>
        SendAsync(
            document,
            $"Elaborazione completata: {document.FileName}",
            $"L'elaborazione di <strong>{WebUtility.HtmlEncode(document.FileName)}</strong> è stata completata.",
            ct);

    public Task SendFailedAsync(Document document, CancellationToken ct) =>
        SendAsync(
            document,
            $"Elaborazione non riuscita: {document.FileName}",
            $"L'elaborazione di <strong>{WebUtility.HtmlEncode(document.FileName)}</strong> non è riuscita.",
            ct);

    private async Task SendAsync(Document document, string subject, string message, CancellationToken ct)
    {
        var host        = configuration["Notifications:SmtpHost"] ?? throw new InvalidOperationException("Notifications:SmtpHost non configurato.");
        var port        = configuration.GetValue("Notifications:SmtpPort", 1025);
        var recipient   = configuration["Notifications:DemoUserEmail"] ?? throw new InvalidOperationException("Notifications:DemoUserEmail non configurato.");
        var sender      = configuration["Notifications:FromEmail"] ?? "blazor-ai@example.test";
        var baseUrl     = (configuration["Notifications:PublicBaseUrl"] ?? throw new InvalidOperationException("Notifications:PublicBaseUrl non configurato.")).TrimEnd('/');
        var documentUrl = $"{baseUrl}/documents/{document.Id}";

        using var mail = new MailMessage(sender, recipient, subject, $"""
            <html><body>
            <p>{message}</p>
            <p><a href="{WebUtility.HtmlEncode(documentUrl)}">Apri il documento</a></p>
            </body></html>
            """)
        {
            IsBodyHtml = true
        };
        using var smtp = new SmtpClient(host, port) { EnableSsl = false };
        await smtp.SendMailAsync(mail, ct);
    }
}
