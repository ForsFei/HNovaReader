using System.IO;
using System.Security;
using HNovaReader.Services.Library;

namespace HNovaReader.Services.Import;

public sealed class TextFileScanner : ITextFileScanner
{
    public Task<TextFileScanResult> ScanAsync(
        IReadOnlyCollection<string> paths,
        bool includeSubdirectories,
        IProgress<TextImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return Task.Run(
            () => ScanCore(paths, includeSubdirectories, progress, cancellationToken),
            cancellationToken);
    }

    private static TextFileScanResult ScanCore(
        IReadOnlyCollection<string> paths,
        bool includeSubdirectories,
        IProgress<TextImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<FileScanError>();

        foreach (var inputPath in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                errors.Add(new FileScanError(string.Empty, "路径不能为空。"));
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(inputPath.Trim());
            }
            catch (Exception exception) when (IsExpectedFileSystemException(exception))
            {
                errors.Add(new FileScanError(inputPath, exception.Message));
                continue;
            }

            if (File.Exists(fullPath))
            {
                if (IsTextFile(fullPath))
                {
                    AddFile(fullPath, files, progress);
                }
                else
                {
                    errors.Add(new FileScanError(fullPath, "仅支持 .txt 文件。"));
                }

                continue;
            }

            if (Directory.Exists(fullPath))
            {
                ScanDirectory(
                    fullPath,
                    includeSubdirectories,
                    files,
                    errors,
                    progress,
                    cancellationToken);
                continue;
            }

            errors.Add(new FileScanError(fullPath, "文件或文件夹不存在。"));
        }

        return new TextFileScanResult(
            files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            errors);
    }

    private static void ScanDirectory(
        string rootDirectory,
        bool includeSubdirectories,
        HashSet<string> files,
        List<FileScanError> errors,
        IProgress<TextImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootDirectory);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = pendingDirectories.Pop();

            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(currentDirectory);
            }
            catch (Exception exception) when (IsExpectedFileSystemException(exception))
            {
                errors.Add(new FileScanError(currentDirectory, exception.Message));
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (includeSubdirectories
                            && (attributes & FileAttributes.ReparsePoint) == 0
                            && !Path.GetFileName(entry).Equals(
                                LibraryBackupService.BackupDirectoryName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            pendingDirectories.Push(entry);
                        }

                        continue;
                    }

                    if (IsTextFile(entry))
                    {
                        AddFile(Path.GetFullPath(entry), files, progress);
                    }
                }
                catch (Exception exception) when (IsExpectedFileSystemException(exception))
                {
                    errors.Add(new FileScanError(entry, exception.Message));
                }
            }
        }
    }

    private static void AddFile(
        string filePath,
        HashSet<string> files,
        IProgress<TextImportProgress>? progress)
    {
        if (!files.Add(filePath))
        {
            return;
        }

        progress?.Report(new TextImportProgress(
            TextImportStage.Scanning,
            files.Count,
            null,
            0,
            0,
            0,
            0,
            filePath));
    }

    private static bool IsTextFile(string filePath)
    {
        return string.Equals(
            Path.GetExtension(filePath),
            ".txt",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpectedFileSystemException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException
            or NotSupportedException;
    }
}
