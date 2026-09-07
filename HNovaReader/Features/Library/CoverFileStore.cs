using System.IO;
using HNovaReader.Infrastructure.Storage;

namespace HNovaReader.Features.Library;

public sealed class CoverFileStore(IAppPaths appPaths) : ICoverFileStore
{
    private static readonly HashSet<string> SupportedExtensions = new(
        [".jpg", ".jpeg", ".png", ".webp"],
        StringComparer.OrdinalIgnoreCase);

    public async Task<string> CopyAsync(
        Guid bookId,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var extension = Path.GetExtension(fullSourcePath);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new NotSupportedException("封面仅支持 JPG、PNG 或 WEBP 文件。");
        }

        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("找不到选择的封面文件。", fullSourcePath);
        }

        Directory.CreateDirectory(appPaths.CoversDirectory);
        var destinationPath = Path.Combine(
            appPaths.CoversDirectory,
            $"{bookId:N}-{Guid.NewGuid():N}{extension.ToLowerInvariant()}");

        try
        {
            await using var source = new FileStream(
                fullSourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            return destinationPath;
        }
        catch
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            throw;
        }
    }

    public void DeleteManagedCopy(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(filePath);
        var coversRoot = Path.GetFullPath(appPaths.CoversDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(coversRoot, StringComparison.OrdinalIgnoreCase)
            && File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }
}
