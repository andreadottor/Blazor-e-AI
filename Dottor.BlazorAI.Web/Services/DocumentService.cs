using Dottor.BlazorAI.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Dottor.BlazorAI.Web.Services;

/// <summary>
/// Persistence gateway for <see cref="Document"/> entities. It stores the uploaded PDF and updates the
/// document as each pipeline step completes (extracted text, summary, category, generated image, status).
/// </summary>
/// <remarks>
/// Data flow reference: used by <b>Demo 3</b> (<c>/demo3</c>) and <b>Demo 4</b> (<c>/demo4</c>) to save the
/// uploaded PDF and to persist the incremental results produced by the document pipeline.
/// </remarks>
public sealed class DocumentService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>Maximum accepted size of an uploaded PDF (10 MB).</summary>
    public const long MaxPdfSize = 10 * 1024 * 1024;

    /// <summary>
    /// Validates and stores an uploaded PDF as a new document in the <see cref="DocumentStatus.Uploaded"/> state.
    /// This is the entry point of the flow triggered when the user uploads a file in Demo 3 and Demo 4.
    /// </summary>
    /// <param name="fileName">Original file name of the upload.</param>
    /// <param name="contentType">Content type reported by the browser.</param>
    /// <param name="content">Stream with the PDF bytes.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>The identifier of the newly created document.</returns>
    /// <exception cref="InvalidDataException">Thrown when the file is empty, too large or not a valid PDF.</exception>
    public async Task<Guid> SaveUploadAsync(string fileName, string contentType, Stream content, CancellationToken ct = default)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
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
            Id          = Guid.NewGuid(),
            FileName    = Path.GetFileName(fileName),
            ContentType = "application/pdf",
            PdfContent  = bytes,
            Status      = DocumentStatus.Uploaded,
            CreatedAt   = now,
            UpdatedAt   = now
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);
        return document.Id;
    }

    /// <summary>
    /// Loads a document by its identifier (read-only). Used by the pipeline and by the UI to display results.
    /// </summary>
    public async Task<Document?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    /// <summary>
    /// Loads the lightweight document list used by the overview page without reading PDF or image blobs.
    /// </summary>
    public async Task<List<DocumentListItem>> GetDocumentsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Documents
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new DocumentListItem(
                x.Id,
                x.FileName,
                x.Status,
                x.ApprovalStatus,
                x.Category,
                x.GeneratedImage != null,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(ct);
    }

    /// <summary>Marks the document as <see cref="DocumentStatus.Processing"/> and clears any previous error.</summary>
    public Task MarkProcessingAsync(Guid id, CancellationToken ct) =>
        UpdateAsync(id, document =>
        {
            document.Status = DocumentStatus.Processing;
            document.ErrorMessage = null;
        }, ct);

    /// <summary>Persists the text extracted from the PDF (result of the "ExtractText" pipeline step).</summary>
    public Task SaveExtractedTextAsync(Guid id, string text, CancellationToken ct) =>
        UpdateAsync(id, document => document.ExtractedText = text, ct);

    /// <summary>Persists the AI-generated summary (result of the "Summary" pipeline step).</summary>
    public Task SaveSummaryAsync(Guid id, string summary, CancellationToken ct) =>
        UpdateAsync(id, document => document.Summary = summary, ct);

    /// <summary>Persists the AI-generated category (result of the "Category" pipeline step).</summary>
    public Task SaveCategoryAsync(Guid id, string category, CancellationToken ct) =>
        UpdateAsync(id, document => document.Category = category, ct);

    /// <summary>Persists the AI proposal before the workflow asks for human approval.</summary>
    public Task SaveImagePromptAsync(Guid id, string prompt, CancellationToken ct) =>
        UpdateAsync(id, document =>
        {
            document.GeneratedImagePrompt = prompt;
            document.ApprovedImagePrompt  = null;
            document.ApprovalStatus       = ApprovalStatus.Pending;
            document.Status               = DocumentStatus.WaitingForApproval;
            document.ApprovedAt           = null;
        }, ct);

    /// <summary>Persists the exact prompt approved by the user.</summary>
    public Task ApproveImagePromptAsync(Guid id, string prompt, CancellationToken ct) =>
        UpdateAsync(id, document =>
        {
            document.ApprovedImagePrompt = prompt;
            document.ApprovalStatus      = ApprovalStatus.Approved;
            document.Status               = DocumentStatus.Approved;
            document.ApprovedAt           = DateTimeOffset.UtcNow;
        }, ct);

    /// <summary>Records a controlled rejection; no image will be generated.</summary>
    public Task RejectImagePromptAsync(Guid id, CancellationToken ct) =>
        UpdateAsync(id, document =>
        {
            document.ApprovedImagePrompt = null;
            document.ApprovalStatus      = ApprovalStatus.Rejected;
            document.Status              = DocumentStatus.Rejected;
        }, ct);

    /// <summary>Persists the AI-generated image (result of the "Image" pipeline step).</summary>
    public Task SaveImageAsync(Guid id, byte[] image, string contentType, CancellationToken ct) =>
        UpdateAsync(id, document =>
        {
            document.GeneratedImage            = image;
            document.GeneratedImageContentType = contentType;
        }, ct);

    /// <summary>Marks the document as <see cref="DocumentStatus.Completed"/> (final "Complete" pipeline step).</summary>
    public Task MarkCompletedAsync(Guid id, CancellationToken ct) =>
        UpdateAsync(id, document => document.Status = DocumentStatus.Completed, ct);

    /// <summary>Marks processing as cancelled without classifying it as a technical failure.</summary>
    public Task MarkCancelledAsync(Guid id, CancellationToken ct = default) =>
        UpdateAsync(id, document =>
        {
            document.Status       = DocumentStatus.Cancelled;
            document.ErrorMessage = null;
        }, ct);

    /// <summary>Marks the document as <see cref="DocumentStatus.Failed"/> and stores the error message.</summary>
    public Task MarkFailedAsync(Guid id, string error, CancellationToken ct = default) =>
        UpdateAsync(id, document =>
        {
            document.Status       = DocumentStatus.Failed;
            document.ErrorMessage = error;
        }, ct);

    /// <summary>
    /// Loads the document, applies the <paramref name="update"/> mutation, refreshes the timestamp and saves it.
    /// </summary>
    private async Task UpdateAsync(Guid id, Action<Document> update, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var document = await db.Documents.SingleAsync(x => x.Id == id, ct);
        update(document);
        document.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

public sealed record DocumentListItem(
    Guid Id,
    string FileName,
    DocumentStatus Status,
    ApprovalStatus? ApprovalStatus,
    string? Category,
    bool ImageAvailable,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
