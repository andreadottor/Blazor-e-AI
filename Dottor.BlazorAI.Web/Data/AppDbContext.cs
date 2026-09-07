using Microsoft.EntityFrameworkCore;

namespace Dottor.BlazorAI.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var document = modelBuilder.Entity<Document>();
        document.HasKey(x => x.Id);
        document.Property(x => x.FileName).HasMaxLength(260);
        document.Property(x => x.ContentType).HasMaxLength(100);
        document.Property(x => x.PdfContent).HasColumnType("varbinary(max)");
        document.Property(x => x.ExtractedText).HasColumnType("nvarchar(max)");
        document.Property(x => x.Summary).HasColumnType("nvarchar(max)");
        document.Property(x => x.Category).HasMaxLength(100);
        document.Property(x => x.GeneratedImage).HasColumnType("varbinary(max)");
        document.Property(x => x.GeneratedImageContentType).HasMaxLength(100);
        document.Property(x => x.ErrorMessage).HasColumnType("nvarchar(max)");
        document.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
    }
}
