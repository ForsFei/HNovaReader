using HNovaReader.Infrastructure.Storage;

namespace HNovaReader.Tests.Infrastructure;

[Collection("Environment variable tests")]
public sealed class AppPathsTests
{
    [Fact]
    public void EnsureDirectories_CreatesExpectedApplicationFolders()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = new AppPaths(root);

            paths.EnsureDirectories();

            Assert.True(Directory.Exists(paths.RootDirectory));
            Assert.True(Directory.Exists(paths.CoversDirectory));
            Assert.True(Directory.Exists(paths.CacheDirectory));
            Assert.True(Directory.Exists(paths.ManagedLibraryDirectory));
            Assert.Equal(Path.Combine(paths.RootDirectory, "library.db"), paths.DatabasePath);
            Assert.Equal(Path.Combine(paths.RootDirectory, "settings.json"), paths.SettingsPath);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void DefaultConstructor_UsesExplicitQaRootOverride()
    {
        var root = CreateTempRoot();
        var originalValue = Environment.GetEnvironmentVariable(
            AppPaths.RootOverrideEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                AppPaths.RootOverrideEnvironmentVariable,
                root);

            var paths = new AppPaths();

            Assert.Equal(Path.GetFullPath(root), paths.RootDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AppPaths.RootOverrideEnvironmentVariable,
                originalValue);
            DeleteTempRoot(root);
        }
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
}

[CollectionDefinition("Environment variable tests", DisableParallelization = true)]
public sealed class EnvironmentVariableTestCollection;
