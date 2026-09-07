using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HNovaReader.Data.Configurations;

public sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable(
            "Books",
            table => table.HasCheckConstraint(
                "CK_Books_FileSize_NonNegative",
                "FileSize >= 0"));
        builder.HasKey(book => book.Id);

        builder.Property(book => book.FilePath)
            .HasMaxLength(2048)
            .IsRequired();
        builder.Property(book => book.NormalizedFilePath)
            .HasMaxLength(2048)
            .IsRequired();
        builder.Property(book => book.SourceFilePath)
            .HasMaxLength(2048)
            .IsRequired();
        builder.Property(book => book.NormalizedSourceFilePath)
            .HasMaxLength(2048)
            .IsRequired();
        builder.Property(book => book.StorageMode)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(book => book.Title)
            .HasMaxLength(400)
            .IsRequired();
        builder.Property(book => book.Description)
            .HasMaxLength(4000);
        builder.Property(book => book.CoverPath)
            .HasMaxLength(2048);
        builder.Property(book => book.FileEncoding)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(book => book.NormalizedFilePath)
            .IsUnique();
        builder.HasIndex(book => book.NormalizedSourceFilePath)
            .IsUnique();
        builder.HasIndex(book => book.Title);
        builder.HasIndex(book => book.AuthorId);
        builder.HasIndex(book => book.FolderId);
        builder.HasIndex(book => book.SeriesId);
        builder.HasIndex(book => book.LastOpenedAtUtc);
        builder.HasIndex(book => book.IsFavorite);

        builder.HasOne(book => book.Author)
            .WithMany(author => author.Books)
            .HasForeignKey(book => book.AuthorId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(book => book.Folder)
            .WithMany(folder => folder.Books)
            .HasForeignKey(book => book.FolderId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(book => book.Series)
            .WithMany(series => series.Books)
            .HasForeignKey(book => book.SeriesId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
