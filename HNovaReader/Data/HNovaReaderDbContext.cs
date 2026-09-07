using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HNovaReader.Data;

public sealed class HNovaReaderDbContext(DbContextOptions<HNovaReaderDbContext> options)
    : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();

    public DbSet<Author> Authors => Set<Author>();

    public DbSet<LibraryFolder> LibraryFolders => Set<LibraryFolder>();

    public DbSet<Series> Series => Set<Series>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<BookTag> BookTags => Set<BookTag>();

    public DbSet<BookTextIndex> BookTextIndexes => Set<BookTextIndex>();

    public DbSet<TextCheckpoint> TextCheckpoints => Set<TextCheckpoint>();

    public DbSet<Chapter> Chapters => Set<Chapter>();

    public DbSet<ReadingPosition> ReadingPositions => Set<ReadingPosition>();

    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

    public DbSet<Highlight> Highlights => Set<Highlight>();

    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HNovaReaderDbContext).Assembly);
    }
}
