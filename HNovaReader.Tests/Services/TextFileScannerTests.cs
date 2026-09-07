using HNovaReader.Services.Import;

namespace HNovaReader.Tests.Services;

public sealed class TextFileScannerTests
{
    [Fact]
    public async Task ScanAsync_RecursivelyFindsTxtFilesAndDeduplicatesInputs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        var nested = Path.Combine(root, "Nested");
        Directory.CreateDirectory(nested);

        try
        {
            var rootBook = Path.Combine(root, "Root.TXT");
            var nestedBook = Path.Combine(nested, "Nested.txt");
            await File.WriteAllTextAsync(rootBook, "root", cancellationToken);
            await File.WriteAllTextAsync(nestedBook, "nested", cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(nested, "Ignored.md"),
                "ignored",
                cancellationToken);
            var progressEvents = new List<TextImportProgress>();

            var result = await new TextFileScanner().ScanAsync(
                [root, rootBook],
                includeSubdirectories: true,
                new InlineProgress<TextImportProgress>(progressEvents.Add),
                cancellationToken);

            Assert.Empty(result.Errors);
            Assert.Equal(2, result.Files.Count);
            Assert.Contains(Path.GetFullPath(rootBook), result.Files);
            Assert.Contains(Path.GetFullPath(nestedBook), result.Files);
            Assert.Equal(2, progressEvents[^1].DiscoveredCount);
            Assert.All(
                progressEvents,
                item => Assert.Equal(TextImportStage.Scanning, item.Stage));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ScanAsync_CanLimitFolderImportToTopDirectory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        var nested = Path.Combine(root, "Nested");
        Directory.CreateDirectory(nested);

        try
        {
            var rootBook = Path.Combine(root, "Root.txt");
            await File.WriteAllTextAsync(rootBook, "root", cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(nested, "Nested.txt"),
                "nested",
                cancellationToken);

            var result = await new TextFileScanner().ScanAsync(
                [root],
                includeSubdirectories: false,
                progress: null,
                cancellationToken);

            Assert.Equal([Path.GetFullPath(rootBook)], result.Files);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ScanAsync_AlwaysExcludesHiddenLibraryBackups()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        var backup = Path.Combine(root, ".hnova-backups", "session");
        Directory.CreateDirectory(backup);

        try
        {
            var visibleBook = Path.Combine(root, "Visible.txt");
            await File.WriteAllTextAsync(visibleBook, "visible", cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(backup, "Backup.txt"),
                "backup",
                cancellationToken);

            var result = await new TextFileScanner().ScanAsync(
                [root],
                includeSubdirectories: true,
                progress: null,
                cancellationToken);

            Assert.Equal([Path.GetFullPath(visibleBook)], result.Files);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ScanAsync_ReturnsErrorsForMissingAndUnsupportedPaths()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        Directory.CreateDirectory(root);

        try
        {
            var unsupported = Path.Combine(root, "Cover.png");
            await File.WriteAllTextAsync(unsupported, "not an image", cancellationToken);

            var result = await new TextFileScanner().ScanAsync(
                [unsupported, Path.Combine(root, "Missing.txt")],
                includeSubdirectories: true,
                progress: null,
                cancellationToken);

            Assert.Empty(result.Files);
            Assert.Equal(2, result.Errors.Count);
            Assert.Contains(result.Errors, error => error.Message.Contains(".txt"));
            Assert.Contains(result.Errors, error => error.Message.Contains("不存在"));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ScanAsync_ObservesCancellationBeforeScanning()
    {
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TextFileScanner().ScanAsync(
                [CreateTempRoot()],
                includeSubdirectories: true,
                progress: null,
                cancellationSource.Token));
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "HNovaReader.Tests",
            Guid.NewGuid().ToString("N"));
    }

    private static void DeleteTempRoot(string root)
    {
        var safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "HNovaReader.Tests"));
        var resolvedRoot = Path.GetFullPath(root);
        if (resolvedRoot.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(resolvedRoot))
        {
            Directory.Delete(resolvedRoot, recursive: true);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            report(value);
        }
    }
}
