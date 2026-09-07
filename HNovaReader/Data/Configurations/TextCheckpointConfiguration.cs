using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HNovaReader.Data.Configurations;

public sealed class TextCheckpointConfiguration : IEntityTypeConfiguration<TextCheckpoint>
{
    public void Configure(EntityTypeBuilder<TextCheckpoint> builder)
    {
        builder.ToTable(
            "TextCheckpoints",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_TextCheckpoints_CharacterOffset_NonNegative",
                    "CharacterOffset >= 0");
                table.HasCheckConstraint(
                    "CK_TextCheckpoints_ByteOffset_NonNegative",
                    "ByteOffset >= 0");
            });
        builder.HasKey(checkpoint => new
        {
            checkpoint.BookTextIndexId,
            checkpoint.CharacterOffset,
        });
        builder.HasOne(checkpoint => checkpoint.BookTextIndex)
            .WithMany(index => index.Checkpoints)
            .HasForeignKey(checkpoint => checkpoint.BookTextIndexId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
