using HNovaReader.Services.Library;
using HNovaReader.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HNovaReader.Tests.Services;

public sealed class LibraryServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(
        2026,
        8,
        26,
        9,
        30,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task AddBookAsync_UsesFileNameAsTitleAndCreatesRelationships()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var service = CreateService(database);
        var sourcePath = Path.Combine(database.RootDirectory, "银河帝国.txt");

        var result = await service.AddBookAsync(
            new AddBookRequest
            {
                FilePath = sourcePath,
                AuthorName = "  Isaac Asimov ",
                SeriesName = "Foundation",
                SeriesIndex = 2.5,
                Tags = ["Sci-Fi", "Favorite", "sci-fi"],
                FileSize = 1024,
                LastWriteTimeUtc = new DateTime(2026, 8, 25, 12, 0, 0),
                FileEncoding = "utf-8",
            },
            cancellationToken);

        Assert.Equal("银河帝国", result.Title);
        Assert.Equal("Isaac Asimov", result.AuthorName);
        Assert.Equal("Foundation", result.SeriesName);
        Assert.Equal(2.5, result.SeriesIndex);
        Assert.Equal(["Favorite", "Sci-Fi"], result.Tags);
        Assert.Equal(FixedNow.UtcDateTime, result.DateAddedUtc);
        Assert.Equal(DateTimeKind.Utc, result.LastWriteTimeUtc.Kind);
        Assert.True(await service.ContainsFilePathAsync(
            sourcePath.ToUpperInvariant(),
            cancellationToken));

        await using var dbContext = await database.DbContextFactory.CreateDbContextAsync(
            cancellationToken);
        Assert.Equal(1, await dbContext.Authors.CountAsync(cancellationToken));
        Assert.Equal(1, await dbContext.Series.CountAsync(cancellationToken));
        Assert.Equal(2, await dbContext.Tags.CountAsync(cancellationToken));
        Assert.Equal(2, await dbContext.BookTags.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task AddBookAsync_RejectsCaseOnlyDuplicatePath()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var service = CreateService(database);
        var sourcePath = Path.Combine(database.RootDirectory, "Duplicate.txt");

        await service.AddBookAsync(
            CreateRequest(sourcePath),
            cancellationToken);

        var exception = await Assert.ThrowsAsync<DuplicateBookException>(() =>
            service.AddBookAsync(
                CreateRequest(sourcePath.ToUpperInvariant()),
                cancellationToken));

        Assert.Equal(Path.GetFullPath(sourcePath.ToUpperInvariant()), exception.FilePath);
    }

    [Fact]
    public async Task UpdateBookAsync_ReplacesMetadataAndTagLinks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var service = CreateService(database);
        var book = await service.AddBookAsync(
            CreateRequest(
                Path.Combine(database.RootDirectory, "Update.txt"),
                authorName: "Old Author",
                seriesName: "Old Series",
                tags: ["Keep", "Remove"]),
            cancellationToken);

        var updated = await service.UpdateBookAsync(
            book.Id,
            new UpdateBookRequest
            {
                Title = " Updated Title ",
                Description = " New description ",
                AuthorName = "New Author",
                Tags = ["Keep", "New"],
                IsFavorite = true,
            },
            cancellationToken);

        Assert.NotNull(updated);
        Assert.Equal("Updated Title", updated.Title);
        Assert.Equal("New description", updated.Description);
        Assert.Equal("New Author", updated.AuthorName);
        Assert.Null(updated.SeriesId);
        Assert.Null(updated.SeriesIndex);
        Assert.Equal(["Keep", "New"], updated.Tags);
        Assert.True(updated.IsFavorite);

        var reloaded = await service.GetBookAsync(book.Id, cancellationToken);
        Assert.NotNull(reloaded);
        Assert.Equal(updated.Id, reloaded.Id);
        Assert.Equal(updated.Title, reloaded.Title);
        Assert.Equal(updated.AuthorId, reloaded.AuthorId);
        Assert.Equal(updated.SeriesId, reloaded.SeriesId);
        Assert.Equal(updated.Tags, reloaded.Tags);
        Assert.Equal(updated.IsFavorite, reloaded.IsFavorite);
        Assert.Equal(DateTimeKind.Utc, reloaded.DateAddedUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, reloaded.LastWriteTimeUtc.Kind);

        await using var dbContext = await database.DbContextFactory.CreateDbContextAsync(
            cancellationToken);
        Assert.Equal(2, await dbContext.BookTags.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task GetSeriesBooksAsync_OrdersFractionalIndexesBeforeUnnumberedBooks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var service = CreateService(database);

        var second = await service.AddBookAsync(
            CreateRequest(
                Path.Combine(database.RootDirectory, "Second.txt"),
                seriesName: "Trilogy",
                seriesIndex: 2.5),
            cancellationToken);
        await service.AddBookAsync(
            CreateRequest(
                Path.Combine(database.RootDirectory, "First.txt"),
                seriesName: "Trilogy",
                seriesIndex: 1),
            cancellationToken);
        await service.AddBookAsync(
            CreateRequest(
                Path.Combine(database.RootDirectory, "Appendix.txt"),
                seriesName: "Trilogy"),
            cancellationToken);

        var result = await service.GetSeriesBooksAsync(
            Assert.IsType<Guid>(second.SeriesId),
            cancellationToken);

        Assert.Equal([1, 2.5, null], result.Select(book => book.SeriesIndex));
    }

    [Fact]
    public async Task RemoveBookAsync_DeletesDatabaseRecordButPreservesSourceFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var service = CreateService(database);
        var sourcePath = Path.Combine(database.RootDirectory, "Preserve.txt");
        await File.WriteAllTextAsync(sourcePath, "source content", cancellationToken);
        var book = await service.AddBookAsync(
            CreateRequest(sourcePath, tags: ["One"]),
            cancellationToken);

        var removed = await service.RemoveBookAsync(book.Id, cancellationToken);

        Assert.True(removed);
        Assert.True(File.Exists(sourcePath));
        Assert.Null(await service.GetBookAsync(book.Id, cancellationToken));
        Assert.False(await service.RemoveBookAsync(book.Id, cancellationToken));

        await using var dbContext = await database.DbContextFactory.CreateDbContextAsync(
            cancellationToken);
        Assert.Equal(0, await dbContext.Books.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.BookTags.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task RefreshBookFileInfoAsync_UpdatesFileMetadataWithoutChangingUserMetadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var service = CreateService(database);
        var book = await service.AddBookAsync(
            CreateRequest(
                Path.Combine(database.RootDirectory, "Refresh.txt"),
                authorName: "Author",
                tags: ["Keep"]),
            cancellationToken);
        var refreshedAt = new DateTime(2026, 8, 28, 8, 30, 0, DateTimeKind.Local);

        var updated = await service.RefreshBookFileInfoAsync(
            book.Id,
            new RefreshBookFileInfoRequest
            {
                FileSize = 987654,
                LastWriteTimeUtc = refreshedAt,
                FileEncoding = "gb18030",
            },
            cancellationToken);

        Assert.NotNull(updated);
        Assert.Equal(987654, updated.FileSize);
        Assert.Equal(refreshedAt.ToUniversalTime(), updated.LastWriteTimeUtc);
        Assert.Equal(DateTimeKind.Utc, updated.LastWriteTimeUtc.Kind);
        Assert.Equal("gb18030", updated.FileEncoding);
        Assert.Equal("Author", updated.AuthorName);
        Assert.Equal(["Keep"], updated.Tags);
    }

    [Fact]
    public async Task RelocateBookFileAsync_UpdatesFileFieldsAndPreservesLibraryIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var service = CreateService(database);
        var book = await service.AddBookAsync(
            CreateRequest(
                Path.Combine(database.RootDirectory, "Missing.txt"),
                authorName: "Author",
                seriesName: "Series",
                seriesIndex: 2.5,
                tags: ["Keep"]),
            cancellationToken);
        var relocatedPath = Path.Combine(database.RootDirectory, "Found.txt");
        var lastWrite = new DateTime(2026, 8, 31, 8, 0, 0, DateTimeKind.Local);

        var relocated = await service.RelocateBookFileAsync(
            book.Id,
            new RelocateBookFileRequest
            {
                FilePath = relocatedPath,
                FileSize = 4096,
                LastWriteTimeUtc = lastWrite,
                FileEncoding = "gb18030",
            },
            cancellationToken);

        Assert.NotNull(relocated);
        Assert.Equal(book.Id, relocated.Id);
        Assert.Equal(Path.GetFullPath(relocatedPath), relocated.FilePath);
        Assert.Equal(relocated.FilePath, relocated.SourceFilePath);
        Assert.Equal(HNovaReader.Data.Entities.BookStorageMode.OriginalLocation, relocated.StorageMode);
        Assert.Equal(4096, relocated.FileSize);
        Assert.Equal(lastWrite.ToUniversalTime(), relocated.LastWriteTimeUtc);
        Assert.Equal("gb18030", relocated.FileEncoding);
        Assert.Equal(book.Title, relocated.Title);
        Assert.Equal("Author", relocated.AuthorName);
        Assert.Equal("Series", relocated.SeriesName);
        Assert.Equal(2.5, relocated.SeriesIndex);
        Assert.Equal(["Keep"], relocated.Tags);
    }

    [Fact]
    public async Task RelocateBookFileAsync_RejectsPathOwnedByAnotherBook()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var service = CreateService(database);
        var first = await service.AddBookAsync(
            CreateRequest(Path.Combine(database.RootDirectory, "First.txt")),
            cancellationToken);
        var secondPath = Path.Combine(database.RootDirectory, "Second.txt");
        await service.AddBookAsync(CreateRequest(secondPath), cancellationToken);

        var exception = await Assert.ThrowsAsync<DuplicateBookException>(() =>
            service.RelocateBookFileAsync(
                first.Id,
                new RelocateBookFileRequest
                {
                    FilePath = secondPath.ToUpperInvariant(),
                    FileSize = 10,
                    LastWriteTimeUtc = DateTime.UtcNow,
                    FileEncoding = "utf-8",
                },
                cancellationToken));

        Assert.Equal(Path.GetFullPath(secondPath.ToUpperInvariant()), exception.FilePath);
    }

    [Fact]
    public async Task AddBookAsync_RejectsNegativeFileSize()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var service = CreateService(database);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.AddBookAsync(
                new AddBookRequest
                {
                    FilePath = Path.Combine(database.RootDirectory, "Invalid.txt"),
                    FileSize = -1,
                },
                cancellationToken));
    }

    private static LibraryService CreateService(SqliteTestDatabase database)
    {
        return new LibraryService(
            database.DbContextFactory,
            new FilePathNormalizer(),
            new FixedTimeProvider(FixedNow));
    }

    private static AddBookRequest CreateRequest(
        string filePath,
        string? authorName = null,
        string? seriesName = null,
        double? seriesIndex = null,
        IReadOnlyCollection<string>? tags = null)
    {
        return new AddBookRequest
        {
            FilePath = filePath,
            AuthorName = authorName,
            SeriesName = seriesName,
            SeriesIndex = seriesIndex,
            Tags = tags ?? Array.Empty<string>(),
            FileSize = 1,
            LastWriteTimeUtc = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc),
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
