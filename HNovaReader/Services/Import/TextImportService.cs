using System.IO;
using HNovaReader.Data.Entities;
using HNovaReader.Infrastructure.Storage;
using HNovaReader.Services.Library;

namespace HNovaReader.Services.Import;

public sealed class TextImportService(
    ITextFileScanner fileScanner,
    ITextEncodingDetector encodingDetector,
    IManagedBookFileStore managedFileStore,
    ILibraryService libraryService,
    ILibraryOrganizationService organizationService,
    IAppPaths appPaths) : ITextImportService
{
    public async Task<TextImportResult> ImportAsync(
        TextImportRequest request,
        IProgress<TextImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Paths);
        if (!Enum.IsDefined(request.StorageMode))
        {
            throw new ArgumentOutOfRangeException(nameof(request.StorageMode));
        }

        var encodingOverride = string.IsNullOrWhiteSpace(request.EncodingOverride)
            ? null
            : encodingDetector.GetUserOverride(request.EncodingOverride);
        var importTarget = request.StorageMode == BookStorageMode.ManagedCopy
            ? await organizationService.ResolveImportTargetAsync(
                request.TargetFolderId,
                request.TargetSeriesId,
                GetManagedLibraryDirectory(request.ManagedLibraryDirectory),
                cancellationToken)
            : null;

        var scanResult = await fileScanner.ScanAsync(
            request.Paths,
            request.IncludeSubdirectories,
            progress,
            cancellationToken);
        var items = scanResult.Errors
            .Select(error => new TextImportItemResult(
                error.Path,
                null,
                TextImportItemStatus.Failed,
                null,
                null,
                error.Message))
            .ToList();

        var importedCount = 0;
        var skippedCount = 0;
        var failedCount = scanResult.Errors.Count;
        var processedCount = 0;
        ReportProgress(
            progress,
            TextImportStage.Scanning,
            scanResult.Files.Count,
            scanResult.Files.Count,
            processedCount,
            importedCount,
            skippedCount,
            failedCount,
            null);

        foreach (var sourcePath in scanResult.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? copiedFilePath = null;
            TextEncodingDetectionResult? detectedEncoding = null;

            try
            {
                ReportProgress(
                    progress,
                    TextImportStage.CheckingDuplicate,
                    scanResult.Files.Count,
                    scanResult.Files.Count,
                    processedCount,
                    importedCount,
                    skippedCount,
                    failedCount,
                    sourcePath);

                if (await libraryService.ContainsFilePathAsync(sourcePath, cancellationToken))
                {
                    skippedCount++;
                    items.Add(new TextImportItemResult(
                        sourcePath,
                        null,
                        TextImportItemStatus.SkippedDuplicate,
                        null,
                        null,
                        "书库中已存在该源文件。"));
                    continue;
                }

                var sourceInfo = new FileInfo(sourcePath);
                sourceInfo.Refresh();
                if (!sourceInfo.Exists)
                {
                    throw new FileNotFoundException("扫描后源文件已不存在。", sourcePath);
                }

                ReportProgress(
                    progress,
                    TextImportStage.DetectingEncoding,
                    scanResult.Files.Count,
                    scanResult.Files.Count,
                    processedCount,
                    importedCount,
                    skippedCount,
                    failedCount,
                    sourcePath);
                detectedEncoding = encodingOverride
                    ?? await encodingDetector.DetectAsync(sourcePath, cancellationToken);
                if (encodingOverride is null
                    && detectedEncoding.Source == EncodingDetectionSource.Fallback
                    && !string.IsNullOrWhiteSpace(request.FallbackEncoding))
                {
                    detectedEncoding = encodingDetector.GetUserOverride(request.FallbackEncoding);
                }

                var fileTarget = importTarget;
                var managedRoot = GetManagedLibraryDirectory(request.ManagedLibraryDirectory);
                if (fileTarget is not null && IsPathWithinDirectory(sourcePath, managedRoot))
                {
                    var folder = await organizationService.EnsureFolderPathAsync(
                        Path.GetRelativePath(managedRoot, Path.GetDirectoryName(sourcePath)!), cancellationToken);
                    fileTarget = await organizationService.ResolveImportTargetAsync(
                        folder.Id, request.TargetSeriesId, managedRoot, cancellationToken);
                }
                else if (fileTarget is not null && request.PreserveDirectoryStructure)
                {
                    var sourceDirectory = request.Paths.Where(Directory.Exists)
                        .Select(Path.GetFullPath)
                        .Where(directory => IsPathWithinDirectory(sourcePath, directory))
                        .OrderByDescending(directory => directory.Length).FirstOrDefault();
                    if (sourceDirectory is not null)
                    {
                        var root = GetManagedLibraryDirectory(request.ManagedLibraryDirectory);
                        var relative = Path.Combine(
                            Path.GetRelativePath(root, fileTarget.DestinationDirectory),
                            Path.GetFileName(sourceDirectory.TrimEnd(Path.DirectorySeparatorChar)),
                            Path.GetRelativePath(sourceDirectory, Path.GetDirectoryName(sourcePath)!));
                        var folder = await organizationService.EnsureFolderPathAsync(relative, cancellationToken);
                        fileTarget = await organizationService.ResolveImportTargetAsync(
                            folder.Id, request.TargetSeriesId, root, cancellationToken);
                    }
                }
                var storedPath = sourcePath;
                if (fileTarget is not null
                    && !IsPathWithinDirectory(sourcePath, fileTarget.DestinationDirectory))
                {
                    ReportProgress(
                        progress,
                        TextImportStage.Copying,
                        scanResult.Files.Count,
                        scanResult.Files.Count,
                        processedCount,
                        importedCount,
                        skippedCount,
                        failedCount,
                        sourcePath);
                    storedPath = await managedFileStore.CopyAsync(
                        sourcePath,
                        fileTarget.DestinationDirectory,
                        cancellationToken);
                    copiedFilePath = storedPath;
                }

                ReportProgress(
                    progress,
                    TextImportStage.Saving,
                    scanResult.Files.Count,
                    scanResult.Files.Count,
                    processedCount,
                    importedCount,
                    skippedCount,
                    failedCount,
                    sourcePath);
                var book = await libraryService.AddBookAsync(
                    new AddBookRequest
                    {
                        FilePath = storedPath,
                        SourceFilePath = sourcePath,
                        Title = Path.GetFileNameWithoutExtension(sourcePath),
                        FileSize = sourceInfo.Length,
                        LastWriteTimeUtc = sourceInfo.LastWriteTimeUtc,
                        FileEncoding = detectedEncoding.EncodingName,
                        StorageMode = request.StorageMode,
                        FolderId = fileTarget?.FolderId,
                        SeriesId = fileTarget?.SeriesId,
                    },
                    cancellationToken);

                importedCount++;
                items.Add(new TextImportItemResult(
                    sourcePath,
                    storedPath,
                    TextImportItemStatus.Imported,
                    detectedEncoding,
                    book,
                    null));
                copiedFilePath = null;
            }
            catch (DuplicateBookException exception)
            {
                var cleanupError = TryDeleteCopy(copiedFilePath);
                skippedCount++;
                items.Add(new TextImportItemResult(
                    sourcePath,
                    null,
                    TextImportItemStatus.SkippedDuplicate,
                    detectedEncoding,
                    null,
                    AppendCleanupError(exception.Message, cleanupError)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryDeleteCopy(copiedFilePath);
                throw;
            }
            catch (Exception exception)
            {
                var cleanupError = TryDeleteCopy(copiedFilePath);
                failedCount++;
                items.Add(new TextImportItemResult(
                    sourcePath,
                    null,
                    TextImportItemStatus.Failed,
                    detectedEncoding,
                    null,
                    AppendCleanupError(exception.Message, cleanupError)));
            }
            finally
            {
                processedCount++;
                ReportProgress(
                    progress,
                    TextImportStage.Saving,
                    scanResult.Files.Count,
                    scanResult.Files.Count,
                    processedCount,
                    importedCount,
                    skippedCount,
                    failedCount,
                    sourcePath);
            }
        }

        ReportProgress(
            progress,
            TextImportStage.Completed,
            scanResult.Files.Count,
            scanResult.Files.Count,
            processedCount,
            importedCount,
            skippedCount,
            failedCount,
            null);
        return new TextImportResult(items);
    }

    private string? TryDeleteCopy(string? copiedFilePath)
    {
        if (copiedFilePath is null)
        {
            return null;
        }

        try
        {
            managedFileStore.DeleteCopy(copiedFilePath);
            return null;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    private string GetManagedLibraryDirectory(string? requestedDirectory)
    {
        return Path.GetFullPath(
            string.IsNullOrWhiteSpace(requestedDirectory)
                ? appPaths.ManagedLibraryDirectory
                : requestedDirectory.Trim());
    }

    private static bool IsPathWithinDirectory(string filePath, string directoryPath)
    {
        var relativePath = Path.GetRelativePath(directoryPath, filePath);
        return !Path.IsPathRooted(relativePath)
            && !relativePath.Equals("..", StringComparison.Ordinal)
            && !relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal);
    }

    private static string AppendCleanupError(string message, string? cleanupError)
    {
        return cleanupError is null
            ? message
            : $"{message} 副本清理失败：{cleanupError}";
    }

    private static void ReportProgress(
        IProgress<TextImportProgress>? progress,
        TextImportStage stage,
        int discoveredCount,
        int? totalCount,
        int processedCount,
        int importedCount,
        int skippedCount,
        int failedCount,
        string? currentPath)
    {
        progress?.Report(new TextImportProgress(
            stage,
            discoveredCount,
            totalCount,
            processedCount,
            importedCount,
            skippedCount,
            failedCount,
            currentPath));
    }
}
