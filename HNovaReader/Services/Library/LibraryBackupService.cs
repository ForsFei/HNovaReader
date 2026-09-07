using System.IO;
using System.Text.Json;

namespace HNovaReader.Services.Library;

public sealed class LibraryBackupService(TimeProvider timeProvider) : ILibraryBackupService
{
    public const string BackupDirectoryName = ".hnova-backups";
    private const int BufferSize = 128 * 1024;

    public async Task<LibraryBackupResult> BackupAsync(
        string libraryRoot,
        string operationName,
        IReadOnlyCollection<LibraryBackupItem> items,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(items);

        var root = Path.GetFullPath(libraryRoot);
        var backupRoot = Path.Combine(root, BackupDirectoryName);
        Directory.CreateDirectory(backupRoot);
        TryMarkHidden(backupRoot);

        var timestamp = timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss-fff");
        var sessionDirectory = GetUniqueDirectory(backupRoot, $"{timestamp}-{CleanSegment(operationName)}");
        Directory.CreateDirectory(sessionDirectory);

        var manifestItems = new List<object>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = Path.GetFullPath(item.SourcePath);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("无法备份：源 TXT 文件不存在。", sourcePath);
            }

            var relativePath = GetBackupRelativePath(root, sourcePath, item.BookId);
            var backupPath = Path.Combine(sessionDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            await CopyFileAsync(sourcePath, backupPath, cancellationToken);
            File.SetLastWriteTimeUtc(backupPath, File.GetLastWriteTimeUtc(sourcePath));
            manifestItems.Add(new
            {
                item.BookId,
                SourcePath = sourcePath,
                item.TargetPath,
                BackupPath = backupPath,
            });
        }

        var manifestPath = Path.Combine(sessionDirectory, "manifest.json");
        var manifest = new
        {
            Operation = operationName,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Items = manifestItems,
        };
        await using (var stream = new FileStream(
            manifestPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                manifest,
                new JsonSerializerOptions { WriteIndented = true },
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        return new LibraryBackupResult(sessionDirectory, manifestPath);
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, BufferSize, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private static string GetBackupRelativePath(string root, string sourcePath, Guid? bookId)
    {
        var relativePath = Path.GetRelativePath(root, sourcePath);
        if (!Path.IsPathRooted(relativePath)
            && !relativePath.Equals("..", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return relativePath;
        }

        return Path.Combine("外部文件", bookId?.ToString("N") ?? "unknown", Path.GetFileName(sourcePath));
    }

    private static string GetUniqueDirectory(string root, string baseName)
    {
        for (var suffix = 1; ; suffix++)
        {
            var name = suffix == 1 ? baseName : $"{baseName}-{suffix}";
            var path = Path.Combine(root, name);
            if (!Directory.Exists(path))
            {
                return path;
            }
        }
    }

    private static string CleanSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "operation" : cleaned.Trim();
    }

    private static void TryMarkHidden(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            File.SetAttributes(path, attributes | FileAttributes.Hidden);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
