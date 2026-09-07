using System.Text;
using HNovaReader.Data.Entities;
using HNovaReader.Infrastructure.Storage;
using HNovaReader.Services.Import;
using HNovaReader.Services.Library;
using HNovaReader.Services.Settings;
using HNovaReader.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HNovaReader.Tests.Services;

public sealed class TextImportServiceTests
{
    [Fact]
    public async Task ImportAsync_ImportsFolderInPlaceAndReportsCompletedProgress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var sourceDirectory = Path.Combine(database.RootDirectory, "Sources");
        Directory.CreateDirectory(sourceDirectory);
        var utf8Path = Path.Combine(sourceDirectory, "星海.txt");
        var gbPath = Path.Combine(sourceDirectory, "旧书.txt");
        await File.WriteAllTextAsync(utf8Path, "UTF-8 内容", cancellationToken);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        await File.WriteAllTextAsync(
            gbPath,
            string.Concat(Enumerable.Repeat("这是简体中文小说内容。", 100)),
            Encoding.GetEncoding("GB18030"),
            cancellationToken);
        var progressEvents = new List<TextImportProgress>();
        var service = CreateService(database);

        var result = await service.ImportAsync(
            new TextImportRequest
            {
                Paths = [sourceDirectory],
                StorageMode = BookStorageMode.OriginalLocation,
            },
            new InlineProgress<TextImportProgress>(progressEvents.Add),
            cancellationToken);

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.All(result.Items, item => Assert.Equal(item.SourcePath, item.StoredPath));
        Assert.All(
            result.Items,
            item => Assert.Equal(BookStorageMode.OriginalLocation, item.Book?.StorageMode));
        Assert.Contains(result.Items, item => item.Book?.Title == "星海");
        Assert.Contains(result.Items, item => item.Encoding?.EncodingName == "gb18030");
        Assert.Equal(TextImportStage.Completed, progressEvents[^1].Stage);
        Assert.Equal(2, progressEvents[^1].ProcessedCount);
        Assert.Equal(2, progressEvents[^1].ImportedCount);

        await using var dbContext = await database.DbContextFactory.CreateDbContextAsync(
            cancellationToken);
        Assert.Equal(2, await dbContext.Books.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task ImportAsync_ManagedCopyPreservesSourceAndSkipsRepeatedSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var sourceDirectory = Path.Combine(database.RootDirectory, "Sources");
        var managedDirectory = Path.Combine(database.RootDirectory, "Managed");
        var defaultTargetDirectory = Path.Combine(
            managedDirectory,
            LibraryOrganizationDefaults.InboxFolderName,
            LibraryOrganizationDefaults.UnsortedName);
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(defaultTargetDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "同名书.txt");
        await File.WriteAllTextAsync(sourcePath, "new content", cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(defaultTargetDirectory, "同名书.txt"),
            "existing content",
            cancellationToken);
        var service = CreateService(database);
        var request = new TextImportRequest
        {
            Paths = [sourcePath],
            StorageMode = BookStorageMode.ManagedCopy,
            ManagedLibraryDirectory = managedDirectory,
        };

        var first = await service.ImportAsync(request, progress: null, cancellationToken);
        var second = await service.ImportAsync(request, progress: null, cancellationToken);

        Assert.Equal(1, first.ImportedCount);
        Assert.Equal(1, second.SkippedCount);
        var imported = Assert.Single(first.Items);
        Assert.Equal(TextImportItemStatus.Imported, imported.Status);
        Assert.Equal(Path.GetFullPath(sourcePath), imported.Book?.SourceFilePath);
        Assert.Equal(BookStorageMode.ManagedCopy, imported.Book?.StorageMode);
        Assert.EndsWith("同名书 (2).txt", imported.StoredPath, StringComparison.Ordinal);
        Assert.True(File.Exists(sourcePath));
        Assert.Equal("new content", await File.ReadAllTextAsync(
            Assert.IsType<string>(imported.StoredPath),
            cancellationToken));
        Assert.Equal(2, Directory.EnumerateFiles(defaultTargetDirectory, "*.txt").Count());

        await using var dbContext = await database.DbContextFactory.CreateDbContextAsync(
            cancellationToken);
        Assert.Equal(1, await dbContext.Books.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task ImportAsync_ContinuesAfterScanErrorsAndReturnsSummary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var sourceDirectory = Path.Combine(database.RootDirectory, "Sources");
        Directory.CreateDirectory(sourceDirectory);
        var validPath = Path.Combine(sourceDirectory, "Valid.txt");
        var unsupportedPath = Path.Combine(sourceDirectory, "Cover.png");
        await File.WriteAllTextAsync(validPath, "valid", cancellationToken);
        await File.WriteAllTextAsync(unsupportedPath, "not png", cancellationToken);

        var result = await CreateService(database).ImportAsync(
            new TextImportRequest
            {
                Paths =
                [
                    validPath,
                    unsupportedPath,
                    Path.Combine(sourceDirectory, "Missing.txt"),
                ],
            },
            progress: null,
            cancellationToken);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Equal(3, result.Items.Count);
        Assert.Contains(result.Items, item => item.Message?.Contains("仅支持") == true);
        Assert.Contains(result.Items, item => item.Message?.Contains("不存在") == true);
    }

    [Fact]
    public async Task ImportAsync_CancellationBeforeSavingRemovesManagedCopy()
    {
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationSource.Token);
        var sourceDirectory = Path.Combine(database.RootDirectory, "Sources");
        var managedDirectory = Path.Combine(database.RootDirectory, "Managed");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "Cancel.txt");
        await File.WriteAllTextAsync(sourcePath, "cancel content", cancellationSource.Token);
        var cancellationRequested = false;
        var progress = new InlineProgress<TextImportProgress>(item =>
        {
            if (!cancellationRequested && item.Stage == TextImportStage.Saving)
            {
                cancellationRequested = true;
                cancellationSource.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(database).ImportAsync(
                new TextImportRequest
                {
                    Paths = [sourcePath],
                    StorageMode = BookStorageMode.ManagedCopy,
                    ManagedLibraryDirectory = managedDirectory,
                },
                progress,
                cancellationSource.Token));

        Assert.True(File.Exists(sourcePath));
        Assert.Empty(Directory.EnumerateFiles(managedDirectory, "*.txt"));
        await using var dbContext = await database.DbContextFactory.CreateDbContextAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(0, await dbContext.Books.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportAsync_UsesBatchEncodingOverride()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        var sourcePath = Path.Combine(database.RootDirectory, "Override.txt");
        await File.WriteAllTextAsync(sourcePath, "ASCII content", cancellationToken);

        var result = await CreateService(database).ImportAsync(
            new TextImportRequest
            {
                Paths = [sourcePath],
                EncodingOverride = "Big5",
            },
            progress: null,
            cancellationToken);

        var imported = Assert.Single(result.Items);
        Assert.Equal("big5", imported.Encoding?.EncodingName, ignoreCase: true);
        Assert.Equal(EncodingDetectionSource.UserOverride, imported.Encoding?.Source);
        Assert.Equal("big5", imported.Book?.FileEncoding, ignoreCase: true);
    }

    private static TextImportService CreateService(SqliteTestDatabase database)
    {
        var appPaths = new AppPaths(Path.Combine(database.RootDirectory, "AppData"));
        appPaths.EnsureDirectories();
        var libraryService = new LibraryService(
            database.DbContextFactory,
            new FilePathNormalizer(),
            TimeProvider.System);
        var settings = new StubAppSettingsService();
        settings.Set(new AppSettings { LibraryFolder = appPaths.ManagedLibraryDirectory });
        var organizationService = new LibraryOrganizationService(
            database.DbContextFactory,
            new FilePathNormalizer(),
            settings,
            appPaths,
            new LibraryBackupService(TimeProvider.System));

        return new TextImportService(
            new TextFileScanner(),
            new TextEncodingDetector(),
            new ManagedBookFileStore(),
            libraryService,
            organizationService,
            appPaths);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            report(value);
        }
    }
}
