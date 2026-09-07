using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HNovaReader.Data.Configurations;

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags");
        builder.HasKey(tag => tag.Id);
        builder.Property(tag => tag.Name)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(tag => tag.NormalizedName)
            .HasMaxLength(100)
            .IsRequired();
        builder.HasIndex(tag => tag.NormalizedName)
            .IsUnique();
    }
}
