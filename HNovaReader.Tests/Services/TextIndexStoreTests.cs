using HNovaReader.Data.Entities;
using HNovaReader.Services.Reader;
using HNovaReader.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HNovaReader.Tests.Services;

public sealed class TextIndexStoreTests
{
    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsIndexAndRejectsStaleKeys()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var bookId = await AddBookAsync(database, cancellationToken);
        var store = new EfTextIndexStore(database.DbContextFactory, TimeProvider.System);
        var version = new ReaderFileVersion(
            4096,
            new DateTime(2026, 8, 28, 8, 0, 0, DateTimeKind.Utc));
        var snapshot = new TextIndexSnapshot(
            version,
            "utf-8",
            1,
            120,
            true,
            [new TextIndexCheckpoint(0, 3), new TextIndexCheckpoint(120, 180)],
            [new ChapterInfo("第一章", 2, 10, 70), new ChapterInfo("第二章", 2, 70, 120)]);

        await store.SaveAsync(bookId, snapshot, cancellationToken);
        var loaded = await store.TryLoadAsync(
            bookId,
            version,
            "UTF-8",
            1,
            cancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(120, loaded.TotalCharacterCount);
        Assert.Equal(snapshot.Checkpoints, loaded.Checkpoints);
        Assert.Equal(snapshot.Chapters, loaded.Chapters);
        Assert.Null(await store.TryLoadAsync(
            bookId,
            version with { FileSize = version.FileSize + 1 },
            "utf-8",
            1,
            cancellationToken));
        Assert.Null(await store.TryLoadAsync(
            bookId,
            version,
            "utf-16",
            1,
            cancellationToken));
        Assert.Null(await store.TryLoadAsync(
            bookId,
            version,
            "utf-8",
            2,
            cancellationToken));
    }

    [Fact]
    public async Task BookDeletion_CascadesReaderIndexRows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var bookId = await AddBookAsync(database, cancellationToken);
        var store = new EfTextIndexStore(database.DbContextFactory, TimeProvider.System);
        var snapshot = new TextIndexSnapshot(
            new ReaderFileVersion(20, DateTime.UtcNow),
            "utf-8",
            1,
            10,
            true,
            [new TextIndexCheckpoint(0, 0)],
            [new ChapterInfo("第一章", 2, 0, 10)]);
        await store.SaveAsync(bookId, snapshot, cancellationToken);

        await using (var dbContext = await database.DbContextFactory.CreateDbContextAsync(
            cancellationToken))
        {
            var book = await dbContext.Books.SingleAsync(
                item => item.Id == bookId,
                cancellationToken);
            dbContext.Books.Remove(book);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await using var verification = await database.DbContextFactory.CreateDbContextAsync(
            cancellationToken);
        Assert.Equal(0, await verification.BookTextIndexes.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.TextCheckpoints.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.Chapters.CountAsync(cancellationToken));
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
            Title = "Reader Index Test",
            DateAddedUtc = DateTime.UtcNow,
            FileSize = 0,
            LastWriteTimeUtc = DateTime.UtcNow,
            FileEncoding = "utf-8",
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return id;
    }
}
