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
    private readonly SemaphoreSlim initializationGate = new(1, 1);

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        await initializationGate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var folders = await db.LibraryFolders.ToListAsync(cancellationToken);
            var root = folders.FirstOrDefault(folder => folder.Id == LibraryOrganizationDefaults.RootFolderId);
            var upgrade = root is null;
            if (root is null)
            {
                root = new LibraryFolder
                {
                    Id = LibraryOrganizationDefaults.RootFolderId,
                    Name = "书库根目录",
                    NormalizedName = "书库根目录",
                    IsSystem = true,
                    SortOrder = int.MinValue,
                };
                db.LibraryFolders.Add(root);
                folders.Add(root);
                foreach (var folder in folders.Where(folder => folder.Id != root.Id && folder.ParentId is null))
                {
                    folder.ParentId = root.Id;
                }
            }

            var inbox = folders.FirstOrDefault(folder => folder.Id == LibraryOrganizationDefaults.InboxFolderId);
            if (inbox is null)
            {
                inbox = new LibraryFolder
                {
                    Id = LibraryOrganizationDefaults.InboxFolderId,
                    Name = LibraryOrganizationDefaults.InboxFolderName,
                    NormalizedName = LibraryNameRules.Normalize(LibraryOrganizationDefaults.InboxFolderName),
                    RelativePath = LibraryOrganizationDefaults.InboxFolderName,
                    NormalizedRelativePath = LibraryNameRules.Normalize(LibraryOrganizationDefaults.InboxFolderName),
                    ParentId = root.Id,
                    IsSystem = true,
                    SortOrder = int.MinValue + 1,
                };
                db.LibraryFolders.Add(inbox);
                folders.Add(inbox);
            }

            // Upgrade from real paths, not SeriesId. Existing book IDs/annotations and disk files are untouched.
            if (upgrade)
            {
                foreach (var directory in EnumerateVisibleDirectories(GetLibraryRoot()))
                {
                    EnsurePath(db, folders, Path.GetRelativePath(GetLibraryRoot(), directory));
                }
                var books = await db.Books.ToListAsync(cancellationToken);
                foreach (var book in books)
                {
                    if (book.StorageMode == BookStorageMode.ManagedCopy && IsWithin(GetLibraryRoot(), book.FilePath))
                    {
                        var relative = Path.GetRelativePath(GetLibraryRoot(), Path.GetDirectoryName(book.FilePath)!);
                        var folder = EnsurePath(db, folders, relative == "." ? "" : relative);
                        book.FolderId = folder.Id;
                    }
                    else if (book.FolderId is null)
                    {
                        book.FolderId = inbox.Id;
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            Directory.CreateDirectory(FolderPath(inbox));
        }
        finally
        {
            initializationGate.Release();
        }
    }

    public async Task<IReadOnlyList<LibraryFolderDetails>> GetFoldersAsync(CancellationToken cancellationToken)
    {
        await EnsureDefaultsAsync(cancellationToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var folders = await db.LibraryFolders.AsNoTracking().OrderBy(folder => folder.SortOrder)
            .ThenBy(folder => folder.Name).ToListAsync(cancellationToken);
        var counts = await db.Books.GroupBy(book => book.FolderId)
            .Select(group => new { Id = group.Key, Count = group.Count() }).ToListAsync(cancellationToken);
        return folders.Select(folder => Details(folder,
            counts.FirstOrDefault(count => count.Id == folder.Id)?.Count ?? 0)).ToArray();
    }

    public Task<LibraryFolderDetails> CreateFolderAsync(string name, CancellationToken cancellationToken) =>
        CreateFolderAsync(LibraryOrganizationDefaults.RootFolderId, name, cancellationToken);

    public async Task<LibraryFolderDetails> CreateFolderAsync(Guid parentId, string name, CancellationToken cancellationToken)
    {
        var clean = LibraryNameRules.Clean(name, nameof(name));
        await using var db = await CreateOrganizationContextAsync(cancellationToken);
        var parent = await db.LibraryFolders.SingleAsync(folder => folder.Id == parentId, cancellationToken);
        var relative = Path.Combine(parent.RelativePath, clean);
        var normalized = LibraryNameRules.Normalize(relative);
        if (await db.LibraryFolders.AnyAsync(folder => folder.NormalizedRelativePath == normalized, cancellationToken)
            || Directory.Exists(Path.Combine(GetLibraryRoot(), relative)))
        {
            throw new InvalidOperationException("该目录下已经存在同名文件夹，请使用更新书库识别已有目录。");
        }
        var folder = NewFolder(parent.Id, clean, relative);
        var path = FolderPath(folder);
        Directory.CreateDirectory(path);
        db.LibraryFolders.Add(folder);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            TryDeleteEmptyDirectory(path);
            throw;
        }
        return Details(folder, 0);
    }

    public async Task<LibraryFolderDetails> EnsureFolderPathAsync(string relativePath, CancellationToken cancellationToken)
    {
        await using var db = await CreateOrganizationContextAsync(cancellationToken);
        var folders = await db.LibraryFolders.ToListAsync(cancellationToken);
        var folder = EnsurePath(db, folders, relativePath == "." ? "" : relativePath);
        await db.SaveChangesAsync(cancellationToken);
        return Details(folder, 0);
    }

    private static LibraryFolder EnsurePath(HNovaReaderDbContext db, List<LibraryFolder> folders, string relative)
    {
        if (Path.IsPathRooted(relative))
        {
            throw new ArgumentException("目录必须位于书库根目录中。");
        }
        var current = folders.Single(folder => folder.Id == LibraryOrganizationDefaults.RootFolderId);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            var name = LibraryNameRules.Clean(segment, nameof(relative));
            var path = Path.Combine(current.RelativePath, name);
            var normalized = LibraryNameRules.Normalize(path);
            var next = folders.FirstOrDefault(folder => folder.NormalizedRelativePath == normalized);
            if (next is null)
            {
                next = NewFolder(current.Id, name, path);
                db.LibraryFolders.Add(next);
                folders.Add(next);
            }
            current = next;
        }
        return current;
    }

    private static LibraryFolder NewFolder(Guid parentId, string name, string relative) => new()
    {
        Id = Guid.NewGuid(),
        ParentId = parentId,
        Name = name,
        NormalizedName = LibraryNameRules.Normalize(name),
        RelativePath = relative,
        NormalizedRelativePath = LibraryNameRules.Normalize(relative),
    };

    private static LibraryFolderDetails Details(LibraryFolder folder, int count) => new(
        folder.Id, folder.Name, folder.SortOrder, folder.IsSystem, folder.UnsortedName, 0, count, [],
        folder.ParentId, folder.RelativePath);

    public async Task RenameFolderAsync(Guid folderId, string name, CancellationToken cancellationToken)
    {
        var clean = LibraryNameRules.Clean(name, nameof(name));
        await ChangeFolderAsync(folderId, null, clean, cancellationToken);
    }

    public Task MoveFolderAsync(Guid folderId, Guid targetParentId, CancellationToken cancellationToken) =>
        ChangeFolderAsync(folderId, targetParentId, null, cancellationToken);

    private async Task ChangeFolderAsync(Guid id, Guid? newParentId, string? newName, CancellationToken ct)
    {
        await using var db = await CreateOrganizationContextAsync(ct);
        var folders = await db.LibraryFolders.ToListAsync(ct);
        var folder = folders.Single(item => item.Id == id);
        if (id == LibraryOrganizationDefaults.RootFolderId)
        {
            throw new InvalidOperationException("书库根目录请在设置中修改。");
        }
        var parentId = newParentId ?? folder.ParentId!.Value;
        var parent = folders.Single(item => item.Id == parentId);
        if (parent.Id == id || IsRelativeWithin(folder.RelativePath, parent.RelativePath))
        {
            throw new InvalidOperationException("不能移动到自身或子目录中。");
        }
        var oldRelative = folder.RelativePath;
        var newRelative = Path.Combine(parent.RelativePath, newName ?? folder.Name);
        if (oldRelative.Equals(newRelative, StringComparison.Ordinal))
        {
            return;
        }
        var oldPath = FolderPath(folder);
        var target = Path.Combine(GetLibraryRoot(), newRelative);
        if (!oldPath.Equals(target, StringComparison.OrdinalIgnoreCase)
            && (Directory.Exists(target) || File.Exists(target)))
        {
            throw new InvalidOperationException("目标存在同名文件或文件夹，操作已取消。");
        }
        var descendants = folders.Where(item => item.Id == id || IsRelativeWithin(oldRelative, item.RelativePath)).ToArray();
        var normalized = LibraryNameRules.Normalize(newRelative);
        if (folders.Any(item => !descendants.Contains(item) && item.NormalizedRelativePath == normalized))
        {
            throw new InvalidOperationException("目标文件夹已存在。");
        }
        var books = await db.Books.Where(book => book.StorageMode == BookStorageMode.ManagedCopy).ToListAsync(ct);
        var affected = books.Where(book => IsWithin(oldPath, book.FilePath)).ToArray();
        // Include unregistered/non-TXT files in directory-level backup. Refuse reparse points.
        var files = Directory.Exists(oldPath) ? EnumerateSafeFiles(oldPath).ToArray() : [];
        if (affected.Any(book => !File.Exists(book.FilePath)))
        {
            throw new FileNotFoundException("部分书籍文件丢失，请先更新书库。");
        }
        await backupService.BackupAsync(GetLibraryRoot(), "调整目录-" + folder.Name,
            files.Select(file => new LibraryBackupItem(affected.FirstOrDefault(book =>
                book.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase))?.Id, file,
                Path.Combine(target, Path.GetRelativePath(oldPath, file)))).ToArray(), ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var moved = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (Directory.Exists(oldPath))
            {
                MoveDirectory(oldPath, target);
                moved = true;
            }
            else
            {
                Directory.CreateDirectory(target);
            }
            foreach (var item in descendants)
            {
                var suffix = item.Id == id ? "" : Path.GetRelativePath(oldRelative, item.RelativePath);
                item.RelativePath = Path.Combine(newRelative, suffix);
                item.NormalizedRelativePath = LibraryNameRules.Normalize(item.RelativePath);
            }
            folder.Name = newName ?? folder.Name;
            folder.NormalizedName = LibraryNameRules.Normalize(folder.Name);
            folder.ParentId = parentId;
            foreach (var book in affected)
            {
                RebaseBook(book, oldPath, target);
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (moved && Directory.Exists(target) && !Directory.Exists(oldPath))
            {
                MoveDirectory(target, oldPath);
            }
            else if (!moved)
            {
                TryDeleteEmptyDirectory(target);
            }
            throw;
        }
    }

    public async Task DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken)
    {
        await using var db = await CreateOrganizationContextAsync(cancellationToken);
        var folders = await db.LibraryFolders.ToListAsync(cancellationToken);
        var folder = folders.Single(item => item.Id == folderId);
        if (folder.IsSystem)
        {
            throw new InvalidOperationException("根目录和默认收件箱不能删除。");
        }
        var descendants = folders.Where(item => item.Id == folderId ||
            IsRelativeWithin(folder.RelativePath, item.RelativePath)).ToArray();
        var ids = descendants.Select(item => item.Id).ToArray();
        var books = await db.Books.Where(book => book.FolderId != null && ids.Contains(book.FolderId.Value))
            .ToListAsync(cancellationToken);
        var oldPath = FolderPath(folder);
        var files = Directory.Exists(oldPath) ? EnumerateSafeFiles(oldPath).ToArray() : [];
        if (files.Any(file => !books.Any(book => book.StorageMode == BookStorageMode.ManagedCopy
            && book.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("目录包含尚未入库或非 TXT 文件，为避免丢失，请先更新书库或在资源管理器中处理。");
        }
        if (books.Any(book => book.StorageMode == BookStorageMode.ManagedCopy && !File.Exists(book.FilePath)))
        {
            throw new FileNotFoundException("部分书籍文件丢失，请先更新书库。");
        }
        var inbox = folders.Single(item => item.Id == LibraryOrganizationDefaults.InboxFolderId);
        foreach (var book in books)
        {
            book.FolderId = inbox.Id;
        }
        // Remove child rows before parent rows (self-reference uses Restrict).
        db.LibraryFolders.RemoveRange(descendants);
        await MoveAndSaveAsync(db, books.Where(IsManagedExisting).ToArray(),
            book => Path.Combine(FolderPath(inbox), Path.GetFileName(book.FilePath)),
            "删除文件夹-" + folder.Name, cancellationToken);
        TryDeleteEmptyDirectoryTree(oldPath);
    }

    public async Task PruneMissingFoldersAsync(CancellationToken ct)
    {
        if (!Directory.Exists(GetLibraryRoot())) return;
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var folders = await db.LibraryFolders.ToListAsync(ct);
        var occupied = (await db.Books.Where(book => book.FolderId != null)
            .Select(book => book.FolderId!.Value).ToListAsync(ct)).ToHashSet();
        var removed = new HashSet<Guid>();
        foreach (var folder in folders.OrderByDescending(folder => folder.RelativePath.Count(c => c == Path.DirectorySeparatorChar)))
        {
            if (folder.IsSystem || occupied.Contains(folder.Id) ||
                folders.Any(child => child.ParentId == folder.Id && !removed.Contains(child.Id)))
                continue;
            try { _ = File.GetAttributes(FolderPath(folder)); }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                if (!Directory.Exists(GetLibraryRoot())) return;
                db.LibraryFolders.Remove(folder);
                removed.Add(folder.Id);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        await db.SaveChangesAsync(ct);
    }

    // Series are global metadata. These compatibility methods never create or move physical directories.
    public async Task<LibrarySeriesDetails> CreateSeriesAsync(Guid folderId, string name, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var clean = name.Trim();
        if (clean.Length > 200) throw new ArgumentException("系列名称不能超过 200 个字符。");
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var normalized = LibraryNameRules.Normalize(clean);
        var series = await db.Series.SingleOrDefaultAsync(item => item.NormalizedName == normalized, ct);
        if (series is null)
        {
            series = new Series { Id = Guid.NewGuid(), Name = clean, NormalizedName = normalized };
            db.Series.Add(series);
            await db.SaveChangesAsync(ct);
        }
        return new(series.Id, Guid.Empty, series.Name, await db.Books.CountAsync(book => book.SeriesId == series.Id, ct));
    }

    public async Task RenameSeriesAsync(Guid seriesId, string name, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var clean = name.Trim();
        if (clean.Length > 200) throw new ArgumentException("系列名称不能超过 200 个字符。");
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var series = await db.Series.SingleAsync(item => item.Id == seriesId, ct);
        var normalized = LibraryNameRules.Normalize(clean);
        if (await db.Series.AnyAsync(item => item.Id != seriesId && item.NormalizedName == normalized, ct))
            throw new InvalidOperationException("该系列名称已存在。");
        series.Name = clean;
        series.NormalizedName = normalized;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteSeriesAsync(Guid seriesId, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var series = await db.Series.Include(item => item.Books).SingleAsync(item => item.Id == seriesId, ct);
        foreach (var book in series.Books) { book.SeriesId = null; book.SeriesIndex = null; }
        db.Series.Remove(series);
        await db.SaveChangesAsync(ct);
    }

    public Task RenameUnsortedAsync(Guid folderId, string name, CancellationToken ct) =>
        throw new NotSupportedException("未分系列不再是目录节点，请重命名实际文件夹。");

    public Task<LibrarySeriesDetails> MoveSeriesAsync(Guid seriesId, Guid targetFolderId, CancellationToken ct) =>
        throw new NotSupportedException("系列是独立标记，请拖动书籍或文件夹。");

    public async Task<LibraryImportTarget> ResolveImportTargetAsync(Guid? folderId, Guid? seriesId,
        string? libraryRoot, CancellationToken ct)
    {
        await using var db = await CreateOrganizationContextAsync(ct);
        var folder = await db.LibraryFolders.SingleAsync(item => item.Id ==
            (folderId ?? LibraryOrganizationDefaults.InboxFolderId), ct);
        if (seriesId is { } id && !await db.Series.AnyAsync(item => item.Id == id, ct))
            throw new InvalidOperationException("系列不存在。");
        var root = string.IsNullOrWhiteSpace(libraryRoot) ? GetLibraryRoot() : Path.GetFullPath(libraryRoot);
        var directory = Path.Combine(root, folder.RelativePath);
        Directory.CreateDirectory(directory);
        return new(folder.Id, seriesId, directory);
    }

    public async Task<LibraryMoveResult> MoveBooksAsync(LibraryMoveRequest request, CancellationToken ct)
    {
        if (request.BookIds.Count == 0) return new(0, [], null);
        await using var db = await CreateOrganizationContextAsync(ct);
        var folder = await db.LibraryFolders.SingleAsync(item => item.Id == request.FolderId, ct);
        var ids = request.BookIds.Distinct().ToArray();
        var books = await db.Books.Where(book => ids.Contains(book.Id)).ToListAsync(ct);
        var failures = books.Where(book => !IsManagedExisting(book)).Select(book => new LibraryMoveFailure(
            book.Id, book.Title, "仅能移动已存在的书库内 TXT 文件。")).ToList();
        failures.AddRange(ids.Except(books.Select(book => book.Id)).Select(id =>
            new LibraryMoveFailure(id, "未知书籍", "书库记录不存在。")));
        if (failures.Count > 0) return new(0, failures, null);
        foreach (var book in books) book.FolderId = folder.Id; // Keep series, tags, progress and annotations.
        var backup = await MoveAndSaveAsync(db, books,
            book => Path.Combine(FolderPath(folder), Path.GetFileName(book.FilePath)), request.OperationName, ct);
        return new(books.Count, [], backup?.DirectoryPath);
    }

    public async Task<LibraryBackupResult?> BackupBooksAsync(IReadOnlyCollection<Guid> bookIds,
        string operationName, CancellationToken ct)
    {
        if (bookIds.Count == 0) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var ids = bookIds.Distinct().ToArray();
        var books = await db.Books.Where(book => ids.Contains(book.Id)).ToListAsync(ct);
        if (books.Count != ids.Length || books.Any(book => !File.Exists(book.FilePath)))
            throw new FileNotFoundException("无法备份：部分书籍文件不存在。");
        return await backupService.BackupAsync(GetLibraryRoot(), operationName,
            books.Select(book => new LibraryBackupItem(book.Id, book.FilePath, null)).ToArray(), ct);
    }

    public async Task MigrateRootAsync(string targetRoot, CancellationToken ct)
    {
        var destination = Path.GetFullPath(targetRoot);
        await using var db = await CreateOrganizationContextAsync(ct);
        var folders = await db.LibraryFolders.ToListAsync(ct);
        var books = await db.Books.Where(book => book.StorageMode == BookStorageMode.ManagedCopy).ToListAsync(ct);
        if (books.Any(book => !File.Exists(book.FilePath)))
            throw new FileNotFoundException("部分书籍文件丢失，无法迁移书库。");
        foreach (var folder in folders) Directory.CreateDirectory(Path.Combine(destination, folder.RelativePath));
        await MoveAndSaveAsync(db, books, book => Path.Combine(destination,
            folders.Single(folder => folder.Id == book.FolderId).RelativePath, Path.GetFileName(book.FilePath)),
            "迁移书库根目录", ct, destination);
    }

    private string FolderPath(LibraryFolder folder) => Path.Combine(GetLibraryRoot(), folder.RelativePath);

    private static bool IsRelativeWithin(string parent, string path) =>
        parent.Length == 0 ? path.Length > 0 : path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsWithin(string parent, string path) => IsRelativeWithin(
        Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(path));

    private void RebaseBook(Book book, string oldDirectory, string target)
    {
        var path = filePathNormalizer.Normalize(Path.Combine(target, Path.GetRelativePath(oldDirectory, book.FilePath)));
        book.FilePath = path.FullPath;
        book.NormalizedFilePath = path.ComparisonKey;
        if (IsWithin(oldDirectory, book.SourceFilePath))
        {
            var source = filePathNormalizer.Normalize(Path.Combine(target, Path.GetRelativePath(oldDirectory, book.SourceFilePath)));
            book.SourceFilePath = source.FullPath;
            book.NormalizedSourceFilePath = source.ComparisonKey;
        }
    }

    private static IEnumerable<string> EnumerateSafeFiles(string directory)
    {
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("目录包含符号链接或联接，无法安全移动。");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else yield return entry;
            }
        }
    }

    private static IEnumerable<string> EnumerateVisibleDirectories(string root)
    {
        var pending = new Stack<string>();
        if (Directory.Exists(root)) pending.Push(root);
        while (pending.TryPop(out var current))
        {
            string[] children;
            try { children = Directory.GetDirectories(current); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
            foreach (var child in children)
            {
                if (Path.GetFileName(child).Equals(LibraryBackupService.BackupDirectoryName, StringComparison.OrdinalIgnoreCase))
                    continue;
                FileAttributes attributes;
                try { attributes = File.GetAttributes(child); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                yield return child;
                pending.Push(child);
            }
        }
    }

    private static void MoveDirectory(string source, string target)
    {
        if (!source.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(source, target);
            return;
        }
        var temporary = source + ".rename-" + Guid.NewGuid().ToString("N");
        Directory.Move(source, temporary);
        try { Directory.Move(temporary, target); }
        catch { Directory.Move(temporary, source); throw; }
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
                if (book.SourceFilePath.Equals(book.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    book.SourceFilePath = normalized.FullPath;
                    book.NormalizedSourceFilePath = normalized.ComparisonKey;
                }
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

        var destinationCreated = false;
        try
        {
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                destinationCreated = true;
                await source.CopyToAsync(destination, CopyBufferSize, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }
            File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
            File.Delete(sourcePath);
        }
        catch
        {
            if (destinationCreated && File.Exists(sourcePath) && File.Exists(destinationPath))
                File.Delete(destinationPath);
            throw;
        }
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
