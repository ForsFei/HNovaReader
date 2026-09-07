using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HNovaReader.Data.Configurations;

public sealed class ChapterConfiguration : IEntityTypeConfiguration<Chapter>
{
    public void Configure(EntityTypeBuilder<Chapter> builder)
    {
        builder.ToTable(
            "Chapters",
            table =>
            {
                table.HasCheckConstraint("CK_Chapters_Ordinal_NonNegative", "Ordinal >= 0");
                table.HasCheckConstraint("CK_Chapters_Level_Positive", "Level > 0");
                table.HasCheckConstraint(
                    "CK_Chapters_Offsets_Valid",
                    "StartOffset >= 0 AND EndOffset >= StartOffset");
            });
        builder.HasKey(chapter => new
        {
            chapter.BookTextIndexId,
            chapter.Ordinal,
        });
        builder.Property(chapter => chapter.Title)
            .HasMaxLength(240)
            .IsRequired();
        builder.HasIndex(chapter => new
        {
            chapter.BookTextIndexId,
            chapter.StartOffset,
        });
        builder.HasOne(chapter => chapter.BookTextIndex)
            .WithMany(index => index.Chapters)
            .HasForeignKey(chapter => chapter.BookTextIndexId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
