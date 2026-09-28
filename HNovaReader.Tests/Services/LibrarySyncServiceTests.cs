using HNovaReader.Data.Entities;
using HNovaReader.Infrastructure.Storage;
using HNovaReader.Services.Import;
using HNovaReader.Services.Library;
using HNovaReader.Services.Settings;
using HNovaReader.Tests.TestSupport;

namespace HNovaReader.Tests.Services;

public sealed class LibrarySyncServiceTests
{
    [Fact]
    public async Task Sync_RecognizesEmptyAndDeepFoldersAndPrunesDeletedEmptyDirectories()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SqliteTestDatabase.CreateAsync(ct);
        var (sync, library, root) = Create(db);
        var deep = Path.Combine(root, "一层", "二层", "三层", "四层", "五层");
        Directory.CreateDirectory(deep);
        await File.WriteAllTextAsync(Path.Combine(deep, "书.txt"), "content", ct);
        var empty = Path.Combine(root, "空文件夹");
        Directory.CreateDirectory(empty);
        Assert.Equal(1, (await sync.SyncAsync(ct)).AddedCount);
        Assert.Equal(Path.Combine(deep, "书.txt"), Assert.Single(await library.GetBooksAsync(ct)).FilePath);
        await using var context = await db.DbContextFactory.CreateDbContextAsync(ct);
        Assert.Contains(context.LibraryFolders, folder => folder.RelativePath == "空文件夹");
        Directory.Delete(empty);
        await sync.SyncAsync(ct);
        context.ChangeTracker.Clear();
        Assert.DoesNotContain(context.LibraryFolders, folder => folder.RelativePath == "空文件夹");
    }

    [Fact]
    public async Task Sync_ImportsByDirectoryWithoutCopyingAndRemovesDeletedManagedBook()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(ct);
        var (sync, library, root) = Create(database);
        var seriesDirectory = Path.Combine(root, "科幻", "基地");
        Directory.CreateDirectory(seriesDirectory);
        var firstPath = Path.Combine(seriesDirectory, "第一部.TXT");
        var secondPath = Path.Combine(seriesDirectory, "第二部.txt");
        await File.WriteAllTextAsync(firstPath, "第一章 内容", ct);
        await File.WriteAllTextAsync(secondPath, "第一章 续集", ct);
        var backups = Path.Combine(root, LibraryBackupService.BackupDirectoryName, "旧备份");
        Directory.CreateDirectory(backups);
        await File.WriteAllTextAsync(Path.Combine(backups, "不要导入.txt"), "backup", ct);

        var first = await sync.SyncAsync(ct);
        Assert.Equal(2, first.AddedCount);
        Assert.Empty(first.Errors);
        var books = await library.GetBooksAsync(ct);
        Assert.Equal(2, books.Count);
        Assert.All(books, book =>
        {
            Assert.Equal("基地", book.FolderName);
            Assert.Null(book.SeriesName);
            Assert.Equal(seriesDirectory, Path.GetDirectoryName(book.FilePath));
        });
        var survivingId = books.Single(book => book.FilePath == secondPath).Id;
        var second = await sync.SyncAsync(ct);
        Assert.Equal(0, second.AddedCount);
        Assert.Equal(0, second.RemovedCount);
        File.Delete(firstPath);
        var third = await sync.SyncAsync(ct);
        Assert.Equal(1, third.RemovedCount);
        Assert.Equal(survivingId, Assert.Single(await library.GetBooksAsync(ct)).Id);
        Assert.True(File.Exists(secondPath));
    }

    [Fact]
    public async Task Sync_MissingRootAndIncompleteScanNeverRemoveRecords()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(ct);
        var (sync, library, root) = Create(database);
        var directory = Path.Combine(root, "收件箱", "未分系列");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "保留.txt");
        await File.WriteAllTextAsync(path, "content", ct);
        await sync.SyncAsync(ct);
        Directory.Move(root, root + "-offline");
        var missing = await sync.SyncAsync(ct);
        Assert.NotEmpty(missing.Errors);
        Assert.Single(await library.GetBooksAsync(ct));
        Assert.False(Directory.Exists(root));
        Directory.Move(root + "-offline", root);
        File.Delete(path);
        var (partial, _, _) = Create(database, new PartialScanner());
        var result = await partial.SyncAsync(ct);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, result.RemovedCount);
        Assert.Single(await library.GetBooksAsync(ct));
    }

    [Fact]
    public async Task Sync_RootFilesUseDefaultSeriesAndExternalOriginalRecordsAreUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(ct);
        var (sync, library, root) = Create(database);
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "根目录.txt");
        await File.WriteAllTextAsync(source, "content", ct);
        var external = Path.Combine(database.RootDirectory, "outside.txt");
        await File.WriteAllTextAsync(external, "external", ct);
        await library.AddBookAsync(new AddBookRequest
        {
            FilePath = external,
            SourceFilePath = external,
            StorageMode = BookStorageMode.OriginalLocation,
            FileEncoding = "utf-8",
            FileSize = new FileInfo(external).Length,
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(external),
        }, ct);
        File.Delete(external);
        Assert.Equal(1, (await sync.SyncAsync(ct)).AddedCount);
        var books = await library.GetBooksAsync(ct);
        Assert.Equal(2, books.Count);
        var managed = Assert.Single(books, book => book.StorageMode == BookStorageMode.ManagedCopy);
        Assert.Null(managed.SeriesId);
        Assert.Equal(LibraryOrganizationDefaults.RootFolderId, managed.FolderId);
        Assert.Equal(source, managed.FilePath);
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(managed.FilePath));
        Assert.Equal(0, (await sync.SyncAsync(ct)).AddedCount);
    }

    private static (LibrarySyncService Sync, LibraryService Library, string Root) Create(
        SqliteTestDatabase database, ITextFileScanner? scanner = null)
    {
        var root = Path.Combine(database.RootDirectory, "Managed");
        var paths = new AppPaths(Path.Combine(database.RootDirectory, "AppData"));
        paths.EnsureDirectories();
        var settings = new StubAppSettingsService();
        settings.Set(new AppSettings { LibraryFolder = root });
        var library = new LibraryService(database.DbContextFactory, new FilePathNormalizer(), TimeProvider.System);
        var organization = new LibraryOrganizationService(database.DbContextFactory, new FilePathNormalizer(),
            settings, paths, new LibraryBackupService(TimeProvider.System));
        var importer = new TextImportService(new TextFileScanner(), new TextEncodingDetector(),
            new ManagedBookFileStore(), library, organization, paths);
        return (new LibrarySyncService(scanner ?? new TextFileScanner(), importer, library, organization,
            settings, paths), library, root);
    }

    private sealed class PartialScanner : ITextFileScanner
    {
        public Task<TextFileScanResult> ScanAsync(IReadOnlyCollection<string> paths, bool includeSubdirectories,
            IProgress<TextImportProgress>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new TextFileScanResult([], [new FileScanError(paths.First(), "access denied")]));
    }
}
