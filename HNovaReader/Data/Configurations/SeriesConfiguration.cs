using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HNovaReader.Data.Configurations;

public sealed class SeriesConfiguration : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> builder)
    {
        builder.ToTable("Series");
        builder.HasKey(series => series.Id);
        builder.Property(series => series.Name)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(series => series.NormalizedName)
            .HasMaxLength(200)
            .IsRequired();
        builder.HasIndex(series => series.NormalizedName)
            .IsUnique();
        builder.Ignore(series => series.Folder);
    }
}
