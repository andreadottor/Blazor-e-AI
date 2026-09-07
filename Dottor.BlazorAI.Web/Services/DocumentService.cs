using Dottor.BlazorAI.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Dottor.BlazorAI.Web.Services;

public sealed class DocumentService(IDbContextFactory<AppDbContext> dbFactory)
{
    public const long MaxPdfSize = 10 * 1024 * 1024;

    public async Task<Guid> SaveUploadAsync(
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        if (bytes.Length == 0 || bytes.Length > MaxPdfSize)
        {
            throw new InvalidDataException("Il PDF è vuoto o supera il limite di 10 MB.");
        }

        if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
        {
            throw new InvalidDataException("Il file selezionato non è un PDF valido.");
        }

        var now = DateTimeOffset.UtcNow;
        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = Path.GetFileName(fileName),
            ContentType = "application/pdf",
            PdfContent = bytes,
            Status = DocumentStatus.Uploaded,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);
        return document.Id;
    }

    public async Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public Task MarkProcessingAsync(Guid id, CancellationToken cancellationToken) =>
        UpdateAsync(id, document =>
        {
            document.Status = DocumentStatus.Processing;
            document.ErrorMessage = null;
        }, cancellationToken);

    public Task SaveExtractedTextAsync(Guid id, string text, CancellationToken cancellationToken) =>
        UpdateAsync(id, document => document.ExtractedText = text, cancellationToken);

    public Task SaveSummaryAsync(Guid id, string summary, CancellationToken cancellationToken) =>
        UpdateAsync(id, document => document.Summary = summary, cancellationToken);

    public Task SaveCategoryAsync(Guid id, string category, CancellationToken cancellationToken) =>
        UpdateAsync(id, document => document.Category = category, cancellationToken);

    public Task SaveImageAsync(Guid id, byte[] image, string contentType, CancellationToken cancellationToken) =>
        UpdateAsync(id, document =>
        {
            document.GeneratedImage = image;
            document.GeneratedImageContentType = contentType;
        }, cancellationToken);

    public Task MarkCompletedAsync(Guid id, CancellationToken cancellationToken) =>
        UpdateAsync(id, document => document.Status = DocumentStatus.Completed, cancellationToken);

    public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken = default) =>
        UpdateAsync(id, document =>
        {
            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = error;
        }, cancellationToken);

    private async Task UpdateAsync(Guid id, Action<Document> update, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var document = await db.Documents.SingleAsync(x => x.Id == id, cancellationToken);
        update(document);
        document.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
