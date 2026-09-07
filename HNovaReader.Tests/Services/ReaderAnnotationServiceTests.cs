using HNovaReader.Data.Entities;
using HNovaReader.Services.Reader;
using HNovaReader.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HNovaReader.Tests.Services;

public sealed class ReaderAnnotationServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(
        2026,
        8,
        31,
        8,
        30,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task CreateAndGetAsync_RoundTripsAllAnnotationTypes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var bookId = await AddBookAsync(database, cancellationToken);
        var service = CreateService(database);
        var version = CreateVersion();

        var bookmark = await service.AddBookmarkAsync(
            CreateBookmark(bookId, version),
            cancellationToken);
        var highlight = await service.AddHighlightAsync(
            CreateHighlight(bookId, version, HighlightStyle.Blue),
            cancellationToken);
        var positionNote = await service.AddPositionNoteAsync(
            new CreatePositionNoteRequest(
                bookId,
                512,
                "位置附近的文字",
                "独立位置笔记",
                "位置前文",
                "位置后文",
                version,
                "utf-8"),
            cancellationToken);
        var highlightNote = await service.AddHighlightNoteAsync(
            new CreateHighlightNoteRequest(
                CreateHighlight(bookId, version, HighlightStyle.Green, 768, "高亮笔记原文"),
                "高亮关联笔记"),
            cancellationToken);

        var loaded = await service.GetAsync(bookId, version, cancellationToken);

        Assert.False(loaded.NeedsReview);
        Assert.Single(loaded.Bookmarks);
        Assert.Equal(bookmark.Id, loaded.Bookmarks[0].Id);
        Assert.Equal("第二章", loaded.Bookmarks[0].ChapterTitleSnapshot);
        Assert.Equal(2, loaded.Highlights.Count);
        Assert.Contains(loaded.Highlights, item => item.Id == highlight.Id && item.Style == HighlightStyle.Blue);
        Assert.Equal(2, loaded.Notes.Count);
        Assert.Contains(loaded.Notes, item => item.Id == positionNote.Id && item.CharacterOffset == 512);
        Assert.Contains(
            loaded.Notes,
            item => item.Id == highlightNote.Note.Id
                && item.HighlightId == highlightNote.Highlight.Id
                && item.CharacterOffset == 768);
        Assert.All(loaded.Notes, item => Assert.Equal(FixedNow.UtcDateTime, item.UpdatedAtUtc));
    }

    [Fact]
    public async Task GetAsync_WhenFileVersionChanged_MarksEveryAnnotationForReview()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var bookId = await AddBookAsync(database, cancellationToken);
        var service = CreateService(database);
        var originalVersion = CreateVersion();

        await service.AddBookmarkAsync(CreateBookmark(bookId, originalVersion), cancellationToken);
        await service.AddHighlightAsync(
            CreateHighlight(bookId, originalVersion, HighlightStyle.Yellow),
            cancellationToken);
        await service.AddPositionNoteAsync(
            new CreatePositionNoteRequest(
                bookId,
                90,
                "原文",
                "笔记",
                null,
                null,
                originalVersion,
                "utf-8"),
            cancellationToken);

        var changedVersion = originalVersion with { FileSize = originalVersion.FileSize + 1 };
        var loaded = await service.GetAsync(bookId, changedVersion, cancellationToken);

        Assert.True(loaded.NeedsReview);
        Assert.All(loaded.Bookmarks, item => Assert.True(item.NeedsReview));
        Assert.All(loaded.Highlights, item => Assert.True(item.NeedsReview));
        Assert.All(loaded.Notes, item => Assert.True(item.NeedsReview));
    }

    [Fact]
    public async Task AddBookmarkAsync_AtSameOffsetRefreshesSingleBookmark()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var bookId = await AddBookAsync(database, cancellationToken);
        var service = CreateService(database);
        var version = CreateVersion();

        var first = await service.AddBookmarkAsync(CreateBookmark(bookId, version), cancellationToken);
        var refreshed = await service.AddBookmarkAsync(
            CreateBookmark(bookId, version) with { PreviewText = "更新后的预览" },
            cancellationToken);

        Assert.Equal(first.Id, refreshed.Id);
        Assert.Equal("更新后的预览", refreshed.PreviewText);
        await using var dbContext = await database.DbContextFactory.CreateDbContextAsync(cancellationToken);
        Assert.Equal(1, await dbContext.Bookmarks.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task DeleteHighlightAsync_CascadesLinkedNote_AndBookDeletionCascadesRemainingRows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var bookId = await AddBookAsync(database, cancellationToken);
        var service = CreateService(database);
        var version = CreateVersion();
        var linked = await service.AddHighlightNoteAsync(
            new CreateHighlightNoteRequest(
                CreateHighlight(bookId, version, HighlightStyle.Green),
                "关联笔记"),
            cancellationToken);
        await service.AddBookmarkAsync(CreateBookmark(bookId, version), cancellationToken);
        await service.AddPositionNoteAsync(
            new CreatePositionNoteRequest(
                bookId,
                100,
                "原文",
                "位置笔记",
                null,
                null,
                version,
                "utf-8"),
            cancellationToken);

        await service.DeleteHighlightAsync(linked.Highlight.Id, cancellationToken);
        await service.AddHighlightNoteAsync(
            new CreateHighlightNoteRequest(
                CreateHighlight(bookId, version, HighlightStyle.Blue, 900, "随书删除的高亮"),
                "随书删除的关联笔记"),
            cancellationToken);
        await using (var dbContext = await database.DbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            Assert.Equal(1, await dbContext.Highlights.CountAsync(cancellationToken));
            Assert.Equal(2, await dbContext.Notes.CountAsync(cancellationToken));
            var book = await dbContext.Books.SingleAsync(item => item.Id == bookId, cancellationToken);
            dbContext.Books.Remove(book);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await using var verification = await database.DbContextFactory.CreateDbContextAsync(cancellationToken);
        Assert.Equal(0, await verification.Bookmarks.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.Highlights.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.Notes.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task AddHighlightAsync_RejectsLengthThatDoesNotMatchOriginalText()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var service = CreateService(database);
        var invalid = CreateHighlight(Guid.NewGuid(), CreateVersion(), HighlightStyle.Yellow) with
        {
            Length = 3,
            SelectedText = "两个",
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddHighlightAsync(invalid, cancellationToken));
    }

    private static EfReaderAnnotationService CreateService(SqliteTestDatabase database)
    {
        return new EfReaderAnnotationService(
            database.DbContextFactory,
            new FixedTimeProvider(FixedNow));
    }

    private static ReaderFileVersion CreateVersion()
    {
        return new ReaderFileVersion(
            4096,
            new DateTime(2026, 8, 31, 8, 0, 0, DateTimeKind.Utc));
    }

    private static CreateBookmarkRequest CreateBookmark(Guid bookId, ReaderFileVersion version)
    {
        return new CreateBookmarkRequest(
            bookId,
            256,
            "第二章",
            "书签位置的预览文字",
            "书签前文",
            "书签后文",
            version,
            "utf-8");
    }

    private static CreateHighlightRequest CreateHighlight(
        Guid bookId,
        ReaderFileVersion version,
        HighlightStyle style,
        long offset = 300,
        string selectedText = "被高亮的原始文字")
    {
        return new CreateHighlightRequest(
            bookId,
            offset,
            selectedText.Length,
            selectedText,
            style,
            "高亮前文",
            "高亮后文",
            version,
            "utf-8");
    }

    private static async Task<Guid> AddBookAsync(
        SqliteTestDatabase database,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var filePath = Path.Combine(database.RootDirectory, $"{id:N}.txt");
        await using var dbContext = await database.DbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.Books.Add(new Book
        {
            Id = id,
            FilePath = filePath,
            NormalizedFilePath = filePath.ToUpperInvariant(),
            SourceFilePath = filePath,
            NormalizedSourceFilePath = filePath.ToUpperInvariant(),
            StorageMode = BookStorageMode.OriginalLocation,
            Title = "Annotation Test",
            DateAddedUtc = DateTime.UtcNow,
            FileSize = 0,
            LastWriteTimeUtc = DateTime.UtcNow,
            FileEncoding = "utf-8",
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return id;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
