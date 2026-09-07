using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HNovaReader.Data.Configurations;

public sealed class ReadingPositionConfiguration : IEntityTypeConfiguration<ReadingPosition>
{
    public void Configure(EntityTypeBuilder<ReadingPosition> builder)
    {
        builder.ToTable(
            "ReadingPositions",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ReadingPositions_CharacterOffset_NonNegative",
                    "CharacterOffset >= 0");
                table.HasCheckConstraint(
                    "CK_ReadingPositions_ByteOffsetHint_NonNegative",
                    "ByteOffsetHint IS NULL OR ByteOffsetHint >= 0");
                table.HasCheckConstraint(
                    "CK_ReadingPositions_Progress_Range",
                    "Progress >= 0 AND Progress <= 1");
                table.HasCheckConstraint(
                    "CK_ReadingPositions_FileSize_NonNegative",
                    "FileSize >= 0");
            });
        builder.HasKey(position => position.BookId);
        builder.Property(position => position.ChapterTitleSnapshot)
            .HasMaxLength(240);
        builder.Property(position => position.AnchorPrefix)
            .HasMaxLength(256);
        builder.Property(position => position.AnchorSuffix)
            .HasMaxLength(256);
        builder.Property(position => position.EncodingName)
            .HasMaxLength(64)
            .IsRequired();
        builder.HasIndex(position => position.UpdatedAtUtc);
        builder.HasOne(position => position.Book)
            .WithOne(book => book.ReadingPosition)
            .HasForeignKey<ReadingPosition>(position => position.BookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
