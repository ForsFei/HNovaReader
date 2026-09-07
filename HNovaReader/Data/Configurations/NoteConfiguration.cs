using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HNovaReader.Data.Configurations;

public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable(
            "Notes",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Notes_Anchor_ExactlyOne",
                    "(HighlightId IS NOT NULL AND CharacterOffset IS NULL) OR (HighlightId IS NULL AND CharacterOffset IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_Notes_CharacterOffset_NonNegative",
                    "CharacterOffset IS NULL OR CharacterOffset >= 0");
                table.HasCheckConstraint("CK_Notes_FileSize_NonNegative", "FileSize >= 0");
            });
        builder.HasKey(note => note.Id);
        builder.Property(note => note.PreviewText).HasMaxLength(500).IsRequired();
        builder.Property(note => note.Content).HasMaxLength(20000).IsRequired();
        builder.Property(note => note.AnchorPrefix).HasMaxLength(256);
        builder.Property(note => note.AnchorSuffix).HasMaxLength(256);
        builder.Property(note => note.EncodingName).HasMaxLength(64).IsRequired();
        builder.HasIndex(note => new { note.BookId, note.CharacterOffset });
        builder.HasIndex(note => note.HighlightId);
        builder.HasIndex(note => note.UpdatedAtUtc);
        builder.HasOne(note => note.Book)
            .WithMany(book => book.Notes)
            .HasForeignKey(note => note.BookId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(note => note.Highlight)
            .WithMany(highlight => highlight.Notes)
            .HasForeignKey(note => note.HighlightId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
