namespace HNovaReader.Data.Entities;

public sealed class Book
{
    public Guid Id { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public string NormalizedFilePath { get; set; } = string.Empty;

    public string SourceFilePath { get; set; } = string.Empty;

    public string NormalizedSourceFilePath { get; set; } = string.Empty;

    public BookStorageMode StorageMode { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? CoverPath { get; set; }

    public Guid? AuthorId { get; set; }

    public Author? Author { get; set; }

    public Guid? FolderId { get; set; }

    public LibraryFolder? Folder { get; set; }

    public Guid? SeriesId { get; set; }

    public Series? Series { get; set; }

    public double? SeriesIndex { get; set; }

    public bool IsFavorite { get; set; }

    public DateTime DateAddedUtc { get; set; }

    public DateTime? LastOpenedAtUtc { get; set; }

    public long FileSize { get; set; }

    public DateTime LastWriteTimeUtc { get; set; }

    public string FileEncoding { get; set; } = string.Empty;

    public BookTextIndex? TextIndex { get; set; }

    public ReadingPosition? ReadingPosition { get; set; }

    public ICollection<Bookmark> Bookmarks { get; } = new List<Bookmark>();

    public ICollection<Highlight> Highlights { get; } = new List<Highlight>();

    public ICollection<Note> Notes { get; } = new List<Note>();

    public ICollection<BookTag> BookTags { get; } = new List<BookTag>();
}
