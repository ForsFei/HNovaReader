using HNovaReader.Data.Entities;

namespace HNovaReader.Services.Library;

public sealed record BookDetails(
    Guid Id,
    string FilePath,
    string SourceFilePath,
    BookStorageMode StorageMode,
    string Title,
    string? Description,
    string? CoverPath,
    Guid? AuthorId,
    string? AuthorName,
    Guid? SeriesId,
    string? SeriesName,
    double? SeriesIndex,
    IReadOnlyList<string> Tags,
    bool IsFavorite,
    DateTime DateAddedUtc,
    DateTime? LastOpenedAtUtc,
    long FileSize,
    DateTime LastWriteTimeUtc,
    string FileEncoding,
    long? ReadingCharacterOffset = null,
    double? ReadingProgress = null,
    string? ReadingChapterTitle = null,
    Guid? FolderId = null,
    string? FolderName = null);
