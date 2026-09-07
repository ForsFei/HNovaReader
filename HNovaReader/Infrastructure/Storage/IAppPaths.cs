namespace HNovaReader.Infrastructure.Storage;

public interface IAppPaths
{
    string RootDirectory { get; }

    string DatabasePath { get; }

    string SettingsPath { get; }

    string CoversDirectory { get; }

    string CacheDirectory { get; }

    string ManagedLibraryDirectory { get; }

    void EnsureDirectories();
}
