using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HNovaReader.Data.Configurations;

public sealed class BookTagConfiguration : IEntityTypeConfiguration<BookTag>
{
    public void Configure(EntityTypeBuilder<BookTag> builder)
    {
        builder.ToTable("BookTags");
        builder.HasKey(bookTag => new { bookTag.BookId, bookTag.TagId });

        builder.HasOne(bookTag => bookTag.Book)
            .WithMany(book => book.BookTags)
            .HasForeignKey(bookTag => bookTag.BookId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(bookTag => bookTag.Tag)
            .WithMany(tag => tag.BookTags)
            .HasForeignKey(bookTag => bookTag.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
