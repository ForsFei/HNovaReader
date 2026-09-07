using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HNovaReader.Data.Configurations;

public sealed class AuthorConfiguration : IEntityTypeConfiguration<Author>
{
    public void Configure(EntityTypeBuilder<Author> builder)
    {
        builder.ToTable("Authors");
        builder.HasKey(author => author.Id);
        builder.Property(author => author.Name)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(author => author.NormalizedName)
            .HasMaxLength(200)
            .IsRequired();
        builder.HasIndex(author => author.NormalizedName)
            .IsUnique();
    }
}
