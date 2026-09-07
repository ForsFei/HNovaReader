using HNovaReader.Data.Entities;
using HNovaReader.Services.Reader;
using HNovaReader.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HNovaReader.Tests.Services;

public sealed class ReadingPositionServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(
        2026,
        8,
        31,
        3,
        30,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task SaveAndGetAsync_RoundTripsAnchorAndUpdatesLastOpenedTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var bookId = await AddBookAsync(database, cancellationToken);
        var service = new EfReadingPositionService(
            database.DbContextFactory,
            new FixedTimeProvider(FixedNow));
        var version = new ReaderFileVersion(
            8192,
            new DateTime(2026, 8, 31, 2, 0, 0, DateTimeKind.Utc));

        await service.SaveAsync(
            new SaveReadingPositionRequest(
                bookId,
                4096,
                6144,
                0.5,
                "第二章",
                "锚点之前",
                "锚点之后",
                version,
                "utf-8"),
            cancellationToken);
        var loaded = await service.GetAsync(bookId, cancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(4096, loaded.CharacterOffset);
        Assert.Equal(6144, loaded.ByteOffsetHint);
        Assert.Equal(0.5, loaded.Progress);
        Assert.Equal("第二章", loaded.ChapterTitleSnapshot);
        Assert.Equal("锚点之前", loaded.AnchorPrefix);
        Assert.Equal("锚点之后", loaded.AnchorSuffix);
        Assert.Equal(version, loaded.FileVersion);
        Assert.Equal("utf-8", loaded.EncodingName);
        Assert.Equal(FixedNow.UtcDateTime, loaded.UpdatedAtUtc);

        await using var dbContext = await database.DbContextFactory.CreateDbContextAsync(
            cancellationToken);
        var book = await dbContext.Books.SingleAsync(
            candidate => candidate.Id == bookId,
            cancellationToken);
        Assert.Equal(FixedNow.UtcDateTime, book.LastOpenedAtUtc);
        Assert.Equal(1, await dbContext.ReadingPositions.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task SaveAsync_UpdatesSingleRowAndBookDeletionCascadesPosition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var bookId = await AddBookAsync(database, cancellationToken);
        var service = new EfReadingPositionService(database.DbContextFactory, TimeProvider.System);
        var version = new ReaderFileVersion(100, DateTime.UtcNow);

        await service.SaveAsync(
            new SaveReadingPositionRequest(
                bookId,
                10,
                null,
                0.1,
                null,
                null,
                null,
                version,
                "utf-8"),
            cancellationToken);
        await service.SaveAsync(
            new SaveReadingPositionRequest(
                bookId,
                80,
                90,
                0.8,
                "尾声",
                null,
                null,
                version,
                "utf-8"),
            cancellationToken);

        await using (var dbContext = await database.DbContextFactory.CreateDbContextAsync(
            cancellationToken))
        {
            Assert.Equal(1, await dbContext.ReadingPositions.CountAsync(cancellationToken));
            var book = await dbContext.Books.SingleAsync(
                candidate => candidate.Id == bookId,
                cancellationToken);
            dbContext.Books.Remove(book);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await using var verification = await database.DbContextFactory.CreateDbContextAsync(
            cancellationToken);
        Assert.Equal(0, await verification.ReadingPositions.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task SaveAsync_RejectsInvalidProgress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var service = new EfReadingPositionService(database.DbContextFactory, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SaveAsync(
            new SaveReadingPositionRequest(
                Guid.NewGuid(),
                0,
                null,
                1.1,
                null,
                null,
                null,
                new ReaderFileVersion(0, DateTime.UtcNow),
                "utf-8"),
            cancellationToken));
    }

    private static async Task<Guid> AddBookAsync(
        SqliteTestDatabase database,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var filePath = Path.Combine(database.RootDirectory, $"{id:N}.txt");
        await using var dbContext = await database.DbContextFactory.CreateDbContextAsync(
            cancellationToken);
        dbContext.Books.Add(new Book
        {
            Id = id,
            FilePath = filePath,
            NormalizedFilePath = filePath.ToUpperInvariant(),
            SourceFilePath = filePath,
            NormalizedSourceFilePath = filePath.ToUpperInvariant(),
            StorageMode = BookStorageMode.OriginalLocation,
            Title = "Reading Position Test",
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
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
