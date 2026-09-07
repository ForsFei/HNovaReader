using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HNovaReader.Data.Configurations;

public sealed class BookmarkConfiguration : IEntityTypeConfiguration<Bookmark>
{
    public void Configure(EntityTypeBuilder<Bookmark> builder)
    {
        builder.ToTable(
            "Bookmarks",
            table =>
            {
                table.HasCheckConstraint("CK_Bookmarks_CharacterOffset_NonNegative", "CharacterOffset >= 0");
                table.HasCheckConstraint("CK_Bookmarks_FileSize_NonNegative", "FileSize >= 0");
            });
        builder.HasKey(bookmark => bookmark.Id);
        builder.Property(bookmark => bookmark.ChapterTitleSnapshot).HasMaxLength(240);
        builder.Property(bookmark => bookmark.PreviewText).HasMaxLength(500).IsRequired();
        builder.Property(bookmark => bookmark.AnchorPrefix).HasMaxLength(256);
        builder.Property(bookmark => bookmark.AnchorSuffix).HasMaxLength(256);
        builder.Property(bookmark => bookmark.EncodingName).HasMaxLength(64).IsRequired();
        builder.HasIndex(bookmark => new { bookmark.BookId, bookmark.CharacterOffset }).IsUnique();
        builder.HasIndex(bookmark => bookmark.CreatedAtUtc);
        builder.HasOne(bookmark => bookmark.Book)
            .WithMany(book => book.Bookmarks)
            .HasForeignKey(bookmark => bookmark.BookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
