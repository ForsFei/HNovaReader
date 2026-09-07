using HNovaReader.Features.Library;
using HNovaReader.Infrastructure.Storage;

namespace HNovaReader.Tests.Features;

public sealed class CoverFileStoreTests
{
    [Fact]
    public async Task CopyAsync_CopiesIntoManagedDirectoryAndPreservesSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureDirectories();
            var sourcePath = Path.Combine(root, "source.png");
            await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4], cancellationToken);
            var store = new CoverFileStore(paths);

            var copiedPath = await store.CopyAsync(Guid.NewGuid(), sourcePath, cancellationToken);

            Assert.True(File.Exists(sourcePath));
            Assert.True(File.Exists(copiedPath));
            Assert.StartsWith(paths.CoversDirectory, copiedPath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                await File.ReadAllBytesAsync(sourcePath, cancellationToken),
                await File.ReadAllBytesAsync(copiedPath, cancellationToken));

            store.DeleteManagedCopy(copiedPath);
            Assert.False(File.Exists(copiedPath));
            Assert.True(File.Exists(sourcePath));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task CopyAsync_RejectsUnsupportedCoverType()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureDirectories();
            var sourcePath = Path.Combine(root, "cover.gif");
            await File.WriteAllBytesAsync(sourcePath, [1], cancellationToken);
            var store = new CoverFileStore(paths);

            await Assert.ThrowsAsync<NotSupportedException>(() =>
                store.CopyAsync(Guid.NewGuid(), sourcePath, cancellationToken));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "HNovaReader.Tests", Guid.NewGuid().ToString("N"));
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
