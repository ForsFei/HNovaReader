using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HNovaReader.Data.Configurations;

public sealed class HighlightConfiguration : IEntityTypeConfiguration<Highlight>
{
    public void Configure(EntityTypeBuilder<Highlight> builder)
    {
        builder.ToTable(
            "Highlights",
            table =>
            {
                table.HasCheckConstraint("CK_Highlights_StartOffset_NonNegative", "StartOffset >= 0");
                table.HasCheckConstraint("CK_Highlights_Length_Positive", "Length > 0");
                table.HasCheckConstraint("CK_Highlights_FileSize_NonNegative", "FileSize >= 0");
            });
        builder.HasKey(highlight => highlight.Id);
        builder.Property(highlight => highlight.SelectedText).HasMaxLength(16384).IsRequired();
        builder.Property(highlight => highlight.Style).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(highlight => highlight.AnchorPrefix).HasMaxLength(256);
        builder.Property(highlight => highlight.AnchorSuffix).HasMaxLength(256);
        builder.Property(highlight => highlight.EncodingName).HasMaxLength(64).IsRequired();
        builder.HasIndex(highlight => new { highlight.BookId, highlight.StartOffset });
        builder.HasIndex(highlight => highlight.CreatedAtUtc);
        builder.HasOne(highlight => highlight.Book)
            .WithMany(book => book.Highlights)
            .HasForeignKey(highlight => highlight.BookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
