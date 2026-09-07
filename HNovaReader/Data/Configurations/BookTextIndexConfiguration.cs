using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HNovaReader.Data.Configurations;

public sealed class BookTextIndexConfiguration : IEntityTypeConfiguration<BookTextIndex>
{
    public void Configure(EntityTypeBuilder<BookTextIndex> builder)
    {
        builder.ToTable(
            "BookTextIndexes",
            table =>
            {
                table.HasCheckConstraint("CK_BookTextIndexes_FileSize_NonNegative", "FileSize >= 0");
                table.HasCheckConstraint(
                    "CK_BookTextIndexes_TotalCharacterCount_NonNegative",
                    "TotalCharacterCount >= 0");
                table.HasCheckConstraint(
                    "CK_BookTextIndexes_PatternVersion_Positive",
                    "PatternVersion > 0");
            });
        builder.HasKey(index => index.Id);
        builder.Property(index => index.EncodingName)
            .HasMaxLength(64)
            .IsRequired();
        builder.HasIndex(index => index.BookId)
            .IsUnique();
        builder.HasOne(index => index.Book)
            .WithOne(book => book.TextIndex)
            .HasForeignKey<BookTextIndex>(index => index.BookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
