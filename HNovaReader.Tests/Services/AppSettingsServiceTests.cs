using HNovaReader.Data.Entities;
using HNovaReader.Infrastructure.Storage;
using HNovaReader.Services.Settings;
using HNovaReader.Services.Theming;

namespace HNovaReader.Tests.Services;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsVersionedSettingsWithoutTemporaryFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureDirectories();
            var service = new JsonAppSettingsService(paths, TimeProvider.System);
            var settings = new AppSettings
            {
                Theme = AppTheme.Light,
                ReaderTheme = ReaderTheme.Dark,
                FontFamily = "霞鹜文楷",
                FontSize = 22,
                LineHeight = 1.9,
                ParagraphSpacing = 8,
                MaximumContentWidth = 920,
                HorizontalMargin = 72,
                ReadingMode = ReadingModePreference.Paged,
                LibraryFolder = Path.Combine(root, "managed-books"),
                ImportBehavior = BookStorageMode.ManagedCopy,
                RememberWindowSize = true,
                DefaultTextEncoding = "GB18030",
                WindowWidth = 1500,
                WindowHeight = 960,
            };

            await service.SaveAsync(settings, cancellationToken);
            var reloadedService = new JsonAppSettingsService(paths, TimeProvider.System);
            var loaded = await reloadedService.LoadAsync(cancellationToken);

            Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.Equal(AppTheme.Light, loaded.Theme);
            Assert.Equal(ReaderTheme.Dark, loaded.ReaderTheme);
            Assert.Equal("霞鹜文楷", loaded.FontFamily);
            Assert.Equal(22, loaded.FontSize);
            Assert.Equal(ReadingModePreference.Paged, loaded.ReadingMode);
            Assert.Equal(BookStorageMode.ManagedCopy, loaded.ImportBehavior);
            Assert.Equal("GB18030", loaded.DefaultTextEncoding);
            Assert.Equal(1500, loaded.WindowWidth);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task LoadAsync_WhenJsonIsCorrupt_PreservesFileAndReturnsDefaults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureDirectories();
            await File.WriteAllTextAsync(paths.SettingsPath, "{ not-json", cancellationToken);
            var service = new JsonAppSettingsService(
                paths,
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero)));

            var loaded = await service.LoadAsync(cancellationToken);

            Assert.Equal(new AppSettings(), loaded);
            Assert.False(File.Exists(paths.SettingsPath));
            Assert.Single(Directory.GetFiles(root, "settings.json.corrupt-*"));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task LoadAsync_WhenLibraryPathIsInvalid_PreservesFileAndReturnsDefaults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureDirectories();
            await File.WriteAllTextAsync(
                paths.SettingsPath,
                "{\"libraryFolder\":\"\\u0000\"}",
                cancellationToken);
            var service = new JsonAppSettingsService(paths, TimeProvider.System);

            var loaded = await service.LoadAsync(cancellationToken);

            Assert.Equal(new AppSettings(), loaded);
            Assert.False(File.Exists(paths.SettingsPath));
            Assert.Single(Directory.GetFiles(root, "settings.json.corrupt-*"));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task SaveAsync_ClampsUnsafeNumericValues()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateRoot();
        try
        {
            var service = new JsonAppSettingsService(new AppPaths(root), TimeProvider.System);

            await service.SaveAsync(
                new AppSettings
                {
                    FontSize = 200,
                    LineHeight = double.NaN,
                    HorizontalMargin = -20,
                    WindowWidth = 100,
                },
                cancellationToken);

            Assert.Equal(30, service.Current.FontSize);
            Assert.Equal(1.75, service.Current.LineHeight);
            Assert.Equal(24, service.Current.HorizontalMargin);
            Assert.Equal(1080, service.Current.WindowWidth);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        return Path.Combine(Path.GetTempPath(), "HNovaReader.Tests", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteRoot(string root)
    {
        var safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "HNovaReader.Tests"));
        var resolved = Path.GetFullPath(root);
        if (resolved.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
