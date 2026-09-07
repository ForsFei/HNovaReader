using HNovaReader.Data.Entities;
using HNovaReader.Infrastructure.Storage;
using HNovaReader.Services.Library;
using HNovaReader.Services.Settings;
using HNovaReader.Tests.TestSupport;

namespace HNovaReader.Tests.Services;

public sealed class LibraryOrganizationServiceTests
{
    [Fact]
    public async Task MoveBooksAsync_BacksUpAndMovesManagedFileWithoutChangingIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var root = Path.Combine(database.RootDirectory, "Managed");
        var sourceDirectory = Path.Combine(
            root,
            LibraryOrganizationDefaults.InboxFolderName,
            LibraryOrganizationDefaults.UnsortedName);
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "银河.txt");
        await File.WriteAllTextAsync(sourcePath, "test content", cancellationToken);

        var appPaths = new AppPaths(Path.Combine(database.RootDirectory, "AppData"));
        appPaths.EnsureDirectories();
        var settings = new StubAppSettingsService();
        settings.Set(new AppSettings { LibraryFolder = root });
        var library = new LibraryService(
            database.DbContextFactory,
            new FilePathNormalizer(),
            TimeProvider.System);
        var organization = new LibraryOrganizationService(
            database.DbContextFactory,
            new FilePathNormalizer(),
            settings,
            appPaths,
            new LibraryBackupService(TimeProvider.System));
        await organization.EnsureDefaultsAsync(cancellationToken);
        var book = await library.AddBookAsync(
            new AddBookRequest
            {
                FilePath = sourcePath,
                SourceFilePath = sourcePath,
                StorageMode = BookStorageMode.ManagedCopy,
                FolderId = LibraryOrganizationDefaults.InboxFolderId,
                FileSize = new FileInfo(sourcePath).Length,
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(sourcePath),
                FileEncoding = "utf-8",
            },
            cancellationToken);
        var folder = await organization.CreateFolderAsync("科幻", cancellationToken);
        var series = await organization.CreateSeriesAsync(folder.Id, "基地", cancellationToken);

        var result = await organization.MoveBooksAsync(
            new LibraryMoveRequest([book.Id], folder.Id, series.Id),
            cancellationToken);

        Assert.Equal(1, result.MovedCount);
        Assert.Empty(result.Failures);
        Assert.NotNull(result.BackupDirectory);
        Assert.True(Directory.Exists(result.BackupDirectory));
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "manifest.json")));
        Assert.False(File.Exists(sourcePath));
        var updated = Assert.IsType<BookDetails>(await library.GetBookAsync(book.Id, cancellationToken));
        Assert.Equal(book.Id, updated.Id);
        Assert.Equal(folder.Id, updated.FolderId);
        Assert.Equal(series.Id, updated.SeriesId);
        Assert.Equal("科幻", updated.FolderName);
        Assert.Equal("基地", updated.SeriesName);
        Assert.True(File.Exists(updated.FilePath));
        Assert.Equal(
            Path.Combine(root, "科幻", "基地", "银河.txt"),
            updated.FilePath,
            ignoreCase: true);
        Assert.StartsWith(
            Path.Combine(root, LibraryBackupService.BackupDirectoryName),
            result.BackupDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MoveBooksAsync_RejectsOriginalLocationWithoutMovingOrBackingUp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var root = Path.Combine(database.RootDirectory, "Managed");
        var externalPath = Path.Combine(database.RootDirectory, "External.txt");
        await File.WriteAllTextAsync(externalPath, "external", cancellationToken);
        var appPaths = new AppPaths(Path.Combine(database.RootDirectory, "AppData"));
        appPaths.EnsureDirectories();
        var settings = new StubAppSettingsService();
        settings.Set(new AppSettings { LibraryFolder = root });
        var library = new LibraryService(
            database.DbContextFactory,
            new FilePathNormalizer(),
            TimeProvider.System);
        var organization = new LibraryOrganizationService(
            database.DbContextFactory,
            new FilePathNormalizer(),
            settings,
            appPaths,
            new LibraryBackupService(TimeProvider.System));
        await organization.EnsureDefaultsAsync(cancellationToken);
        var book = await library.AddBookAsync(
            new AddBookRequest
            {
                FilePath = externalPath,
                StorageMode = BookStorageMode.OriginalLocation,
                FileSize = new FileInfo(externalPath).Length,
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(externalPath),
                FileEncoding = "utf-8",
            },
            cancellationToken);
        var folder = await organization.CreateFolderAsync("目标", cancellationToken);

        var result = await organization.MoveBooksAsync(
            new LibraryMoveRequest([book.Id], folder.Id, null),
            cancellationToken);

        Assert.Equal(0, result.MovedCount);
        Assert.Single(result.Failures);
        Assert.True(File.Exists(externalPath));
        Assert.False(Directory.Exists(Path.Combine(root, LibraryBackupService.BackupDirectoryName)));
    }

    [Fact]
    public async Task RenameFolderAsync_AllowsInboxAndRenamesPhysicalDirectory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var root = Path.Combine(database.RootDirectory, "Managed");
        var (library, organization) = CreateServices(database, root);
        await organization.EnsureDefaultsAsync(cancellationToken);
        var oldDirectory = Path.Combine(
            root,
            LibraryOrganizationDefaults.InboxFolderName,
            LibraryOrganizationDefaults.UnsortedName);
        var sourcePath = Path.Combine(oldDirectory, "收件.txt");
        await File.WriteAllTextAsync(sourcePath, "inbox", cancellationToken);
        var book = await AddManagedBookAsync(
            library,
            sourcePath,
            LibraryOrganizationDefaults.InboxFolderId,
            seriesId: null,
            cancellationToken);

        await organization.RenameFolderAsync(
            LibraryOrganizationDefaults.InboxFolderId,
            "导入箱",
            cancellationToken);

        var expectedPath = Path.Combine(root, "导入箱", LibraryOrganizationDefaults.UnsortedName, "收件.txt");
        Assert.False(Directory.Exists(Path.Combine(root, LibraryOrganizationDefaults.InboxFolderName)));
        Assert.True(File.Exists(expectedPath));
        var updated = Assert.IsType<BookDetails>(await library.GetBookAsync(book.Id, cancellationToken));
        Assert.Equal("导入箱", updated.FolderName);
        Assert.Equal(expectedPath, updated.FilePath, ignoreCase: true);
        Assert.True(Directory.EnumerateDirectories(
            Path.Combine(root, LibraryBackupService.BackupDirectoryName)).Any());
    }

    [Fact]
    public async Task RenameSeriesAsync_RenamesPhysicalDirectoryAndBookPath()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var root = Path.Combine(database.RootDirectory, "Managed");
        var (library, organization) = CreateServices(database, root);
        await organization.EnsureDefaultsAsync(cancellationToken);
        var folder = await organization.CreateFolderAsync("小说", cancellationToken);
        var series = await organization.CreateSeriesAsync(folder.Id, "旧系列", cancellationToken);
        var oldDirectory = Path.Combine(root, folder.Name, series.Name);
        var sourcePath = Path.Combine(oldDirectory, "第一卷.txt");
        await File.WriteAllTextAsync(sourcePath, "volume", cancellationToken);
        var book = await AddManagedBookAsync(
            library,
            sourcePath,
            folder.Id,
            series.Id,
            cancellationToken);

        await organization.RenameSeriesAsync(series.Id, "新系列", cancellationToken);

        var expectedPath = Path.Combine(root, folder.Name, "新系列", "第一卷.txt");
        Assert.False(Directory.Exists(oldDirectory));
        Assert.True(File.Exists(expectedPath));
        var updated = Assert.IsType<BookDetails>(await library.GetBookAsync(book.Id, cancellationToken));
        Assert.Equal("新系列", updated.SeriesName);
        Assert.Equal(expectedPath, updated.FilePath, ignoreCase: true);
    }

    [Fact]
    public async Task MoveSeriesAsync_MovesDirectoryAndUpdatesSeriesAndBooks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var root = Path.Combine(database.RootDirectory, "Managed");
        var (library, organization) = CreateServices(database, root);
        await organization.EnsureDefaultsAsync(cancellationToken);
        var sourceFolder = await organization.CreateFolderAsync("来源", cancellationToken);
        var targetFolder = await organization.CreateFolderAsync("目标", cancellationToken);
        var series = await organization.CreateSeriesAsync(sourceFolder.Id, "三部曲", cancellationToken);
        var oldDirectory = Path.Combine(root, sourceFolder.Name, series.Name);
        var sourcePath = Path.Combine(oldDirectory, "上.txt");
        await File.WriteAllTextAsync(sourcePath, "part one", cancellationToken);
        var book = await AddManagedBookAsync(
            library,
            sourcePath,
            sourceFolder.Id,
            series.Id,
            cancellationToken);

        var moved = await organization.MoveSeriesAsync(
            series.Id,
            targetFolder.Id,
            cancellationToken);

        var expectedPath = Path.Combine(root, targetFolder.Name, series.Name, "上.txt");
        Assert.Equal(series.Id, moved.Id);
        Assert.Equal(targetFolder.Id, moved.FolderId);
        Assert.False(Directory.Exists(oldDirectory));
        Assert.True(File.Exists(expectedPath));
        var updated = Assert.IsType<BookDetails>(await library.GetBookAsync(book.Id, cancellationToken));
        Assert.Equal(targetFolder.Id, updated.FolderId);
        Assert.Equal(series.Id, updated.SeriesId);
        Assert.Equal(expectedPath, updated.FilePath, ignoreCase: true);
    }

    [Fact]
    public async Task RenameEmptySeriesAndFolder_ReplacesPhysicalDirectories()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var root = Path.Combine(database.RootDirectory, "Managed");
        var (_, organization) = CreateServices(database, root);
        await organization.EnsureDefaultsAsync(cancellationToken);
        var folder = await organization.CreateFolderAsync("旧文件夹", cancellationToken);
        var series = await organization.CreateSeriesAsync(folder.Id, "旧系列", cancellationToken);

        await organization.RenameSeriesAsync(series.Id, "新系列", cancellationToken);
        await organization.RenameFolderAsync(folder.Id, "新文件夹", cancellationToken);

        Assert.False(Directory.Exists(Path.Combine(root, "旧文件夹")));
        Assert.False(Directory.Exists(Path.Combine(root, "新文件夹", "旧系列")));
        Assert.True(Directory.Exists(Path.Combine(root, "新文件夹", "新系列")));
        Assert.True(Directory.Exists(Path.Combine(
            root,
            "新文件夹",
            LibraryOrganizationDefaults.UnsortedName)));
        Assert.Equal(
            2,
            Directory.EnumerateDirectories(
                Path.Combine(root, LibraryBackupService.BackupDirectoryName)).Count());
    }

    [Fact]
    public async Task RenameUnsortedAsync_RenamesPhysicalDirectoryAndKeepsBooksUnassigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var root = Path.Combine(database.RootDirectory, "Managed");
        var (library, organization) = CreateServices(database, root);
        await organization.EnsureDefaultsAsync(cancellationToken);
        var folder = await organization.CreateFolderAsync("散文", cancellationToken);
        var oldDirectory = Path.Combine(root, folder.Name, folder.UnsortedName);
        var sourcePath = Path.Combine(oldDirectory, "随笔.txt");
        await File.WriteAllTextAsync(sourcePath, "essay", cancellationToken);
        var book = await AddManagedBookAsync(library, sourcePath, folder.Id, null, cancellationToken);

        await organization.RenameUnsortedAsync(folder.Id, "单行本", cancellationToken);

        var expectedPath = Path.Combine(root, folder.Name, "单行本", "随笔.txt");
        Assert.False(Directory.Exists(oldDirectory));
        Assert.True(File.Exists(expectedPath));
        var updated = Assert.IsType<BookDetails>(await library.GetBookAsync(book.Id, cancellationToken));
        Assert.Null(updated.SeriesId);
        Assert.Equal(expectedPath, updated.FilePath, ignoreCase: true);
        var updatedFolder = Assert.Single(await organization.GetFoldersAsync(cancellationToken), item => item.Id == folder.Id);
        Assert.Equal("单行本", updatedFolder.UnsortedName);
    }

    [Fact]
    public async Task DeleteSeriesAsync_MovesBooksToFolderUnsortedAndDeletesOnlySeries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var root = Path.Combine(database.RootDirectory, "Managed");
        var (library, organization) = CreateServices(database, root);
        await organization.EnsureDefaultsAsync(cancellationToken);
        var folder = await organization.CreateFolderAsync("小说", cancellationToken);
        await organization.RenameUnsortedAsync(folder.Id, "散本", cancellationToken);
        var series = await organization.CreateSeriesAsync(folder.Id, "三部曲", cancellationToken);
        var oldDirectory = Path.Combine(root, folder.Name, series.Name);
        var sourcePath = Path.Combine(oldDirectory, "上.txt");
        await File.WriteAllTextAsync(sourcePath, "part", cancellationToken);
        var book = await AddManagedBookAsync(library, sourcePath, folder.Id, series.Id, cancellationToken);

        await organization.DeleteSeriesAsync(series.Id, cancellationToken);

        var expectedPath = Path.Combine(root, folder.Name, "散本", "上.txt");
        Assert.False(Directory.Exists(oldDirectory));
        Assert.True(File.Exists(expectedPath));
        var updated = Assert.IsType<BookDetails>(await library.GetBookAsync(book.Id, cancellationToken));
        Assert.Equal(folder.Id, updated.FolderId);
        Assert.Null(updated.SeriesId);
        Assert.Equal(expectedPath, updated.FilePath, ignoreCase: true);
        var updatedFolder = Assert.Single(await organization.GetFoldersAsync(cancellationToken), item => item.Id == folder.Id);
        Assert.DoesNotContain(updatedFolder.Series, item => item.Id == series.Id);
        Assert.True(Directory.EnumerateDirectories(
            Path.Combine(root, LibraryBackupService.BackupDirectoryName)).Any());
    }

    [Fact]
    public async Task DeleteFolderAsync_MovesBooksToInboxAndRejectsDeletingInbox()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var root = Path.Combine(database.RootDirectory, "Managed");
        var (library, organization) = CreateServices(database, root);
        await organization.EnsureDefaultsAsync(cancellationToken);
        var folder = await organization.CreateFolderAsync("临时", cancellationToken);
        var series = await organization.CreateSeriesAsync(folder.Id, "待整理", cancellationToken);
        var oldDirectory = Path.Combine(root, folder.Name);
        var sourcePath = Path.Combine(oldDirectory, series.Name, "草稿.txt");
        await File.WriteAllTextAsync(sourcePath, "draft", cancellationToken);
        var book = await AddManagedBookAsync(library, sourcePath, folder.Id, series.Id, cancellationToken);

        await organization.DeleteFolderAsync(folder.Id, cancellationToken);

        var expectedPath = Path.Combine(
            root,
            LibraryOrganizationDefaults.InboxFolderName,
            LibraryOrganizationDefaults.UnsortedName,
            "草稿.txt");
        Assert.False(Directory.Exists(oldDirectory));
        Assert.True(File.Exists(expectedPath));
        var updated = Assert.IsType<BookDetails>(await library.GetBookAsync(book.Id, cancellationToken));
        Assert.Equal(LibraryOrganizationDefaults.InboxFolderId, updated.FolderId);
        Assert.Null(updated.SeriesId);
        Assert.Equal(expectedPath, updated.FilePath, ignoreCase: true);
        Assert.DoesNotContain(await organization.GetFoldersAsync(cancellationToken), item => item.Id == folder.Id);
        Assert.True(Directory.EnumerateDirectories(
            Path.Combine(root, LibraryBackupService.BackupDirectoryName)).Any());
        await Assert.ThrowsAsync<InvalidOperationException>(() => organization.DeleteFolderAsync(
            LibraryOrganizationDefaults.InboxFolderId,
            cancellationToken));
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
