using System.IO;
using HNovaReader.Data;
using HNovaReader.Data.Entities;
using HNovaReader.Infrastructure.Storage;
using HNovaReader.Services.Settings;
using Microsoft.EntityFrameworkCore;

namespace HNovaReader.Services.Library;

public sealed class LibraryOrganizationService(
    IDbContextFactory<HNovaReaderDbContext> dbContextFactory,
    IFilePathNormalizer filePathNormalizer,
    IAppSettingsService settingsService,
    IAppPaths appPaths,
    ILibraryBackupService backupService) : ILibraryOrganizationService
{
    private const int CopyBufferSize = 128 * 1024;

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var inbox = await dbContext.LibraryFolders.SingleOrDefaultAsync(
            folder => folder.Id == LibraryOrganizationDefaults.InboxFolderId,
            cancellationToken);
        if (inbox is null)
        {
            inbox = new LibraryFolder
            {
                Id = LibraryOrganizationDefaults.InboxFolderId,
                Name = LibraryOrganizationDefaults.InboxFolderName,
                NormalizedName = LibraryNameRules.Normalize(LibraryOrganizationDefaults.InboxFolderName),
                UnsortedName = LibraryOrganizationDefaults.UnsortedName,
                SortOrder = int.MinValue,
                IsSystem = true,
            };
            dbContext.LibraryFolders.Add(inbox);
        }

        var orphanSeries = await dbContext.Series
            .Where(series => series.FolderId == null)
            .ToListAsync(cancellationToken);
        foreach (var series in orphanSeries)
        {
            series.FolderId = inbox.Id;
        }

        var orphanBooks = await dbContext.Books
            .Where(book => book.FolderId == null)
            .ToListAsync(cancellationToken);
        var seriesFolders = await dbContext.Series
            .Where(series => series.FolderId != null)
            .ToDictionaryAsync(series => series.Id, series => series.FolderId, cancellationToken);
        foreach (var book in orphanBooks)
        {
            book.FolderId = book.SeriesId is { } seriesId
                && seriesFolders.TryGetValue(seriesId, out var folderId)
                ? folderId
                : inbox.Id;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        EnsureOrganizationDirectory(GetLibraryRoot(), inbox.Name, inbox.UnsortedName);
    }

    public async Task<IReadOnlyList<LibraryFolderDetails>> GetFoldersAsync(
        CancellationToken cancellationToken)
    {
        await EnsureDefaultsAsync(cancellationToken);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var folders = await dbContext.LibraryFolders
            .AsNoTracking()
            .Include(folder => folder.Series)
            .Include(folder => folder.Books)
            .OrderBy(folder => folder.SortOrder)
            .ThenBy(folder => folder.Name)
            .ToListAsync(cancellationToken);

        return folders.Select(folder => new LibraryFolderDetails(
            folder.Id,
            folder.Name,
            folder.SortOrder,
            folder.IsSystem,
            folder.UnsortedName,
            folder.Books.Count(book => book.SeriesId == null),
            folder.Books.Count,
            folder.Series
                .OrderBy(series => series.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(series => new LibrarySeriesDetails(
                    series.Id,
                    folder.Id,
                    series.Name,
                    folder.Books.Count(book => book.SeriesId == series.Id)))
                .ToArray()))
            .ToArray();
    }

    public async Task<LibraryFolderDetails> CreateFolderAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var cleanedName = LibraryNameRules.Clean(name, nameof(name));
        var normalizedName = LibraryNameRules.Normalize(cleanedName);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await dbContext.LibraryFolders.AnyAsync(
                folder => folder.NormalizedName == normalizedName,
                cancellationToken))
        {
            throw new InvalidOperationException($"书架文件夹“{cleanedName}”已经存在。");
        }

        var nextSortOrder = (await dbContext.LibraryFolders.MaxAsync(
            folder => (int?)folder.SortOrder,
            cancellationToken) ?? 0) + 1;
        var folder = new LibraryFolder
        {
            Id = Guid.NewGuid(),
            Name = cleanedName,
            NormalizedName = normalizedName,
            UnsortedName = LibraryOrganizationDefaults.UnsortedName,
            SortOrder = nextSortOrder,
        };
        var directory = EnsureOrganizationDirectory(GetLibraryRoot(), cleanedName, folder.UnsortedName);
        dbContext.LibraryFolders.Add(folder);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            TryDeleteEmptyDirectory(Path.GetDirectoryName(directory));
            throw;
        }

        return new LibraryFolderDetails(
            folder.Id,
            folder.Name,
            folder.SortOrder,
            false,
            folder.UnsortedName,
            0,
            0,
            []);
    }

    public async Task<LibrarySeriesDetails> CreateSeriesAsync(
        Guid folderId,
        string name,
        CancellationToken cancellationToken)
    {
        var cleanedName = LibraryNameRules.Clean(name, nameof(name));
        var normalizedName = LibraryNameRules.Normalize(cleanedName);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var folder = await dbContext.LibraryFolders.SingleOrDefaultAsync(
            candidate => candidate.Id == folderId,
            cancellationToken) ?? throw new InvalidOperationException("目标书架文件夹不存在。");
        if (LibraryNameRules.Normalize(folder.UnsortedName) == normalizedName)
        {
            throw new InvalidOperationException($"“{cleanedName}”是该书架的未分系列名称。");
        }

        if (await dbContext.Series.AnyAsync(
                series => series.FolderId == folderId && series.NormalizedName == normalizedName,
                cancellationToken))
        {
            throw new InvalidOperationException($"该书架下已经存在系列“{cleanedName}”。");
        }

        var series = new Series
        {
            Id = Guid.NewGuid(),
            FolderId = folder.Id,
            Name = cleanedName,
            NormalizedName = normalizedName,
        };
        var directory = EnsureOrganizationDirectory(GetLibraryRoot(), folder.Name, series.Name);
        dbContext.Series.Add(series);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            TryDeleteEmptyDirectory(directory);
            throw;
        }

        return new LibrarySeriesDetails(series.Id, folder.Id, series.Name, 0);
    }

    public async Task RenameFolderAsync(
        Guid folderId,
        string name,
        CancellationToken cancellationToken)
    {
        var cleanedName = LibraryNameRules.Clean(name, nameof(name));
        await using var dbContext = await CreateOrganizationContextAsync(cancellationToken);
        var folder = await dbContext.LibraryFolders
            .Include(candidate => candidate.Series)
            .Include(candidate => candidate.Books)
            .SingleOrDefaultAsync(candidate => candidate.Id == folderId, cancellationToken)
            ?? throw new InvalidOperationException("书架文件夹不存在。");
        var normalizedName = LibraryNameRules.Normalize(cleanedName);
        if (await dbContext.LibraryFolders.AnyAsync(
                candidate => candidate.Id != folderId && candidate.NormalizedName == normalizedName,
                cancellationToken))
        {
            throw new InvalidOperationException($"书架文件夹“{cleanedName}”已经存在。");
        }

        var oldName = folder.Name;
        if (string.Equals(oldName, cleanedName, StringComparison.Ordinal))
        {
            return;
        }

        var root = GetLibraryRoot();
        var oldDirectory = Path.Combine(root, oldName);
        var seriesNames = folder.Series.ToDictionary(series => series.Id, series => series.Name);
        EnsureOrganizationDirectory(root, cleanedName, folder.UnsortedName);
        foreach (var seriesName in seriesNames.Values)
        {
            EnsureOrganizationDirectory(root, cleanedName, seriesName);
        }

        folder.Name = cleanedName;
        folder.NormalizedName = normalizedName;
        await MoveAndSaveAsync(
            dbContext,
            folder.Books.Where(IsManagedExisting).ToArray(),
            book => BuildDestinationPath(
                root,
                cleanedName,
                book.SeriesId is { } seriesId && seriesNames.TryGetValue(seriesId, out var seriesName)
                    ? seriesName
                    : folder.UnsortedName,
                Path.GetFileName(book.FilePath)),
            $"重命名书架-{oldName}",
            cancellationToken);
        TryDeleteEmptyDirectoryTree(oldDirectory);
    }

    public async Task RenameSeriesAsync(
        Guid seriesId,
        string name,
        CancellationToken cancellationToken)
    {
        var cleanedName = LibraryNameRules.Clean(name, nameof(name));
        await using var dbContext = await CreateOrganizationContextAsync(cancellationToken);
        var series = await dbContext.Series
            .Include(candidate => candidate.Folder)
            .Include(candidate => candidate.Books)
            .SingleOrDefaultAsync(candidate => candidate.Id == seriesId, cancellationToken)
            ?? throw new InvalidOperationException("系列不存在。");
        var normalizedName = LibraryNameRules.Normalize(cleanedName);
        if (series.Folder is not null
            && LibraryNameRules.Normalize(series.Folder.UnsortedName) == normalizedName)
        {
            throw new InvalidOperationException($"“{cleanedName}”是该书架的未分系列名称。");
        }

        if (await dbContext.Series.AnyAsync(
                candidate => candidate.Id != seriesId
                    && candidate.FolderId == series.FolderId
                    && candidate.NormalizedName == normalizedName,
                cancellationToken))
        {
            throw new InvalidOperationException($"该书架下已经存在系列“{cleanedName}”。");
        }

        var oldName = series.Name;
        if (string.Equals(oldName, cleanedName, StringComparison.Ordinal))
        {
            return;
        }

        var root = GetLibraryRoot();
        var folderName = series.Folder?.Name ?? LibraryOrganizationDefaults.InboxFolderName;
        var oldDirectory = Path.Combine(root, folderName, oldName);
        EnsureOrganizationDirectory(root, folderName, cleanedName);
        series.Name = cleanedName;
        series.NormalizedName = normalizedName;
        await MoveAndSaveAsync(
            dbContext,
            series.Books.Where(IsManagedExisting).ToArray(),
            book => BuildDestinationPath(
                root,
                folderName,
                cleanedName,
                Path.GetFileName(book.FilePath)),
            $"重命名系列-{oldName}",
            cancellationToken);
        TryDeleteEmptyDirectoryTree(oldDirectory);
    }

    public async Task RenameUnsortedAsync(
        Guid folderId,
        string name,
        CancellationToken cancellationToken)
    {
        var cleanedName = LibraryNameRules.Clean(name, nameof(name));
        var normalizedName = LibraryNameRules.Normalize(cleanedName);
        await using var dbContext = await CreateOrganizationContextAsync(cancellationToken);
        var folder = await dbContext.LibraryFolders
            .Include(candidate => candidate.Books)
            .SingleOrDefaultAsync(candidate => candidate.Id == folderId, cancellationToken)
            ?? throw new InvalidOperationException("书架文件夹不存在。");
        if (await dbContext.Series.AnyAsync(
                series => series.FolderId == folderId && series.NormalizedName == normalizedName,
                cancellationToken))
        {
            throw new InvalidOperationException($"该书架下已经存在系列“{cleanedName}”。");
        }

        var oldName = folder.UnsortedName;
        if (string.Equals(oldName, cleanedName, StringComparison.Ordinal))
        {
            return;
        }

        var root = GetLibraryRoot();
        var oldDirectory = Path.Combine(root, folder.Name, oldName);
        EnsureOrganizationDirectory(root, folder.Name, cleanedName);
        folder.UnsortedName = cleanedName;
        var books = folder.Books
            .Where(book => book.SeriesId == null && IsManagedExisting(book))
            .ToArray();
        await MoveAndSaveAsync(
            dbContext,
            books,
            book => BuildDestinationPath(
                root,
                folder.Name,
                cleanedName,
                Path.GetFileName(book.FilePath)),
            $"重命名未分系列-{oldName}",
            cancellationToken);
        TryDeleteEmptyDirectoryTree(oldDirectory);
    }

    public async Task DeleteSeriesAsync(Guid seriesId, CancellationToken cancellationToken)
    {
        await using var dbContext = await CreateOrganizationContextAsync(cancellationToken);
        var series = await dbContext.Series
            .Include(candidate => candidate.Folder)
            .Include(candidate => candidate.Books)
            .SingleOrDefaultAsync(candidate => candidate.Id == seriesId, cancellationToken)
            ?? throw new InvalidOperationException("系列不存在。");
        var folder = series.Folder
            ?? throw new InvalidOperationException("系列所属的书架文件夹不存在。");
        var root = GetLibraryRoot();
        var oldDirectory = Path.Combine(root, folder.Name, series.Name);
        EnsureOrganizationDirectory(root, folder.Name, folder.UnsortedName);
        foreach (var book in series.Books)
        {
            book.SeriesId = null;
            book.SeriesIndex = null;
        }

        dbContext.Series.Remove(series);
        await MoveAndSaveAsync(
            dbContext,
            series.Books.Where(IsManagedExisting).ToArray(),
            book => BuildDestinationPath(
                root,
                folder.Name,
                folder.UnsortedName,
                Path.GetFileName(book.FilePath)),
            $"删除系列-{series.Name}",
            cancellationToken);
        TryDeleteEmptyDirectoryTree(oldDirectory);
    }

    public async Task DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken)
    {
        await using var dbContext = await CreateOrganizationContextAsync(cancellationToken);
        var folder = await dbContext.LibraryFolders
            .Include(candidate => candidate.Series)
            .Include(candidate => candidate.Books)
            .SingleOrDefaultAsync(candidate => candidate.Id == folderId, cancellationToken)
            ?? throw new InvalidOperationException("书架文件夹不存在。");
        if (folder.IsSystem)
        {
            throw new InvalidOperationException("系统收件箱不能删除。");
        }

        var inbox = await dbContext.LibraryFolders.SingleAsync(
            candidate => candidate.Id == LibraryOrganizationDefaults.InboxFolderId,
            cancellationToken);
        var root = GetLibraryRoot();
        var oldDirectory = Path.Combine(root, folder.Name);
        EnsureOrganizationDirectory(root, inbox.Name, inbox.UnsortedName);
        foreach (var book in folder.Books)
        {
            book.FolderId = inbox.Id;
            book.SeriesId = null;
            book.SeriesIndex = null;
        }

        dbContext.LibraryFolders.Remove(folder);
        await MoveAndSaveAsync(
            dbContext,
            folder.Books.Where(IsManagedExisting).ToArray(),
            book => BuildDestinationPath(
                root,
                inbox.Name,
                inbox.UnsortedName,
                Path.GetFileName(book.FilePath)),
            $"删除书架-{folder.Name}",
            cancellationToken);
        TryDeleteEmptyDirectoryTree(oldDirectory);
    }

    public async Task<LibrarySeriesDetails> MoveSeriesAsync(
        Guid seriesId,
        Guid targetFolderId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await CreateOrganizationContextAsync(cancellationToken);
        var series = await dbContext.Series
            .Include(candidate => candidate.Folder)
            .Include(candidate => candidate.Books)
            .SingleOrDefaultAsync(candidate => candidate.Id == seriesId, cancellationToken)
            ?? throw new InvalidOperationException("系列不存在。");
        var targetFolder = await dbContext.LibraryFolders.SingleOrDefaultAsync(
            candidate => candidate.Id == targetFolderId,
            cancellationToken) ?? throw new InvalidOperationException("目标书架文件夹不存在。");
        if (series.FolderId == targetFolder.Id)
        {
            return new LibrarySeriesDetails(series.Id, targetFolder.Id, series.Name, series.Books.Count);
        }

        if (LibraryNameRules.Normalize(targetFolder.UnsortedName) == series.NormalizedName)
        {
            throw new InvalidOperationException($"目标书架的未分系列名称已经是“{series.Name}”。");
        }

        if (await dbContext.Series.AnyAsync(
                candidate => candidate.Id != seriesId
                    && candidate.FolderId == targetFolder.Id
                    && candidate.NormalizedName == series.NormalizedName,
                cancellationToken))
        {
            throw new InvalidOperationException($"目标书架下已经存在系列“{series.Name}”。");
        }

        var root = GetLibraryRoot();
        var sourceFolderName = series.Folder?.Name ?? LibraryOrganizationDefaults.InboxFolderName;
        var oldDirectory = Path.Combine(root, sourceFolderName, series.Name);
        EnsureOrganizationDirectory(root, targetFolder.Name, series.Name);
        series.FolderId = targetFolder.Id;
        foreach (var book in series.Books)
        {
            book.FolderId = targetFolder.Id;
        }

        await MoveAndSaveAsync(
            dbContext,
            series.Books.Where(IsManagedExisting).ToArray(),
            book => BuildDestinationPath(
                root,
                targetFolder.Name,
                series.Name,
                Path.GetFileName(book.FilePath)),
            $"移动系列-{series.Name}",
            cancellationToken);
        TryDeleteEmptyDirectoryTree(oldDirectory);
        return new LibrarySeriesDetails(series.Id, targetFolder.Id, series.Name, series.Books.Count);
    }

    public async Task<LibraryImportTarget> ResolveImportTargetAsync(
        Guid? folderId,
        Guid? seriesId,
        string? libraryRoot,
        CancellationToken cancellationToken)
    {
        await EnsureDefaultsAsync(cancellationToken);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var targetFolderId = folderId ?? LibraryOrganizationDefaults.InboxFolderId;
        var folder = await dbContext.LibraryFolders.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == targetFolderId,
            cancellationToken) ?? throw new InvalidOperationException("目标书架文件夹不存在。");
        Series? series = null;
        if (seriesId is { } requestedSeriesId)
        {
            series = await dbContext.Series.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == requestedSeriesId && candidate.FolderId == folder.Id,
                cancellationToken) ?? throw new InvalidOperationException("目标系列不存在或不属于该书架。");
        }

        return new LibraryImportTarget(
            folder.Id,
            series?.Id,
            EnsureOrganizationDirectory(
                string.IsNullOrWhiteSpace(libraryRoot)
                    ? GetLibraryRoot()
                    : Path.GetFullPath(libraryRoot.Trim()),
                folder.Name,
                series?.Name ?? folder.UnsortedName));
    }

    public async Task<LibraryMoveResult> MoveBooksAsync(
        LibraryMoveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BookIds.Count == 0)
        {
            return new LibraryMoveResult(0, [], null);
        }

        await using var dbContext = await CreateOrganizationContextAsync(cancellationToken);
        var folder = await dbContext.LibraryFolders.SingleOrDefaultAsync(
            candidate => candidate.Id == request.FolderId,
            cancellationToken) ?? throw new InvalidOperationException("目标书架文件夹不存在。");
        Series? series = null;
        if (request.SeriesId is { } seriesId)
        {
            series = await dbContext.Series.SingleOrDefaultAsync(
                candidate => candidate.Id == seriesId && candidate.FolderId == folder.Id,
                cancellationToken) ?? throw new InvalidOperationException("目标系列不存在或不属于该书架。");
        }

        var ids = request.BookIds.Distinct().ToArray();
        var books = await dbContext.Books
            .Where(book => ids.Contains(book.Id))
            .ToListAsync(cancellationToken);
        var failures = books
            .Where(book => book.StorageMode != BookStorageMode.ManagedCopy || !File.Exists(book.FilePath))
            .Select(book => new LibraryMoveFailure(
                book.Id,
                book.Title,
                book.StorageMode != BookStorageMode.ManagedCopy
                    ? "外部文件需要先复制进管理型书库。"
                    : "源 TXT 文件不存在。"))
            .ToArray();
        if (failures.Length > 0 || books.Count != ids.Length)
        {
            return new LibraryMoveResult(0, failures, null);
        }

        foreach (var book in books)
        {
            book.FolderId = folder.Id;
            book.SeriesId = series?.Id;
            book.SeriesIndex = null;
        }

        var backup = await MoveAndSaveAsync(
            dbContext,
            books,
            book => BuildDestinationPath(
                GetLibraryRoot(),
                folder.Name,
                series?.Name ?? folder.UnsortedName,
                Path.GetFileName(book.FilePath)),
            request.OperationName,
            cancellationToken);
        return new LibraryMoveResult(books.Count, [], backup?.DirectoryPath);
    }

    public async Task<LibraryBackupResult?> BackupBooksAsync(
        IReadOnlyCollection<Guid> bookIds,
        string operationName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bookIds);
        if (bookIds.Count == 0)
        {
            return null;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ids = bookIds.Distinct().ToArray();
        var books = await dbContext.Books.AsNoTracking()
            .Where(book => ids.Contains(book.Id))
            .ToListAsync(cancellationToken);
        var existingBooks = books.Where(book => File.Exists(book.FilePath)).ToArray();
        if (existingBooks.Length != ids.Length)
        {
            throw new FileNotFoundException("无法备份：部分书籍的 TXT 文件不存在。");
        }

        return await backupService.BackupAsync(
            GetLibraryRoot(),
            operationName,
            existingBooks.Select(book => new LibraryBackupItem(book.Id, book.FilePath, null)).ToArray(),
            cancellationToken);
    }

    public async Task MigrateRootAsync(string targetRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);
        var destinationRoot = Path.GetFullPath(targetRoot.Trim());
        Directory.CreateDirectory(destinationRoot);
        await using var dbContext = await CreateOrganizationContextAsync(cancellationToken);
        var books = await dbContext.Books
            .Include(book => book.Folder)
            .Include(book => book.Series)
            .Where(book => book.StorageMode == BookStorageMode.ManagedCopy)
            .ToListAsync(cancellationToken);
        await MoveAndSaveAsync(
            dbContext,
            books.Where(IsManagedExisting).ToArray(),
            book => BuildDestinationPath(
                destinationRoot,
                book.Folder?.Name ?? LibraryOrganizationDefaults.InboxFolderName,
                book.Series?.Name ?? book.Folder?.UnsortedName ?? LibraryOrganizationDefaults.UnsortedName,
                Path.GetFileName(book.FilePath)),
            "迁移书库根目录",
            cancellationToken,
            backupRoot: destinationRoot);
    }

    private async Task<LibraryBackupResult?> MoveAndSaveAsync(
        HNovaReaderDbContext dbContext,
        IReadOnlyCollection<Book> books,
        Func<Book, string> destinationFactory,
        string operationName,
        CancellationToken cancellationToken,
        string? backupRoot = null)
    {
        var destinations = new Dictionary<Guid, string>();
        var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var book in books)
        {
            var requestedPath = destinationFactory(book);
            destinations[book.Id] = GetAvailablePath(requestedPath, book.FilePath, reservedPaths);
        }

        var backup = await backupService.BackupAsync(
            backupRoot ?? GetLibraryRoot(),
            operationName,
            books.Select(book => new LibraryBackupItem(book.Id, book.FilePath, destinations[book.Id])).ToArray(),
            cancellationToken);
        var completedMoves = new List<(string Source, string Destination)>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var book in books)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourcePath = Path.GetFullPath(book.FilePath);
                var destinationPath = destinations[book.Id];
                if (!sourcePath.Equals(destinationPath, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    await MoveFileAsync(sourcePath, destinationPath, cancellationToken);
                    completedMoves.Add((sourcePath, destinationPath));
                }

                var normalized = filePathNormalizer.Normalize(destinationPath);
                book.FilePath = normalized.FullPath;
                book.NormalizedFilePath = normalized.ComparisonKey;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return backup;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            foreach (var move in completedMoves.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(move.Destination) && !File.Exists(move.Source))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(move.Source)!);
                        await MoveFileAsync(move.Destination, move.Source, CancellationToken.None);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            throw;
        }
    }

    private async Task<HNovaReaderDbContext> CreateOrganizationContextAsync(
        CancellationToken cancellationToken)
    {
        await EnsureDefaultsAsync(cancellationToken);
        return await dbContextFactory.CreateDbContextAsync(cancellationToken);
    }

    private string GetLibraryRoot()
    {
        return Path.GetFullPath(
            string.IsNullOrWhiteSpace(settingsService.Current.LibraryFolder)
                ? appPaths.ManagedLibraryDirectory
                : settingsService.Current.LibraryFolder.Trim());
    }

    private static string EnsureOrganizationDirectory(
        string root,
        string folderName,
        string organizationName)
    {
        var directory = Path.Combine(root, folderName, organizationName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string BuildDestinationPath(
        string root,
        string folderName,
        string organizationName,
        string fileName)
    {
        return Path.Combine(
            EnsureOrganizationDirectory(root, folderName, organizationName),
            fileName);
    }

    private static string GetAvailablePath(
        string requestedPath,
        string currentPath,
        ISet<string> reservedPaths)
    {
        var fullRequestedPath = Path.GetFullPath(requestedPath);
        if (fullRequestedPath.Equals(Path.GetFullPath(currentPath), StringComparison.OrdinalIgnoreCase))
        {
            reservedPaths.Add(fullRequestedPath);
            return fullRequestedPath;
        }

        var directory = Path.GetDirectoryName(fullRequestedPath)!;
        var baseName = Path.GetFileNameWithoutExtension(fullRequestedPath);
        var extension = Path.GetExtension(fullRequestedPath);
        for (var suffix = 1; ; suffix++)
        {
            var candidate = suffix == 1
                ? fullRequestedPath
                : Path.Combine(directory, $"{baseName} ({suffix}){extension}");
            if (!File.Exists(candidate) && reservedPaths.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static async Task MoveFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                Path.GetPathRoot(sourcePath),
                Path.GetPathRoot(destinationPath),
                StringComparison.OrdinalIgnoreCase))
        {
            File.Move(sourcePath, destinationPath);
            return;
        }

        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, CopyBufferSize, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
        File.Delete(sourcePath);
    }

    private static bool IsManagedExisting(Book book)
    {
        return book.StorageMode == BookStorageMode.ManagedCopy && File.Exists(book.FilePath);
    }

    private static void TryDeleteEmptyDirectory(string? path)
    {
        if (path is not null && Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
        }
    }

    private static void TryDeleteEmptyDirectoryTree(string? path)
    {
        if (path is null || !Directory.Exists(path))
        {
            return;
        }

        foreach (var child in Directory.EnumerateDirectories(path))
        {
            TryDeleteEmptyDirectoryTree(child);
        }

        TryDeleteEmptyDirectory(path);
    }
}
