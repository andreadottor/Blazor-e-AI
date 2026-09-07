namespace Dottor.BlazorAI.Web.Data;

public sealed class Document
{
    public Guid Id { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public required byte[] PdfContent { get; set; }
    public string? ExtractedText { get; set; }
    public DocumentStatus Status { get; set; }
    public string? Summary { get; set; }
    public string? Category { get; set; }
    public byte[]? GeneratedImage { get; set; }
    public string? GeneratedImageContentType { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum DocumentStatus
{
    Uploaded,
    Processing,
    Completed,
    Failed
}
