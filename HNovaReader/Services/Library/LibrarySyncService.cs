using System.IO;
using HNovaReader.Data.Entities;
using HNovaReader.Infrastructure.Storage;
using HNovaReader.Services.Import;
using HNovaReader.Services.Settings;

namespace HNovaReader.Services.Library;

public sealed record LibrarySyncResult(int AddedCount, int RemovedCount, IReadOnlyList<string> Errors)
{
    public string Summary => $"书库更新完成：新增 {AddedCount} 本，移除 {RemovedCount} 本"
        + (Errors.Count == 0 ? string.Empty : $"，{Errors.Count} 项异常：{Errors[0]}");
}

public interface ILibrarySyncService
{
    Task<LibrarySyncResult> SyncAsync(CancellationToken cancellationToken);
}

/// <summary>Only removes database entries after a complete, accessible root scan; never deletes disk files.</summary>
public sealed class LibrarySyncService(
    ITextFileScanner scanner,
    ITextImportService importer,
    ILibraryService library,
    ILibraryOrganizationService organization,
    IAppSettingsService settings,
    IAppPaths appPaths) : ILibrarySyncService
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<LibrarySyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var root = Path.GetFullPath(settings.Current.LibraryFolder ?? appPaths.ManagedLibraryDirectory);
            // Check before GetFolders: that method creates default directories.
            if (!Directory.Exists(root))
            {
                return new(0, 0, ["书库根目录不存在或不可访问，已跳过同步。"]);
            }

            var before = await library.GetBooksAsync(cancellationToken);
            var storedPaths = before.Select(book => book.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var scan = await scanner.ScanAsync([root], true, null, cancellationToken);
            var errors = scan.Errors.Select(error => $"{error.Path}：{error.Message}").ToList();
            var added = 0;
            var removed = 0;
            foreach (var directory in scan.Directories ?? [])
            {
                try
                {
                    await organization.EnsureFolderPathAsync(Path.GetRelativePath(root, directory), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception) { errors.Add($"{directory}：{exception.Message}"); }
            }
            foreach (var path in scan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (storedPaths.Contains(path) || await library.ContainsFilePathAsync(path, cancellationToken))
                        continue;

                    var folder = await organization.EnsureFolderPathAsync(
                        Path.GetRelativePath(root, Path.GetDirectoryName(path)!), cancellationToken);
                    var result = await importer.ImportAsync(new TextImportRequest
                    {
                        Paths = [path],
                        PreserveDirectoryStructure = false,
                        StorageMode = BookStorageMode.ManagedCopy,
                        ManagedLibraryDirectory = root,
                        TargetFolderId = folder.Id,
                        FallbackEncoding = settings.Current.DefaultTextEncoding,
                    }, null, cancellationToken);
                    added += result.ImportedCount;
                    errors.AddRange(result.Items.Where(item => item.Status == TextImportItemStatus.Failed)
                        .Select(item => $"{path}：{item.Message}"));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception) { errors.Add($"{path}：{exception.Message}"); }
            }

            // A disappeared drive, partial scan, or changed root must never look like mass deletion.
            if (scan.Errors.Count == 0 && Directory.Exists(root)
                && root.Equals(Path.GetFullPath(settings.Current.LibraryFolder ?? appPaths.ManagedLibraryDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                var seen = scan.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var book in before.Where(book => book.StorageMode == BookStorageMode.ManagedCopy))
                {
                    var relative = Path.GetRelativePath(root, book.FilePath);
                    if (Path.IsPathRooted(relative) || relative == ".."
                        || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        || relative.Split(Path.DirectorySeparatorChar).Any(part =>
                            part.Equals(LibraryBackupService.BackupDirectoryName, StringComparison.OrdinalIgnoreCase))
                        || seen.Contains(book.FilePath))
                    {
                        continue;
                    }

                    try
                    {
                        _ = File.GetAttributes(book.FilePath);
                    }
                    catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
                    {
                        // Re-read to avoid deleting a book that the UI relocated during the scan.
                        var current = await library.GetBookAsync(book.Id, cancellationToken);
                        if (Directory.Exists(root) && current?.FilePath == book.FilePath
                            && await library.RemoveBookAsync(book.Id, cancellationToken))
                        {
                            removed++;
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        errors.Add($"{book.FilePath}：{exception.Message}");
                    }
                }
            }
            else
            {
                errors.Add("扫描不完整或书库根目录已变化，已跳过删除同步。");
            }

            if (scan.Errors.Count == 0 && Directory.Exists(root)
                && root.Equals(Path.GetFullPath(settings.Current.LibraryFolder ?? appPaths.ManagedLibraryDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                await organization.PruneMissingFoldersAsync(cancellationToken);
            }

            return new(added, removed, errors);
        }
        finally
        {
            gate.Release();
        }
    }
}
