using HNovaReader.Features.Settings;
using HNovaReader.Infrastructure.Storage;
using HNovaReader.Services.Settings;

namespace HNovaReader.Tests.TestSupport;

public sealed class StubAppSettingsService : IAppSettingsService
{
    public AppSettings Current { get; private set; } = new();

    public Exception? SaveException { get; set; }

    public event EventHandler? SettingsChanged;

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Current);
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SaveException is not null)
        {
            return Task.FromException(SaveException);
        }

        Current = settings;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Set(AppSettings settings)
    {
        Current = settings;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class StubSettingsInteractionService : ISettingsInteractionService
{
    public string? SelectedLibraryFolder { get; set; }

    public string? LastCurrentPath { get; private set; }

    public bool ConfirmMigrationResult { get; set; } = true;

    public string? PickLibraryFolder(string? currentPath)
    {
        LastCurrentPath = currentPath;
        return SelectedLibraryFolder;
    }

    public bool ConfirmLibraryMigration(string sourcePath, string targetPath)
    {
        return ConfirmMigrationResult;
    }
}

public sealed class StubAppPaths : IAppPaths
{
    public string RootDirectory { get; init; } = Path.Combine(Path.GetTempPath(), "HNovaReader.Tests");
    public string DatabasePath => Path.Combine(RootDirectory, "library.db");
    public string SettingsPath => Path.Combine(RootDirectory, "settings.json");
    public string CoversDirectory => Path.Combine(RootDirectory, "covers");
    public string CacheDirectory => Path.Combine(RootDirectory, "cache");
    public string ManagedLibraryDirectory => Path.Combine(RootDirectory, "library");
    public void EnsureDirectories() => Directory.CreateDirectory(RootDirectory);
}
