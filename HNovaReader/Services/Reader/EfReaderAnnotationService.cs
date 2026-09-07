using HNovaReader.Data;
using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HNovaReader.Services.Reader;

public sealed class EfReaderAnnotationService(
    IDbContextFactory<HNovaReaderDbContext> dbContextFactory,
    TimeProvider timeProvider) : IReaderAnnotationService
{
    public async Task<ReaderAnnotationsSnapshot> GetAsync(
        Guid bookId,
        ReaderFileVersion currentFileVersion,
        CancellationToken cancellationToken)
    {
        ValidateBookId(bookId);
        ArgumentNullException.ThrowIfNull(currentFileVersion);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var bookmarks = await dbContext.Bookmarks
            .AsNoTracking()
            .Where(item => item.BookId == bookId)
            .OrderBy(item => item.CharacterOffset)
            .ToListAsync(cancellationToken);
        var highlights = await dbContext.Highlights
            .AsNoTracking()
            .Where(item => item.BookId == bookId)
            .OrderBy(item => item.StartOffset)
            .ToListAsync(cancellationToken);
        var notes = await dbContext.Notes
            .AsNoTracking()
            .Include(item => item.Highlight)
            .Where(item => item.BookId == bookId)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        return new ReaderAnnotationsSnapshot(
            bookmarks.Select(item => Map(item, currentFileVersion)).ToArray(),
            highlights.Select(item => Map(item, currentFileVersion)).ToArray(),
            notes.Select(item => Map(item, currentFileVersion)).ToArray());
    }

    public async Task<BookmarkSnapshot> AddBookmarkAsync(
        CreateBookmarkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAnchor(request.BookId, request.CharacterOffset, request.FileVersion, request.EncodingName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PreviewText);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureBookExistsAsync(dbContext, request.BookId, cancellationToken);
        var bookmark = await dbContext.Bookmarks.SingleOrDefaultAsync(
            item => item.BookId == request.BookId && item.CharacterOffset == request.CharacterOffset,
            cancellationToken);
        if (bookmark is null)
        {
            bookmark = new Bookmark
            {
                Id = Guid.NewGuid(),
                BookId = request.BookId,
                CharacterOffset = request.CharacterOffset,
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            };
            dbContext.Bookmarks.Add(bookmark);
        }

        ApplyAnchor(
            bookmark,
            request.ChapterTitleSnapshot,
            request.PreviewText,
            request.AnchorPrefix,
            request.AnchorSuffix,
            request.FileVersion,
            request.EncodingName);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(bookmark, request.FileVersion);
    }

    public async Task<HighlightSnapshot> AddHighlightAsync(
        CreateHighlightRequest request,
        CancellationToken cancellationToken)
    {
        ValidateHighlight(request);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureBookExistsAsync(dbContext, request.BookId, cancellationToken);
        var highlight = CreateHighlight(request);
        dbContext.Highlights.Add(highlight);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(highlight, request.FileVersion);
    }

    public async Task<NoteSnapshot> AddPositionNoteAsync(
        CreatePositionNoteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAnchor(request.BookId, request.CharacterOffset, request.FileVersion, request.EncodingName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Content);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PreviewText);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureBookExistsAsync(dbContext, request.BookId, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var note = new Note
        {
            Id = Guid.NewGuid(),
            BookId = request.BookId,
            CharacterOffset = request.CharacterOffset,
            PreviewText = TruncateRequired(request.PreviewText, 500),
            Content = TruncateRequired(request.Content, 20000),
            AnchorPrefix = Truncate(request.AnchorPrefix, 256),
            AnchorSuffix = Truncate(request.AnchorSuffix, 256),
            FileSize = request.FileVersion.FileSize,
            LastWriteTimeUtc = EnsureUtc(request.FileVersion.LastWriteTimeUtc),
            EncodingName = request.EncodingName.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        dbContext.Notes.Add(note);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(note, request.FileVersion);
    }

    public async Task<(HighlightSnapshot Highlight, NoteSnapshot Note)> AddHighlightNoteAsync(
        CreateHighlightNoteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHighlight(request.Highlight);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Content);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureBookExistsAsync(dbContext, request.Highlight.BookId, cancellationToken);
        var highlight = CreateHighlight(request.Highlight);
        var now = highlight.CreatedAtUtc;
        var note = new Note
        {
            Id = Guid.NewGuid(),
            BookId = request.Highlight.BookId,
            Highlight = highlight,
            PreviewText = TruncateRequired(request.Highlight.SelectedText, 500),
            Content = TruncateRequired(request.Content, 20000),
            AnchorPrefix = Truncate(request.Highlight.AnchorPrefix, 256),
            AnchorSuffix = Truncate(request.Highlight.AnchorSuffix, 256),
            FileSize = request.Highlight.FileVersion.FileSize,
            LastWriteTimeUtc = EnsureUtc(request.Highlight.FileVersion.LastWriteTimeUtc),
            EncodingName = request.Highlight.EncodingName.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        dbContext.Highlights.Add(highlight);
        dbContext.Notes.Add(note);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (Map(highlight, request.Highlight.FileVersion), Map(note, request.Highlight.FileVersion));
    }

    public async Task<NoteSnapshot> UpdateNoteAsync(
        Guid noteId,
        string content,
        ReaderFileVersion currentFileVersion,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(noteId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentNullException.ThrowIfNull(currentFileVersion);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var note = await dbContext.Notes
            .Include(item => item.Highlight)
            .SingleOrDefaultAsync(item => item.Id == noteId, cancellationToken)
            ?? throw new InvalidOperationException("无法更新笔记：记录不存在。");
        note.Content = TruncateRequired(content, 20000);
        note.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(note, currentFileVersion);
    }

    public Task DeleteBookmarkAsync(Guid bookmarkId, CancellationToken cancellationToken) =>
        DeleteAsync<Bookmark>(bookmarkId, cancellationToken);

    public Task DeleteHighlightAsync(Guid highlightId, CancellationToken cancellationToken) =>
        DeleteAsync<Highlight>(highlightId, cancellationToken);

    public Task DeleteNoteAsync(Guid noteId, CancellationToken cancellationToken) =>
        DeleteAsync<Note>(noteId, cancellationToken);

    private async Task DeleteAsync<TEntity>(Guid id, CancellationToken cancellationToken)
        where TEntity : class
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.Set<TEntity>().FindAsync([id], cancellationToken);
        if (entity is null)
        {
            return;
        }

        dbContext.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Highlight CreateHighlight(CreateHighlightRequest request)
    {
        return new Highlight
        {
            Id = Guid.NewGuid(),
            BookId = request.BookId,
            StartOffset = request.StartOffset,
            Length = request.Length,
            SelectedText = TruncateRequired(request.SelectedText, 16384),
            Style = request.Style,
            AnchorPrefix = Truncate(request.AnchorPrefix, 256),
            AnchorSuffix = Truncate(request.AnchorSuffix, 256),
            FileSize = request.FileVersion.FileSize,
            LastWriteTimeUtc = EnsureUtc(request.FileVersion.LastWriteTimeUtc),
            EncodingName = request.EncodingName.Trim(),
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        };
    }

    private static void ApplyAnchor(
        Bookmark bookmark,
        string? chapterTitle,
        string previewText,
        string? prefix,
        string? suffix,
        ReaderFileVersion fileVersion,
        string encodingName)
    {
        bookmark.ChapterTitleSnapshot = Truncate(chapterTitle, 240);
        bookmark.PreviewText = TruncateRequired(previewText, 500);
        bookmark.AnchorPrefix = Truncate(prefix, 256);
        bookmark.AnchorSuffix = Truncate(suffix, 256);
        bookmark.FileSize = fileVersion.FileSize;
        bookmark.LastWriteTimeUtc = EnsureUtc(fileVersion.LastWriteTimeUtc);
        bookmark.EncodingName = encodingName.Trim();
    }

    private static async Task EnsureBookExistsAsync(
        HNovaReaderDbContext dbContext,
        Guid bookId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Books.AnyAsync(book => book.Id == bookId, cancellationToken))
        {
            throw new InvalidOperationException("无法保存标注：书籍记录不存在。");
        }
    }

    private static void ValidateHighlight(CreateHighlightRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAnchor(request.BookId, request.StartOffset, request.FileVersion, request.EncodingName);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(request.Length, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.Length, 16384);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SelectedText);
        if (request.SelectedText.Length != request.Length)
        {
            throw new ArgumentException("高亮长度必须与原始选中文本长度一致。", nameof(request));
        }

        if (!Enum.IsDefined(request.Style))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private static void ValidateAnchor(
        Guid bookId,
        long characterOffset,
        ReaderFileVersion fileVersion,
        string encodingName)
    {
        ValidateBookId(bookId);
        ArgumentOutOfRangeException.ThrowIfNegative(characterOffset);
        ArgumentNullException.ThrowIfNull(fileVersion);
        ArgumentOutOfRangeException.ThrowIfNegative(fileVersion.FileSize);
        ArgumentException.ThrowIfNullOrWhiteSpace(encodingName);
    }

    private static void ValidateBookId(Guid bookId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(bookId, Guid.Empty);
    }

    private static BookmarkSnapshot Map(Bookmark item, ReaderFileVersion currentVersion)
    {
        var version = new ReaderFileVersion(item.FileSize, EnsureUtc(item.LastWriteTimeUtc));
        return new BookmarkSnapshot(
            item.Id,
            item.BookId,
            item.CharacterOffset,
            item.ChapterTitleSnapshot,
            item.PreviewText,
            item.AnchorPrefix,
            item.AnchorSuffix,
            version,
            item.EncodingName,
            EnsureUtc(item.CreatedAtUtc),
            version != currentVersion);
    }

    private static HighlightSnapshot Map(Highlight item, ReaderFileVersion currentVersion)
    {
        var version = new ReaderFileVersion(item.FileSize, EnsureUtc(item.LastWriteTimeUtc));
        return new HighlightSnapshot(
            item.Id,
            item.BookId,
            item.StartOffset,
            item.Length,
            item.SelectedText,
            item.Style,
            item.AnchorPrefix,
            item.AnchorSuffix,
            version,
            item.EncodingName,
            EnsureUtc(item.CreatedAtUtc),
            version != currentVersion);
    }

    private static NoteSnapshot Map(Note item, ReaderFileVersion currentVersion)
    {
        var version = new ReaderFileVersion(item.FileSize, EnsureUtc(item.LastWriteTimeUtc));
        var characterOffset = item.CharacterOffset ?? item.Highlight?.StartOffset
            ?? throw new InvalidOperationException("笔记缺少有效锚点。");
        return new NoteSnapshot(
            item.Id,
            item.BookId,
            item.HighlightId,
            characterOffset,
            item.PreviewText,
            item.Content,
            item.AnchorPrefix,
            item.AnchorSuffix,
            version,
            item.EncodingName,
            EnsureUtc(item.CreatedAtUtc),
            EnsureUtc(item.UpdatedAtUtc),
            version != currentVersion);
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    private static string? Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }

    private static string TruncateRequired(string value, int maximumLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }
}
