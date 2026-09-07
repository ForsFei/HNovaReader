using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HNovaReader.Data.Configurations;

public sealed class LibraryFolderConfiguration : IEntityTypeConfiguration<LibraryFolder>
{
    public void Configure(EntityTypeBuilder<LibraryFolder> builder)
    {
        builder.ToTable("LibraryFolders");
        builder.HasKey(folder => folder.Id);
        builder.Property(folder => folder.Name)
            .HasMaxLength(160)
            .IsRequired();
        builder.Property(folder => folder.NormalizedName)
            .HasMaxLength(160)
            .IsRequired();
        builder.Property(folder => folder.UnsortedName)
            .HasMaxLength(160)
            .IsRequired();
        builder.HasIndex(folder => folder.NormalizedName)
            .IsUnique();
        builder.HasIndex(folder => folder.SortOrder);
    }
}
