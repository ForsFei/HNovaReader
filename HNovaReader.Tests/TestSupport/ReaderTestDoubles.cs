using HNovaReader.Services.Reader;

namespace HNovaReader.Tests.TestSupport;

public sealed class StubReaderEngineFactory : IReaderEngineFactory
{
    public ITextDocumentSession? Session { get; set; }

    public Func<ReaderOpenRequest, ITextDocumentSession>? SessionFactory { get; set; }

    public ReaderOpenRequest? LastRequest { get; private set; }

    public List<ReaderOpenRequest> Requests { get; } = [];

    public Task<ITextDocumentSession> OpenAsync(
        ReaderOpenRequest request,
        IProgress<TextIndexProgress>? indexProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRequest = request;
        Requests.Add(request);
        return Task.FromResult(
            SessionFactory?.Invoke(request)
                ?? Session
                ?? throw new InvalidOperationException("测试 Reader Session 尚未配置。"));
    }
}

public sealed class StubTextFileEditService : ITextFileEditService
{
    public TextFileRangeEditRequest? LastRequest { get; private set; }

    public TextFileRangeEditResult? Result { get; set; }

    public Exception? Exception { get; set; }

    public Task<TextFileRangeEditResult> ReplaceRangeAsync(
        TextFileRangeEditRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRequest = request;
        if (Exception is not null)
        {
            return Task.FromException<TextFileRangeEditResult>(Exception);
        }

        return Task.FromResult(Result ?? new TextFileRangeEditResult(request.ExpectedVersion));
    }
}

public sealed class StubTextDocumentSession : ITextDocumentSession
{
    public StubTextDocumentSession(
        Guid bookId,
        string filePath,
        ReaderFileVersion fileVersion,
        TextReadWindow initialWindow,
        TextIndexSnapshot? index = null)
    {
        BookId = bookId;
        FilePath = filePath;
        OpenedFileVersion = fileVersion;
        InitialWindow = initialWindow;
        CurrentIndex = index;
        IndexingTask = Task.FromResult(index ?? new TextIndexSnapshot(
            fileVersion,
            "utf-8",
            1,
            initialWindow.EndCharacterOffset,
            true,
            [new TextIndexCheckpoint(0, 0)],
            []));
    }

    public Guid BookId { get; }

    public string FilePath { get; }

    public string EncodingName { get; set; } = "utf-8";

    public ReaderFileVersion OpenedFileVersion { get; }

    public TextReadWindow InitialWindow { get; }

    public TextIndexSnapshot? CurrentIndex { get; set; }

    public Task<TextIndexSnapshot> IndexingTask { get; set; }

    public Func<long, int, TextReadWindow>? WindowReader { get; set; }

    public List<(long Offset, int Length)> ReadRequests { get; } = [];

    public bool IndexingCancelled { get; private set; }

    public bool IsDisposed { get; private set; }

    public Task<TextReadWindow> ReadAsync(
        long characterOffset,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadRequests.Add((characterOffset, maximumCharacters));
        return Task.FromResult(WindowReader?.Invoke(characterOffset, maximumCharacters)
            ?? new TextReadWindow(characterOffset, string.Empty, characterOffset == 0, true));
    }

    public Task<bool> HasFileChangedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public void CancelIndexing()
    {
        IndexingCancelled = true;
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

public sealed class StubReadingPositionService : IReadingPositionService
{
    public ReadingPositionSnapshot? Position { get; set; }

    public SaveReadingPositionRequest? LastSaveRequest { get; private set; }

    public List<SaveReadingPositionRequest> SaveRequests { get; } = [];

    public Task<ReadingPositionSnapshot?> GetAsync(
        Guid bookId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Position?.BookId == bookId ? Position : null);
    }

    public Task SaveAsync(
        SaveReadingPositionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSaveRequest = request;
        SaveRequests.Add(request);
        return Task.CompletedTask;
    }
}

public sealed class StubReaderAnnotationService : IReaderAnnotationService
{
    public ReaderAnnotationsSnapshot Annotations { get; set; } = new([], [], []);

    public CreateBookmarkRequest? LastBookmarkRequest { get; private set; }

    public CreateHighlightRequest? LastHighlightRequest { get; private set; }

    public CreatePositionNoteRequest? LastPositionNoteRequest { get; private set; }

    public CreateHighlightNoteRequest? LastHighlightNoteRequest { get; private set; }

    public Task<ReaderAnnotationsSnapshot> GetAsync(
        Guid bookId,
        ReaderFileVersion currentFileVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Annotations);
    }

    public Task<BookmarkSnapshot> AddBookmarkAsync(
        CreateBookmarkRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastBookmarkRequest = request;
        var bookmark = new BookmarkSnapshot(
            Guid.NewGuid(), request.BookId, request.CharacterOffset,
            request.ChapterTitleSnapshot, request.PreviewText, request.AnchorPrefix,
            request.AnchorSuffix, request.FileVersion, request.EncodingName,
            DateTime.UtcNow, false);
        Annotations = Annotations with { Bookmarks = [.. Annotations.Bookmarks, bookmark] };
        return Task.FromResult(bookmark);
    }

    public Task<HighlightSnapshot> AddHighlightAsync(
        CreateHighlightRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastHighlightRequest = request;
        var highlight = CreateHighlightSnapshot(request);
        Annotations = Annotations with { Highlights = [.. Annotations.Highlights, highlight] };
        return Task.FromResult(highlight);
    }

    public Task<NoteSnapshot> AddPositionNoteAsync(
        CreatePositionNoteRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastPositionNoteRequest = request;
        var note = new NoteSnapshot(
            Guid.NewGuid(), request.BookId, null, request.CharacterOffset,
            request.PreviewText, request.Content, request.AnchorPrefix, request.AnchorSuffix,
            request.FileVersion, request.EncodingName, DateTime.UtcNow, DateTime.UtcNow, false);
        Annotations = Annotations with { Notes = [note, .. Annotations.Notes] };
        return Task.FromResult(note);
    }

    public Task<(HighlightSnapshot Highlight, NoteSnapshot Note)> AddHighlightNoteAsync(
        CreateHighlightNoteRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastHighlightNoteRequest = request;
        var highlight = CreateHighlightSnapshot(request.Highlight);
        var note = new NoteSnapshot(
            Guid.NewGuid(), request.Highlight.BookId, highlight.Id,
            request.Highlight.StartOffset, request.Highlight.SelectedText, request.Content,
            request.Highlight.AnchorPrefix, request.Highlight.AnchorSuffix,
            request.Highlight.FileVersion, request.Highlight.EncodingName,
            DateTime.UtcNow, DateTime.UtcNow, false);
        Annotations = Annotations with
        {
            Highlights = [.. Annotations.Highlights, highlight],
            Notes = [note, .. Annotations.Notes],
        };
        return Task.FromResult((highlight, note));
    }

    public Task<NoteSnapshot> UpdateNoteAsync(
        Guid noteId,
        string content,
        ReaderFileVersion currentFileVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var note = Annotations.Notes.Single(item => item.Id == noteId) with
        {
            Content = content,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        Annotations = Annotations with
        {
            Notes = Annotations.Notes.Select(item => item.Id == noteId ? note : item).ToArray(),
        };
        return Task.FromResult(note);
    }

    public Task DeleteBookmarkAsync(Guid bookmarkId, CancellationToken cancellationToken)
    {
        Annotations = Annotations with
        {
            Bookmarks = Annotations.Bookmarks.Where(item => item.Id != bookmarkId).ToArray(),
        };
        return Task.CompletedTask;
    }

    public Task DeleteHighlightAsync(Guid highlightId, CancellationToken cancellationToken)
    {
        Annotations = Annotations with
        {
            Highlights = Annotations.Highlights.Where(item => item.Id != highlightId).ToArray(),
            Notes = Annotations.Notes.Where(item => item.HighlightId != highlightId).ToArray(),
        };
        return Task.CompletedTask;
    }

    public Task DeleteNoteAsync(Guid noteId, CancellationToken cancellationToken)
    {
        Annotations = Annotations with
        {
            Notes = Annotations.Notes.Where(item => item.Id != noteId).ToArray(),
        };
        return Task.CompletedTask;
    }

    private static HighlightSnapshot CreateHighlightSnapshot(CreateHighlightRequest request)
    {
        return new HighlightSnapshot(
            Guid.NewGuid(), request.BookId, request.StartOffset, request.Length,
            request.SelectedText, request.Style, request.AnchorPrefix, request.AnchorSuffix,
            request.FileVersion, request.EncodingName, DateTime.UtcNow, false);
    }
}
