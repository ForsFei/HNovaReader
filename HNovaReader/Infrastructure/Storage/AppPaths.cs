using System.IO;

namespace HNovaReader.Infrastructure.Storage;

public sealed class AppPaths : IAppPaths
{
    public const string RootOverrideEnvironmentVariable = "HNOVAREADER_APP_DATA_ROOT";

    public AppPaths()
        : this(ResolveDefaultRootDirectory())
    {
    }

    public AppPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
        DatabasePath = Path.Combine(RootDirectory, "library.db");
        SettingsPath = Path.Combine(RootDirectory, "settings.json");
        CoversDirectory = Path.Combine(RootDirectory, "covers");
        CacheDirectory = Path.Combine(RootDirectory, "cache");
        ManagedLibraryDirectory = Path.Combine(RootDirectory, "library");
    }

    public string RootDirectory { get; }

    public string DatabasePath { get; }

    public string SettingsPath { get; }

    public string CoversDirectory { get; }

    public string CacheDirectory { get; }

    public string ManagedLibraryDirectory { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(CoversDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(ManagedLibraryDirectory);
    }

    private static string ResolveDefaultRootDirectory()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(RootOverrideEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HNovaReader")
            : configuredRoot;
    }
}
