using HNovaReader.Data.Entities;
using HNovaReader.Services.Library;

namespace HNovaReader.Tests.TestSupport;

public static class TestBookFactory
{
    public static BookDetails Create(
        string title = "测试书籍",
        string? author = null,
        string? series = null,
        IReadOnlyList<string>? tags = null,
        bool isFavorite = false,
        string? coverPath = null,
        string? filePath = null,
        BookStorageMode storageMode = BookStorageMode.OriginalLocation,
        double? seriesIndex = 1,
        DateTime? lastOpenedAtUtc = null,
        long? readingCharacterOffset = null,
        double? readingProgress = null,
        string? readingChapterTitle = null)
    {
        var id = Guid.NewGuid();
        var resolvedPath = filePath ?? Path.Combine(Path.GetTempPath(), $"{id:N}.txt");
        return new BookDetails(
            id,
            resolvedPath,
            resolvedPath,
            storageMode,
            title,
            null,
            coverPath,
            author is null ? null : Guid.NewGuid(),
            author,
            series is null ? null : Guid.NewGuid(),
            series,
            series is null ? null : seriesIndex,
            tags ?? Array.Empty<string>(),
            isFavorite,
            new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc),
            lastOpenedAtUtc,
            2048,
            new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc),
            "utf-8",
            readingCharacterOffset,
            readingProgress,
            readingChapterTitle);
    }
}
