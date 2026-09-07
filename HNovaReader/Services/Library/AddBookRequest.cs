using HNovaReader.Data.Entities;

namespace HNovaReader.Services.Library;

public sealed record AddBookRequest
{
    public required string FilePath { get; init; }

    public string? SourceFilePath { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? CoverPath { get; init; }

    public string? AuthorName { get; init; }

    public Guid? FolderId { get; init; }

    public Guid? SeriesId { get; init; }

    public string? SeriesName { get; init; }

    public double? SeriesIndex { get; init; }

    public IReadOnlyCollection<string> Tags { get; init; } = Array.Empty<string>();

    public bool IsFavorite { get; init; }

    public long FileSize { get; init; }

    public DateTime LastWriteTimeUtc { get; init; }

    public string FileEncoding { get; init; } = "utf-8";

    public BookStorageMode StorageMode { get; init; } = BookStorageMode.ManagedCopy;
}
