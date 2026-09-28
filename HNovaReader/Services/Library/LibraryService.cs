using System.IO;
using HNovaReader.Data;
using HNovaReader.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HNovaReader.Services.Library;

public sealed class LibraryService(
    IDbContextFactory<HNovaReaderDbContext> dbContextFactory,
    IFilePathNormalizer filePathNormalizer,
    TimeProvider timeProvider) : ILibraryService
{
    public async Task<IReadOnlyList<BookDetails>> GetBooksAsync(
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var books = await WithDetails(dbContext.Books)
            .AsNoTracking()
            .OrderBy(book => book.Title)
            .ThenBy(book => book.Id)
            .ToListAsync(cancellationToken);

        return books.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<BookDetails>> GetSeriesBooksAsync(
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var books = await WithDetails(dbContext.Books)
            .AsNoTracking()
            .Where(book => book.SeriesId == seriesId)
            .OrderBy(book => book.SeriesIndex == null)
            .ThenBy(book => book.SeriesIndex)
            .ThenBy(book => book.Title)
            .ThenBy(book => book.Id)
            .ToListAsync(cancellationToken);

        return books.Select(Map).ToArray();
    }

    public async Task<BookDetails?> GetBookAsync(
        Guid bookId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var book = await WithDetails(dbContext.Books)
            .AsNoTracking()
            .SingleOrDefaultAsync(book => book.Id == bookId, cancellationToken);

        return book is null ? null : Map(book);
    }

    public async Task<bool> ContainsFilePathAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var normalizedPath = filePathNormalizer.Normalize(filePath);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.Books
            .AsNoTracking()
            .AnyAsync(
                book => book.NormalizedSourceFilePath == normalizedPath.ComparisonKey
                    || book.NormalizedFilePath == normalizedPath.ComparisonKey,
                cancellationToken);
    }

    public async Task<BookDetails> AddBookAsync(
        AddBookRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateFileMetadata(request.FileSize, request.StorageMode);

        var normalizedPath = filePathNormalizer.Normalize(request.FilePath);
        var normalizedSourcePath = filePathNormalizer.Normalize(
            request.SourceFilePath ?? request.FilePath);
        EnsureLength(normalizedPath.FullPath, 2048, nameof(request.FilePath));
        EnsureLength(normalizedSourcePath.FullPath, 2048, nameof(request.SourceFilePath));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await dbContext.Books.AnyAsync(
                book => book.NormalizedSourceFilePath == normalizedSourcePath.ComparisonKey
                    || book.NormalizedFilePath == normalizedPath.ComparisonKey,
                cancellationToken))
        {
            throw new DuplicateBookException(normalizedSourcePath.FullPath);
        }

        var folder = await ResolveFolderAsync(dbContext, request.FolderId, cancellationToken);
        var series = await ResolveSeriesAsync(
            dbContext,
            folder,
            request.SeriesId,
            request.SeriesName,
            cancellationToken);

        var book = new Book
        {
            Id = Guid.NewGuid(),
            FilePath = normalizedPath.FullPath,
            NormalizedFilePath = normalizedPath.ComparisonKey,
            SourceFilePath = normalizedSourcePath.FullPath,
            NormalizedSourceFilePath = normalizedSourcePath.ComparisonKey,
            StorageMode = request.StorageMode,
            Title = GetTitle(request.Title, normalizedPath.FullPath),
            Description = CleanOptional(request.Description, 4000, nameof(request.Description)),
            CoverPath = CleanOptional(request.CoverPath, 2048, nameof(request.CoverPath)),
            Author = await ResolveAuthorAsync(dbContext, request.AuthorName, cancellationToken),
            Folder = folder,
            Series = series,
            SeriesIndex = series is null
                ? null
                : request.SeriesIndex,
            IsFavorite = request.IsFavorite,
            DateAddedUtc = timeProvider.GetUtcNow().UtcDateTime,
            FileSize = request.FileSize,
            LastWriteTimeUtc = NormalizeUtc(request.LastWriteTimeUtc),
            FileEncoding = CleanRequired(
                request.FileEncoding,
                64,
                nameof(request.FileEncoding)),
        };

        var tags = await ResolveTagsAsync(dbContext, request.Tags, cancellationToken);
        foreach (var tag in tags)
        {
            book.BookTags.Add(new BookTag { Book = book, Tag = tag });
        }

        dbContext.Books.Add(book);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicatePath(exception))
        {
            throw new DuplicateBookException(normalizedSourcePath.FullPath, exception);
        }

        return Map(book);
    }

    public async Task<BookDetails?> UpdateBookAsync(
        Guid bookId,
        UpdateBookRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var book = await WithDetails(dbContext.Books)
            .SingleOrDefaultAsync(book => book.Id == bookId, cancellationToken);

        if (book is null)
        {
            return null;
        }

        book.Title = GetTitle(request.Title, book.FilePath);
        book.Description = CleanOptional(request.Description, 4000, nameof(request.Description));
        book.CoverPath = CleanOptional(request.CoverPath, 2048, nameof(request.CoverPath));
        book.Author = await ResolveAuthorAsync(dbContext, request.AuthorName, cancellationToken);
        var folder = await ResolveFolderAsync(dbContext, book.FolderId, cancellationToken);
        book.Folder = folder;
        book.Series = await ResolveSeriesAsync(
            dbContext,
            folder,
            seriesId: null,
            request.SeriesName,
            cancellationToken);
        book.SeriesIndex = book.Series is null
            ? null
            : request.SeriesIndex;
        book.IsFavorite = request.IsFavorite;

        var tags = await ResolveTagsAsync(dbContext, request.Tags, cancellationToken);
        SynchronizeTags(dbContext, book, tags);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(book);
    }

    public async Task<bool> RemoveBookAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var book = await dbContext.Books.FindAsync([bookId], cancellationToken);

        if (book is null)
        {
            return false;
        }

        dbContext.Books.Remove(book);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<BookDetails?> RefreshBookFileInfoAsync(
        Guid bookId,
        RefreshBookFileInfoRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.FileSize < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.FileSize,
                "文件大小不能为负数。");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var book = await WithDetails(dbContext.Books)
            .SingleOrDefaultAsync(book => book.Id == bookId, cancellationToken);

        if (book is null)
        {
            return null;
        }

        book.FileSize = request.FileSize;
        book.LastWriteTimeUtc = NormalizeUtc(request.LastWriteTimeUtc);
        book.FileEncoding = CleanRequired(
            request.FileEncoding,
            64,
            nameof(request.FileEncoding));

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(book);
    }

    public async Task<BookDetails?> RelocateBookFileAsync(
        Guid bookId,
        RelocateBookFileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateFileMetadata(request.FileSize, BookStorageMode.OriginalLocation);
        var normalizedPath = filePathNormalizer.Normalize(request.FilePath);
        EnsureLength(normalizedPath.FullPath, 2048, nameof(request.FilePath));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await dbContext.Books.AsNoTracking().AnyAsync(
                candidate => candidate.Id != bookId
                    && (candidate.NormalizedFilePath == normalizedPath.ComparisonKey
                        || candidate.NormalizedSourceFilePath == normalizedPath.ComparisonKey),
                cancellationToken))
        {
            throw new DuplicateBookException(normalizedPath.FullPath);
        }

        var book = await WithDetails(dbContext.Books)
            .SingleOrDefaultAsync(candidate => candidate.Id == bookId, cancellationToken);
        if (book is null)
        {
            return null;
        }

        book.FilePath = normalizedPath.FullPath;
        book.NormalizedFilePath = normalizedPath.ComparisonKey;
        book.SourceFilePath = normalizedPath.FullPath;
        book.NormalizedSourceFilePath = normalizedPath.ComparisonKey;
        book.StorageMode = BookStorageMode.OriginalLocation;
        book.FileSize = request.FileSize;
        book.LastWriteTimeUtc = NormalizeUtc(request.LastWriteTimeUtc);
        book.FileEncoding = CleanRequired(request.FileEncoding, 64, nameof(request.FileEncoding));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicatePath(exception))
        {
            throw new DuplicateBookException(normalizedPath.FullPath, exception);
        }

        return Map(book);
    }

    private static IQueryable<Book> WithDetails(IQueryable<Book> books)
    {
        return books
            .Include(book => book.Author)
            .Include(book => book.Folder)
            .Include(book => book.Series)
            .Include(book => book.ReadingPosition)
            .Include(book => book.BookTags)
            .ThenInclude(bookTag => bookTag.Tag);
    }

    private static async Task<Author?> ResolveAuthorAsync(
        HNovaReaderDbContext dbContext,
        string? name,
        CancellationToken cancellationToken)
    {
        var cleanedName = CleanOptional(name, 200, nameof(name));
        if (cleanedName is null)
        {
            return null;
        }

        var normalizedName = NormalizeName(cleanedName);
        var author = await dbContext.Authors.SingleOrDefaultAsync(
            candidate => candidate.NormalizedName == normalizedName,
            cancellationToken);

        if (author is not null)
        {
            return author;
        }

        author = new Author
        {
            Id = Guid.NewGuid(),
            Name = cleanedName,
            NormalizedName = normalizedName,
        };
        dbContext.Authors.Add(author);
        return author;
    }

    private static async Task<LibraryFolder> ResolveFolderAsync(
        HNovaReaderDbContext dbContext,
        Guid? folderId,
        CancellationToken cancellationToken)
    {
        var targetId = folderId ?? LibraryOrganizationDefaults.InboxFolderId;
        var folder = await dbContext.LibraryFolders.SingleOrDefaultAsync(
            candidate => candidate.Id == targetId,
            cancellationToken);
        if (folder is not null)
        {
            return folder;
        }

        if (targetId != LibraryOrganizationDefaults.InboxFolderId)
        {
            throw new InvalidOperationException("指定的书架文件夹不存在。");
        }

        folder = new LibraryFolder
        {
            Id = LibraryOrganizationDefaults.InboxFolderId,
            Name = LibraryOrganizationDefaults.InboxFolderName,
            NormalizedName = LibraryNameRules.Normalize(LibraryOrganizationDefaults.InboxFolderName),
            RelativePath = LibraryOrganizationDefaults.InboxFolderName,
            NormalizedRelativePath = LibraryNameRules.Normalize(LibraryOrganizationDefaults.InboxFolderName),
            UnsortedName = LibraryOrganizationDefaults.UnsortedName,
            SortOrder = int.MinValue,
            IsSystem = true,
        };
        dbContext.LibraryFolders.Add(folder);
        return folder;
    }

    private static async Task<Series?> ResolveSeriesAsync(
        HNovaReaderDbContext dbContext,
        LibraryFolder folder,
        Guid? seriesId,
        string? name,
        CancellationToken cancellationToken)
    {
        if (seriesId is { } requestedSeriesId)
        {
            return await dbContext.Series.SingleOrDefaultAsync(
                candidate => candidate.Id == requestedSeriesId,
                cancellationToken)
                ?? throw new InvalidOperationException("指定的系列不存在。");
        }

        var cleanedName = CleanOptional(name, 200, nameof(name));
        if (cleanedName is null)
        {
            return null;
        }

        var normalizedName = NormalizeName(cleanedName);
        var series = await dbContext.Series.SingleOrDefaultAsync(
            candidate => candidate.NormalizedName == normalizedName,
            cancellationToken);

        if (series is not null)
        {
            return series;
        }

        series = new Series
        {
            Id = Guid.NewGuid(),
            Name = cleanedName,
            NormalizedName = normalizedName,
        };
        dbContext.Series.Add(series);
        return series;
    }

    private static async Task<IReadOnlyList<Tag>> ResolveTagsAsync(
        HNovaReaderDbContext dbContext,
        IEnumerable<string>? names,
        CancellationToken cancellationToken)
    {
        var requestedNames = (names ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => CleanRequired(name, 100, nameof(names)))
            .GroupBy(NormalizeName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        if (requestedNames.Count == 0)
        {
            return Array.Empty<Tag>();
        }

        var normalizedNames = requestedNames.Keys.ToArray();
        var tags = await dbContext.Tags
            .Where(tag => normalizedNames.Contains(tag.NormalizedName))
            .ToListAsync(cancellationToken);
        var existingNames = tags
            .Select(tag => tag.NormalizedName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var normalizedName in normalizedNames.Where(name => !existingNames.Contains(name)))
        {
            var tag = new Tag
            {
                Id = Guid.NewGuid(),
                Name = requestedNames[normalizedName],
                NormalizedName = normalizedName,
            };
            dbContext.Tags.Add(tag);
            tags.Add(tag);
        }

        return tags;
    }

    private static void SynchronizeTags(
        HNovaReaderDbContext dbContext,
        Book book,
        IReadOnlyList<Tag> tags)
    {
        var targetTagIds = tags.Select(tag => tag.Id).ToHashSet();
        var removedLinks = book.BookTags
            .Where(link => !targetTagIds.Contains(link.TagId))
            .ToArray();
        foreach (var removedLink in removedLinks)
        {
            book.BookTags.Remove(removedLink);
            dbContext.BookTags.Remove(removedLink);
        }

        var existingTagIds = book.BookTags.Select(link => link.TagId).ToHashSet();
        foreach (var tag in tags.Where(tag => !existingTagIds.Contains(tag.Id)))
        {
            book.BookTags.Add(new BookTag { Book = book, Tag = tag });
        }
    }

    private static BookDetails Map(Book book)
    {
        var tags = book.BookTags
            .Select(bookTag => bookTag.Tag.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new BookDetails(
            book.Id,
            book.FilePath,
            book.SourceFilePath,
            book.StorageMode,
            book.Title,
            book.Description,
            book.CoverPath,
            book.AuthorId,
            book.Author?.Name,
            book.SeriesId,
            book.Series?.Name,
            book.SeriesIndex,
            tags,
            book.IsFavorite,
            EnsureUtcKind(book.DateAddedUtc),
            book.LastOpenedAtUtc is { } lastOpenedAtUtc
                ? EnsureUtcKind(lastOpenedAtUtc)
                : null,
            book.FileSize,
            EnsureUtcKind(book.LastWriteTimeUtc),
            book.FileEncoding,
            book.ReadingPosition?.CharacterOffset,
            book.ReadingPosition?.Progress,
            book.ReadingPosition?.ChapterTitleSnapshot,
            book.FolderId,
            book.Folder?.Name);
    }

    private static string GetTitle(string? title, string filePath)
    {
        var fallbackTitle = Path.GetFileNameWithoutExtension(filePath);
        return CleanRequired(
            string.IsNullOrWhiteSpace(title) ? fallbackTitle : title,
            400,
            nameof(title));
    }

    private static string? CleanOptional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Trim();
        EnsureLength(cleaned, maxLength, parameterName);
        return cleaned;
    }

    private static string CleanRequired(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var cleaned = value.Trim();
        EnsureLength(cleaned, maxLength, parameterName);
        return cleaned;
    }

    private static void EnsureLength(string value, int maxLength, string parameterName)
    {
        if (value.Length > maxLength)
        {
            throw new ArgumentException(
                $"值的长度不能超过 {maxLength} 个字符。",
                parameterName);
        }
    }

    private static string NormalizeName(string name)
    {
        return name.ToUpperInvariant();
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    private static DateTime EnsureUtcKind(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private static void ValidateFileMetadata(long fileSize, BookStorageMode storageMode)
    {
        if (fileSize < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fileSize),
                fileSize,
                "文件大小不能为负数。");
        }

        if (!Enum.IsDefined(storageMode))
        {
            throw new ArgumentOutOfRangeException(nameof(storageMode));
        }
    }

    private static bool IsDuplicatePath(DbUpdateException exception)
    {
        return exception.InnerException is SqliteException sqliteException
            && sqliteException.SqliteErrorCode == 19
            && sqliteException.Message.Contains(
                "Books.NormalizedFilePath",
                StringComparison.OrdinalIgnoreCase)
            || exception.InnerException is SqliteException sourcePathException
            && sourcePathException.SqliteErrorCode == 19
            && sourcePathException.Message.Contains(
                "Books.NormalizedSourceFilePath",
                StringComparison.OrdinalIgnoreCase);
    }
}
