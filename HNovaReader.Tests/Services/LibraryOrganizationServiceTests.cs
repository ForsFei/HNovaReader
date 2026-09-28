using HNovaReader.Data.Entities;
using HNovaReader.Infrastructure.Storage;
using HNovaReader.Services.Library;
using HNovaReader.Services.Settings;
using HNovaReader.Tests.TestSupport;

namespace HNovaReader.Tests.Services;

public sealed class LibraryOrganizationServiceTests
{
    [Fact]
    public async Task MigrateRoot_PreservesDeepRelativePathsAndSeriesMarkers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SqliteTestDatabase.CreateAsync(ct);
        var root = Path.Combine(db.RootDirectory, "Managed");
        var (library, org) = CreateServices(db, root);
        var folder = await org.EnsureFolderPathAsync(Path.Combine("A", "B", "C", "D"), ct);
        Directory.CreateDirectory(Path.Combine(root, folder.RelativePath));
        var path = Path.Combine(root, folder.RelativePath, "书.txt");
        await File.WriteAllTextAsync(path, "content", ct);
        var series = await org.CreateSeriesAsync(folder.Id, "标记", ct);
        var book = await AddManagedBookAsync(library, path, folder.Id, series.Id, ct);
        var destination = Path.Combine(db.RootDirectory, "NewRoot");
        await org.MigrateRootAsync(destination, ct);
        var updated = Assert.IsType<BookDetails>(await library.GetBookAsync(book.Id, ct));
        Assert.Equal(Path.Combine(destination, folder.RelativePath, "书.txt"), updated.FilePath);
        Assert.True(File.Exists(updated.FilePath));
        Assert.Equal(series.Id, updated.SeriesId);
        Assert.Equal(folder.Id, updated.FolderId);
        Assert.True(Directory.Exists(Path.Combine(destination, LibraryBackupService.BackupDirectoryName)));
    }

    [Fact]
    public async Task Rename_WhenBackupFails_LeavesDirectoryAndDatabaseUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SqliteTestDatabase.CreateAsync(ct);
        var root = Path.Combine(db.RootDirectory, "Managed");
        var paths = new AppPaths(Path.Combine(db.RootDirectory, "App"));
        var settings = new StubAppSettingsService();
        settings.Set(new AppSettings { LibraryFolder = root });
        var org = new LibraryOrganizationService(db.DbContextFactory, new FilePathNormalizer(),
            settings, paths, new FailingBackup());
        var folder = await org.CreateFolderAsync("原目录", ct);
        var path = Path.Combine(root, "原目录", "未入库.txt");
        await File.WriteAllTextAsync(path, "keep", ct);
        await Assert.ThrowsAsync<IOException>(() => org.RenameFolderAsync(folder.Id, "新目录", ct));
        Assert.True(File.Exists(path));
        Assert.False(Directory.Exists(Path.Combine(root, "新目录")));
        Assert.Equal("原目录", (await org.GetFoldersAsync(ct)).Single(item => item.Id == folder.Id).Name);
    }

    private sealed class FailingBackup : ILibraryBackupService
    {
        public Task<LibraryBackupResult> BackupAsync(string libraryRoot, string operationName,
            IReadOnlyCollection<LibraryBackupItem> items, CancellationToken cancellationToken) =>
            throw new IOException("备份失败");
    }

    [Fact]
    public async Task MoveBooks_PreservesSeriesAndIdentityAndBacksUp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SqliteTestDatabase.CreateAsync(ct);
        var root = Path.Combine(db.RootDirectory, "Managed");
        var (library, org) = CreateServices(db, root);
        var from = await org.EnsureFolderPathAsync("文学\\中文\\长篇\\原稿", ct);
        var to = await org.EnsureFolderPathAsync("分类\\阅读\\收藏\\2026", ct);
        Directory.CreateDirectory(Path.Combine(root, from.RelativePath));
        var path = Path.Combine(root, from.RelativePath, "书.txt");
        await File.WriteAllTextAsync(path, "content", ct);
        var series = await org.CreateSeriesAsync(from.Id, "系列标记", ct);
        var book = await AddManagedBookAsync(library, path, from.Id, series.Id, ct);
        var result = await org.MoveBooksAsync(new([book.Id], to.Id, null), ct);
        Assert.Equal(1, result.MovedCount);
        Assert.Empty(result.Failures);
        Assert.NotNull(result.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "manifest.json")));
        var updated = Assert.IsType<BookDetails>(await library.GetBookAsync(book.Id, ct));
        Assert.Equal(book.Id, updated.Id);
        Assert.Equal(series.Id, updated.SeriesId);
        Assert.Equal(to.Id, updated.FolderId);
        Assert.Equal(Path.Combine(root, to.RelativePath, "书.txt"), updated.FilePath);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task CreateFolders_AllowsSameNameUnderDifferentParentsAndTwentyLevels()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SqliteTestDatabase.CreateAsync(ct);
        var root = Path.Combine(db.RootDirectory, "Managed");
        var (_, org) = CreateServices(db, root);
        var first = await org.CreateFolderAsync("第一类", ct);
        var second = await org.CreateFolderAsync("第二类", ct);
        var a = await org.CreateFolderAsync(first.Id, "同名", ct);
        var b = await org.CreateFolderAsync(second.Id, "同名", ct);
        Assert.NotEqual(a.Id, b.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => org.CreateFolderAsync(first.Id, "同名", ct));
        var parent = a;
        for (var i = 0; i < 20; i++) parent = await org.CreateFolderAsync(parent.Id, "层" + i, ct);
        Assert.True(Directory.Exists(Path.Combine(root, parent.RelativePath)));
        var all = await org.GetFoldersAsync(ct);
        Assert.Equal(20, all.Count(folder => folder.Name.StartsWith("层", StringComparison.Ordinal)));
        Assert.Empty(a.Series);
    }

    [Fact]
    public async Task RenameAndReparent_UpdatesWholeSubtreeAndUnregisteredFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SqliteTestDatabase.CreateAsync(ct);
        var root = Path.Combine(db.RootDirectory, "Managed");
        var (library, org) = CreateServices(db, root);
        var folder = await org.CreateFolderAsync("原目录", ct);
        var child = await org.CreateFolderAsync(folder.Id, "子目录", ct);
        var leaf = await org.CreateFolderAsync(child.Id, "更深", ct);
        var path = Path.Combine(root, leaf.RelativePath, "正文.txt");
        await File.WriteAllTextAsync(path, "content", ct);
        var extra = Path.Combine(root, folder.RelativePath, "说明.md");
        await File.WriteAllTextAsync(extra, "unregistered", ct);
        var series = await org.CreateSeriesAsync(folder.Id, "标记", ct);
        var book = await AddManagedBookAsync(library, path, leaf.Id, series.Id, ct);
        await org.RenameFolderAsync(folder.Id, "已改名", ct);
        var target = await org.CreateFolderAsync("目标", ct);
        await org.MoveFolderAsync(folder.Id, target.Id, ct);
        Assert.False(Directory.Exists(Path.Combine(root, "原目录")));
        Assert.True(File.Exists(Path.Combine(root, "目标", "已改名", "说明.md")));
        var updated = Assert.IsType<BookDetails>(await library.GetBookAsync(book.Id, ct));
        Assert.Equal(leaf.Id, updated.FolderId);
        Assert.Equal(series.Id, updated.SeriesId);
        Assert.Equal(Path.Combine(root, "目标", "已改名", "子目录", "更深", "正文.txt"), updated.FilePath);
        Assert.Equal(updated.FilePath, updated.SourceFilePath);
        var all = await org.GetFoldersAsync(ct);
        Assert.Equal(Path.Combine("目标", "已改名", "子目录", "更深"), all.Single(item => item.Id == leaf.Id).RelativePath);
        Assert.Equal(target.Id, all.Single(item => item.Id == folder.Id).ParentId);
        Assert.True(Directory.EnumerateFiles(Path.Combine(root, LibraryBackupService.BackupDirectoryName),
            "说明.md", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task MoveFolder_RejectsCyclesAndCollisionsBeforeAnyMutation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SqliteTestDatabase.CreateAsync(ct);
        var root = Path.Combine(db.RootDirectory, "Managed");
        var (_, org) = CreateServices(db, root);
        var a = await org.CreateFolderAsync("A", ct);
        var child = await org.CreateFolderAsync(a.Id, "B", ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => org.MoveFolderAsync(a.Id, child.Id, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => org.MoveFolderAsync(a.Id, a.Id, ct));
        var c = await org.CreateFolderAsync("C", ct);
        await org.CreateFolderAsync(c.Id, "A", ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => org.MoveFolderAsync(a.Id, c.Id, ct));
        Assert.True(Directory.Exists(Path.Combine(root, "A", "B")));
        Assert.False(Directory.Exists(Path.Combine(root, LibraryBackupService.BackupDirectoryName)));
    }

    [Fact]
    public async Task Series_AreGlobalMetadataAndRenameDeleteNeverMoveFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SqliteTestDatabase.CreateAsync(ct);
        var root = Path.Combine(db.RootDirectory, "Managed");
        var (library, org) = CreateServices(db, root);
        var a = await org.CreateFolderAsync("A", ct);
        var b = await org.CreateFolderAsync("B", ct);
        var series = await org.CreateSeriesAsync(a.Id, "同一个系列", ct);
        Assert.Equal(series.Id, (await org.CreateSeriesAsync(b.Id, "同一个系列", ct)).Id);
        var path = Path.Combine(root, "A", "书.txt");
        await File.WriteAllTextAsync(path, "content", ct);
        var book = await AddManagedBookAsync(library, path, a.Id, series.Id, ct);
        await org.RenameSeriesAsync(series.Id, "新版标记 / 非目录", ct);
        var changed = Assert.IsType<BookDetails>(await library.GetBookAsync(book.Id, ct));
        Assert.Equal(path, changed.FilePath);
        Assert.Equal("新版标记 / 非目录", changed.SeriesName);
        await org.DeleteSeriesAsync(series.Id, ct);
        var cleared = Assert.IsType<BookDetails>(await library.GetBookAsync(book.Id, ct));
        Assert.Null(cleared.SeriesId);
        Assert.Equal(path, cleared.FilePath);
        Assert.True(File.Exists(path));
        Assert.False(Directory.Exists(Path.Combine(root, "A", "同一个系列")));
    }

    [Fact]
    public async Task DeleteFolder_MovesDescendantBooksToInboxWithoutClearingMarkers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SqliteTestDatabase.CreateAsync(ct);
        var root = Path.Combine(db.RootDirectory, "Managed");
        var (library, org) = CreateServices(db, root);
        var folder = await org.CreateFolderAsync("待删除", ct);
        var child = await org.CreateFolderAsync(folder.Id, "子目录", ct);
        var series = await org.CreateSeriesAsync(child.Id, "保留标记", ct);
        var path = Path.Combine(root, child.RelativePath, "书.txt");
        await File.WriteAllTextAsync(path, "content", ct);
        var book = await AddManagedBookAsync(library, path, child.Id, series.Id, ct);
        await org.DeleteFolderAsync(folder.Id, ct);
        var updated = Assert.IsType<BookDetails>(await library.GetBookAsync(book.Id, ct));
        Assert.Equal(LibraryOrganizationDefaults.InboxFolderId, updated.FolderId);
        Assert.Equal(series.Id, updated.SeriesId);
        Assert.True(File.Exists(updated.FilePath));
        Assert.False(Directory.Exists(Path.Combine(root, folder.RelativePath)));
        Assert.DoesNotContain(await org.GetFoldersAsync(ct), item => item.Id == child.Id || item.Id == folder.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => org.DeleteFolderAsync(LibraryOrganizationDefaults.RootFolderId, ct));
    }

    [Fact]
    public async Task DeleteFolder_RefusesUnregisteredFilesWithoutLosingData()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SqliteTestDatabase.CreateAsync(ct);
        var root = Path.Combine(db.RootDirectory, "Managed");
        var (_, org) = CreateServices(db, root);
        var folder = await org.CreateFolderAsync("含其他文件", ct);
        var path = Path.Combine(root, folder.RelativePath, "说明.md");
        await File.WriteAllTextAsync(path, "keep", ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => org.DeleteFolderAsync(folder.Id, ct));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task MoveBooks_RejectsExternalAndMissingRecords()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SqliteTestDatabase.CreateAsync(ct);
        var root = Path.Combine(db.RootDirectory, "Managed");
        var (library, org) = CreateServices(db, root);
        await org.EnsureDefaultsAsync(ct);
        var path = Path.Combine(db.RootDirectory, "外部.txt");
        await File.WriteAllTextAsync(path, "external", ct);
        var book = await library.AddBookAsync(new AddBookRequest
        {
            FilePath = path,
            StorageMode = BookStorageMode.OriginalLocation,
            FileSize = 8,
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(path),
            FileEncoding = "utf-8",
        }, ct);
        var result = await org.MoveBooksAsync(new([book.Id, Guid.NewGuid()], LibraryOrganizationDefaults.InboxFolderId, null), ct);
        Assert.Equal(0, result.MovedCount);
        Assert.Equal(2, result.Failures.Count);
        Assert.True(File.Exists(path));
        Assert.Null(result.BackupDirectory);
    }

    [Fact]
    public async Task RenameInboxAndEmptyFolders_RenamesPhysicalDirectories()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SqliteTestDatabase.CreateAsync(ct);
        var root = Path.Combine(db.RootDirectory, "Managed");
        var (_, org) = CreateServices(db, root);
        await org.EnsureDefaultsAsync(ct);
        await org.RenameFolderAsync(LibraryOrganizationDefaults.InboxFolderId, "导入箱", ct);
        Assert.True(Directory.Exists(Path.Combine(root, "导入箱")));
        Assert.False(Directory.Exists(Path.Combine(root, LibraryOrganizationDefaults.InboxFolderName)));
        var folder = await org.CreateFolderAsync("空目录", ct);
        await org.RenameFolderAsync(folder.Id, "新空目录", ct);
        Assert.True(Directory.Exists(Path.Combine(root, "新空目录")));
    }

    private static (LibraryService Library, LibraryOrganizationService Organization) CreateServices(
        SqliteTestDatabase database,
        string root)
    {
        var appPaths = new AppPaths(Path.Combine(database.RootDirectory, "AppData"));
        appPaths.EnsureDirectories();
        var settings = new StubAppSettingsService();
        settings.Set(new AppSettings { LibraryFolder = root });
        var normalizer = new FilePathNormalizer();
        return (
            new LibraryService(database.DbContextFactory, normalizer, TimeProvider.System),
            new LibraryOrganizationService(
                database.DbContextFactory,
                normalizer,
                settings,
                appPaths,
                new LibraryBackupService(TimeProvider.System)));
    }

    private static Task<BookDetails> AddManagedBookAsync(
        LibraryService library,
        string path,
        Guid folderId,
        Guid? seriesId,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        return library.AddBookAsync(
            new AddBookRequest
            {
                FilePath = path,
                SourceFilePath = path,
                StorageMode = BookStorageMode.ManagedCopy,
                FolderId = folderId,
                SeriesId = seriesId,
                FileSize = file.Length,
                LastWriteTimeUtc = file.LastWriteTimeUtc,
                FileEncoding = "utf-8",
            },
            cancellationToken);
    }
}
