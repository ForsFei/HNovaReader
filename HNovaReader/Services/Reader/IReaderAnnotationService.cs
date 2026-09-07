using HNovaReader.Data.Entities;

namespace HNovaReader.Services.Reader;

public sealed record BookmarkSnapshot(
    Guid Id,
    Guid BookId,
    long CharacterOffset,
    string? ChapterTitleSnapshot,
    string PreviewText,
    string? AnchorPrefix,
    string? AnchorSuffix,
    ReaderFileVersion FileVersion,
    string EncodingName,
    DateTime CreatedAtUtc,
    bool NeedsReview);

public sealed record HighlightSnapshot(
    Guid Id,
    Guid BookId,
    long StartOffset,
    int Length,
    string SelectedText,
    HighlightStyle Style,
    string? AnchorPrefix,
    string? AnchorSuffix,
    ReaderFileVersion FileVersion,
    string EncodingName,
    DateTime CreatedAtUtc,
    bool NeedsReview);

public sealed record NoteSnapshot(
    Guid Id,
    Guid BookId,
    Guid? HighlightId,
    long CharacterOffset,
    string PreviewText,
    string Content,
    string? AnchorPrefix,
    string? AnchorSuffix,
    ReaderFileVersion FileVersion,
    string EncodingName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    bool NeedsReview);

public sealed record ReaderAnnotationsSnapshot(
    IReadOnlyList<BookmarkSnapshot> Bookmarks,
    IReadOnlyList<HighlightSnapshot> Highlights,
    IReadOnlyList<NoteSnapshot> Notes)
{
    public bool NeedsReview => Bookmarks.Any(item => item.NeedsReview)
        || Highlights.Any(item => item.NeedsReview)
        || Notes.Any(item => item.NeedsReview);
}

public sealed record CreateBookmarkRequest(
    Guid BookId,
    long CharacterOffset,
    string? ChapterTitleSnapshot,
    string PreviewText,
    string? AnchorPrefix,
    string? AnchorSuffix,
    ReaderFileVersion FileVersion,
    string EncodingName);

public sealed record CreateHighlightRequest(
    Guid BookId,
    long StartOffset,
    int Length,
    string SelectedText,
    HighlightStyle Style,
    string? AnchorPrefix,
    string? AnchorSuffix,
    ReaderFileVersion FileVersion,
    string EncodingName);

public sealed record CreatePositionNoteRequest(
    Guid BookId,
    long CharacterOffset,
    string PreviewText,
    string Content,
    string? AnchorPrefix,
    string? AnchorSuffix,
    ReaderFileVersion FileVersion,
    string EncodingName);

public sealed record CreateHighlightNoteRequest(
    CreateHighlightRequest Highlight,
    string Content);

public interface IReaderAnnotationService
{
    Task<ReaderAnnotationsSnapshot> GetAsync(
        Guid bookId,
        ReaderFileVersion currentFileVersion,
        CancellationToken cancellationToken);

    Task<BookmarkSnapshot> AddBookmarkAsync(
        CreateBookmarkRequest request,
        CancellationToken cancellationToken);

    Task<HighlightSnapshot> AddHighlightAsync(
        CreateHighlightRequest request,
        CancellationToken cancellationToken);

    Task<NoteSnapshot> AddPositionNoteAsync(
        CreatePositionNoteRequest request,
        CancellationToken cancellationToken);

    Task<(HighlightSnapshot Highlight, NoteSnapshot Note)> AddHighlightNoteAsync(
        CreateHighlightNoteRequest request,
        CancellationToken cancellationToken);

    Task<NoteSnapshot> UpdateNoteAsync(
        Guid noteId,
        string content,
        ReaderFileVersion currentFileVersion,
        CancellationToken cancellationToken);

    Task DeleteBookmarkAsync(Guid bookmarkId, CancellationToken cancellationToken);

    Task DeleteHighlightAsync(Guid highlightId, CancellationToken cancellationToken);

    Task DeleteNoteAsync(Guid noteId, CancellationToken cancellationToken);
}
