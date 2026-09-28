using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HNovaReader.Data.Entities;
using HNovaReader.Features.Shared;
using HNovaReader.Infrastructure.Navigation;
using HNovaReader.Services.Library;
using HNovaReader.Services.Reader;
using HNovaReader.Services.Settings;

namespace HNovaReader.Features.Reader;

public enum ReaderWindowDirection
{
    Previous,
    Next,
}

public sealed partial class ReaderViewModel
    : PageViewModel, INavigationPageLifecycle, IDisposable
{
    public const int WindowCharacterCount = 96 * 1024;
    public const int WindowShiftCharacterCount = 48 * 1024;
    public const int MaxEditCharacterCount = 1024 * 1024;
    private static readonly TimeSpan PositionSaveDelay = TimeSpan.FromMilliseconds(750);
    private const string AnnotationReviewWarning =
        "检测到原始 TXT 已发生修改。部分书签、高亮或笔记的位置可能不准确。";
    private readonly SemaphoreSlim windowGate = new(1, 1);
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly IReaderEngineFactory readerEngineFactory;
    private readonly IReadingPositionService readingPositionService;
    private readonly IReaderAnnotationService annotationService;
    private readonly IAppSettingsService appSettingsService;
    private readonly ITextFileEditService textFileEditService;
    private readonly ILibraryService libraryService;
    private ITextDocumentSession? session;
    private BookDetails? currentBook;
    private CancellationTokenSource? sessionCancellationSource;
    private CancellationTokenSource? positionSaveCancellationSource;
    private long lastSavedCharacterOffset = -1;
    private ReaderTextSelection? pendingNoteSelection;
    private long? pendingNoteCharacterOffset;
    private Guid? editingNoteId;
    private long editStartCharacterOffset;
    private int editOriginalLength;

    public ReaderViewModel(
        IReaderEngineFactory readerEngineFactory,
        IReadingPositionService readingPositionService,
        IReaderAnnotationService annotationService,
        IAppSettingsService appSettingsService,
        ITextFileEditService textFileEditService,
        ILibraryService libraryService)
        : base("阅读", "连续滚动阅读")
    {
        this.readerEngineFactory = readerEngineFactory;
        this.readingPositionService = readingPositionService;
        this.annotationService = annotationService;
        this.appSettingsService = appSettingsService;
        this.textFileEditService = textFileEditService;
        this.libraryService = libraryService;
        ApplyReaderSettings(appSettingsService.Current);
        appSettingsService.SettingsChanged += OnSettingsChanged;
    }

    public ObservableCollection<ChapterInfo> Chapters { get; } = [];

    public ObservableCollection<BookmarkSnapshot> Bookmarks { get; } = [];

    public ObservableCollection<HighlightSnapshot> Highlights { get; } = [];

    public ObservableCollection<NoteSnapshot> Notes { get; } = [];

    [ObservableProperty]
    private string bookTitle = "未打开书籍";

    [ObservableProperty]
    private string author = "未知作者";

    [ObservableProperty]
    private TextReadWindow? currentWindow;

    [ObservableProperty]
    private long restoreCharacterOffset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private long currentCharacterOffset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private long totalCharacterCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private double readingProgress;

    [ObservableProperty]
    private double indexingProgress;

    [ObservableProperty]
    private string indexStatus = "等待打开书籍";

    [ObservableProperty]
    private string currentChapterTitle = "正文";

    [ObservableProperty]
    private string? warningMessage;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isLoadingText;

    [ObservableProperty]
    private bool isIndexing;

    [ObservableProperty]
    private bool isCatalogVisible;

    [ObservableProperty]
    private bool isAnnotationsVisible;

    [ObservableProperty]
    private bool isFocusMode;

    [ObservableProperty]
    private double readerFontSize = 18;

    [ObservableProperty]
    private string readerFontFamily = "Microsoft YaHei UI";

    [ObservableProperty]
    private double readerLineHeight = 1.75;

    [ObservableProperty]
    private double readerParagraphSpacing;

    [ObservableProperty]
    private double readerMaximumContentWidth = 1040;

    [ObservableProperty]
    private double readerHorizontalMargin = 64;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReaderThemeDisplay))]
    private ReaderTheme readerTheme = ReaderTheme.Sepia;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPagedLayout))]
    [NotifyPropertyChangedFor(nameof(ReadingLayoutText))]
    private ReaderLayoutMode readingLayoutMode;

    [ObservableProperty]
    private string contentLayoutVersion = string.Empty;

    [ObservableProperty]
    private int annotationsRevision;

    [ObservableProperty]
    private bool hasAnnotationsNeedingReview;

    [ObservableProperty]
    private bool isNoteEditorVisible;

    [ObservableProperty]
    private string noteDraftText = string.Empty;

    [ObservableProperty]
    private string noteDraftPreview = string.Empty;

    [ObservableProperty]
    private bool isEditMode;

    [ObservableProperty]
    private bool isSavingEdit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditCharacterCountText))]
    private string editText = string.Empty;

    [ObservableProperty]
    private string? editStatusMessage;

    public bool IsPagedLayout => ReadingLayoutMode == ReaderLayoutMode.Paged;

    public string ReadingLayoutText => IsPagedLayout
        ? "分页阅读 · Paged"
        : "连续滚动 · Scroll";

    public string ReaderThemeDisplay => ReaderTheme switch
    {
        ReaderTheme.Light => "浅色",
        ReaderTheme.Dark => "深色",
        _ => "护眼",
    };

    public string EditCharacterCountText =>
        $"{EditText.Length:N0} / {MaxEditCharacterCount:N0} 字符";

    public string ProgressText => TotalCharacterCount > 0
        ? $"{ReadingProgress:P1} · {CurrentCharacterOffset:N0} / {TotalCharacterCount:N0} 字符"
        : $"位置 {CurrentCharacterOffset:N0} · 正在计算全书长度";

    public Task OpenBookAsync(BookDetails book, CancellationToken cancellationToken)
    {
        return OpenBookCoreAsync(
            book,
            explicitCharacterOffset: null,
            suppressFileChangeWarning: false,
            cancellationToken);
    }

    private async Task OpenBookCoreAsync(
        BookDetails book,
        long? explicitCharacterOffset,
        bool suppressFileChangeWarning,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (!explicitCharacterOffset.HasValue)
        {
            ResetEditState();
            IsCatalogVisible = false;
            IsAnnotationsVisible = false;
            IsFocusMode = false;
        }

        await CloseSessionAsync(savePosition: true, CancellationToken.None);

        ErrorMessage = null;
        WarningMessage = null;
        BookTitle = book.Title;
        Author = string.IsNullOrWhiteSpace(book.AuthorName) ? "未知作者" : book.AuthorName;
        currentBook = book;
        Chapters.Clear();
        ClearAnnotations();
        TotalCharacterCount = 0;
        ReadingProgress = 0;
        CurrentCharacterOffset = 0;
        CurrentChapterTitle = "正文";
        IndexingProgress = 0;
        IndexStatus = "正在准备文本…";

        var savedPosition = await readingPositionService.GetAsync(book.Id, cancellationToken);
        var currentFileVersion = ReaderFileVersion.FromPath(book.FilePath);
        var initialCharacterOffset = explicitCharacterOffset
            ?? savedPosition?.CharacterOffset
            ?? 0;
        if (!explicitCharacterOffset.HasValue
            && savedPosition is not null
            && savedPosition.FileVersion != currentFileVersion)
        {
            initialCharacterOffset = 0;
            if (!suppressFileChangeWarning)
            {
                WarningMessage = "TXT 文件自上次阅读后发生了变化，已从开头重新定位。";
            }
        }

        sessionCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progress = new Progress<TextIndexProgress>(OnIndexProgress);
        try
        {
            session = await readerEngineFactory.OpenAsync(
                new ReaderOpenRequest(
                    book.Id,
                    book.FilePath,
                    book.FileEncoding,
                    initialCharacterOffset,
                    WindowCharacterCount),
                progress,
                sessionCancellationSource.Token);
            ContentLayoutVersion = CreateContentLayoutVersion(session);
            CurrentWindow = session.InitialWindow;
            RestoreCharacterOffset = ClampToWindow(initialCharacterOffset, session.InitialWindow);
            CurrentCharacterOffset = RestoreCharacterOffset;
            lastSavedCharacterOffset = explicitCharacterOffset.HasValue
                ? -1
                : savedPosition?.CharacterOffset ?? -1;

            if (session.CurrentIndex is { } index)
            {
                ApplyIndex(index);
            }
            else
            {
                IsIndexing = true;
                IndexStatus = "正在后台建立章节与字符索引…";
            }

            UpdatePositionDerivedState();
            await LoadAnnotationsAsync(session.OpenedFileVersion, cancellationToken);
            _ = ObserveIndexingAsync(session, sessionCancellationSource.Token);
        }
        catch
        {
            await CloseSessionAsync(savePosition: false, CancellationToken.None);
            throw;
        }
    }

    public async Task MoveWindowAsync(
        ReaderWindowDirection direction,
        long visibleCharacterOffset,
        CancellationToken cancellationToken = default)
    {
        var activeSession = session;
        var window = CurrentWindow;
        if (activeSession is null || window is null)
        {
            return;
        }

        if ((direction == ReaderWindowDirection.Previous && window.IsStartOfDocument)
            || (direction == ReaderWindowDirection.Next && window.IsEndOfDocument))
        {
            return;
        }

        var requestedStart = direction == ReaderWindowDirection.Previous
            ? Math.Max(0, window.StartCharacterOffset - WindowShiftCharacterCount)
            : window.StartCharacterOffset + WindowShiftCharacterCount;
        if (direction == ReaderWindowDirection.Next && requestedStart >= window.EndCharacterOffset)
        {
            return;
        }

        await LoadWindowAsync(
            activeSession,
            requestedStart,
            visibleCharacterOffset,
            cancellationToken);
    }

    public async Task MovePagedWindowAsync(
        ReaderWindowDirection direction,
        long pageStartCharacterOffset,
        CancellationToken cancellationToken = default)
    {
        var activeSession = session;
        var window = CurrentWindow;
        if (activeSession is null || window is null)
        {
            return;
        }

        var anchor = Math.Max(0, pageStartCharacterOffset);
        if ((direction == ReaderWindowDirection.Previous && anchor <= 0)
            || (direction == ReaderWindowDirection.Next && window.IsEndOfDocument))
        {
            return;
        }

        var requestedStart = direction == ReaderWindowDirection.Previous
            ? Math.Max(0, anchor - WindowShiftCharacterCount)
            : anchor;
        await LoadWindowAsync(
            activeSession,
            requestedStart,
            anchor,
            cancellationToken);
    }

    public void UpdateVisibleAnchor(long characterOffset)
    {
        if (CurrentWindow is not { } window || session is null)
        {
            return;
        }

        CurrentCharacterOffset = ClampToWindow(characterOffset, window);
        UpdatePositionDerivedState();
        SchedulePositionSave();
    }

    public async Task FlushPositionAsync(CancellationToken cancellationToken)
    {
        CancelPendingPositionSave();
        await SavePositionCoreAsync(force: true, cancellationToken);
    }

    public Task OnNavigatedFromAsync(CancellationToken cancellationToken)
    {
        ResetEditState();
        return CloseSessionAsync(savePosition: true, cancellationToken);
    }

    public void Dispose()
    {
        appSettingsService.SettingsChanged -= OnSettingsChanged;
    }

    [RelayCommand]
    private async Task ApplyReaderThemeAsync(ReaderTheme theme)
    {
        ReaderTheme = theme;
        try
        {
            await appSettingsService.SaveAsync(
                appSettingsService.Current with { ReaderTheme = theme },
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            WarningMessage = $"阅读主题已切换，但未能保存：{exception.Message}";
        }
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (IsEditMode || IsSavingEdit)
        {
            return;
        }

        if (CurrentWindow is not { } window || session is null || currentBook is null)
        {
            WarningMessage = "请先打开一本 TXT 书籍再编辑。";
            return;
        }

        editStartCharacterOffset = window.StartCharacterOffset;
        editOriginalLength = window.Text.Length;
        EditText = window.Text;
        EditStatusMessage = "保存将更新 TXT；切换只读会放弃未保存的修改。";
        ErrorMessage = null;
        IsEditMode = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        if (!IsSavingEdit)
        {
            ResetEditState();
        }
    }

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        var activeSession = session;
        var book = currentBook;
        if (!IsEditMode || activeSession is null || book is null)
        {
            return;
        }

        if (EditText.Length > MaxEditCharacterCount)
        {
            ErrorMessage = $"单次编辑不能超过 {MaxEditCharacterCount:N0} 个字符。";
            return;
        }

        var replacementText = EditText;
        var expectedVersion = activeSession.OpenedFileVersion;
        var encodingName = activeSession.EncodingName;
        var relativeAnchor = Math.Clamp(
            CurrentCharacterOffset - editStartCharacterOffset,
            0,
            editOriginalLength);
        var reopenAnchor = editStartCharacterOffset
            + Math.Min(relativeAnchor, replacementText.Length);
        var sessionWasClosed = false;

        IsSavingEdit = true;
        ErrorMessage = null;
        EditStatusMessage = "正在安全保存并重新载入…";
        try
        {
            await SavePositionCoreAsync(force: true, CancellationToken.None);
            await CloseSessionAsync(savePosition: false, CancellationToken.None);
            sessionWasClosed = true;

            var result = await textFileEditService.ReplaceRangeAsync(
                new TextFileRangeEditRequest(
                    book.FilePath,
                    encodingName,
                    expectedVersion,
                    editStartCharacterOffset,
                    editOriginalLength,
                    replacementText),
                CancellationToken.None);
            var refreshedBook = await libraryService.RefreshBookFileInfoAsync(
                book.Id,
                new RefreshBookFileInfoRequest
                {
                    FileSize = result.FileVersion.FileSize,
                    LastWriteTimeUtc = result.FileVersion.LastWriteTimeUtc,
                    FileEncoding = encodingName,
                },
                CancellationToken.None)
                ?? book with
                {
                    FileSize = result.FileVersion.FileSize,
                    LastWriteTimeUtc = result.FileVersion.LastWriteTimeUtc,
                    FileEncoding = encodingName,
                };

            IsEditMode = false;
            EditText = string.Empty;
            await OpenBookCoreAsync(
                refreshedBook,
                reopenAnchor,
                suppressFileChangeWarning: true,
                CancellationToken.None);
            await SavePositionCoreAsync(force: true, CancellationToken.None);
            EditStatusMessage = "修改已保存。";
        }
        catch (Exception exception)
        {
            var failureMessage = $"修改未保存：{exception.Message}";
            ErrorMessage = failureMessage;
            EditStatusMessage = "原文件未被覆盖；请检查文件是否被其他程序修改或占用。";
            if (sessionWasClosed && File.Exists(book.FilePath))
            {
                try
                {
                    var currentVersion = ReaderFileVersion.FromPath(book.FilePath);
                    var recoveryBook = await libraryService.RefreshBookFileInfoAsync(
                        book.Id,
                        new RefreshBookFileInfoRequest
                        {
                            FileSize = currentVersion.FileSize,
                            LastWriteTimeUtc = currentVersion.LastWriteTimeUtc,
                            FileEncoding = encodingName,
                        },
                        CancellationToken.None)
                        ?? book with
                        {
                            FileSize = currentVersion.FileSize,
                            LastWriteTimeUtc = currentVersion.LastWriteTimeUtc,
                            FileEncoding = encodingName,
                        };
                    await OpenBookCoreAsync(
                        recoveryBook,
                        reopenAnchor,
                        suppressFileChangeWarning: true,
                        CancellationToken.None);
                    IsEditMode = false;
                }
                catch (Exception recoveryException)
                {
                    WarningMessage = $"重新打开 TXT 失败：{recoveryException.Message}";
                }
            }

            ErrorMessage = failureMessage;
        }
        finally
        {
            IsSavingEdit = false;
        }
    }

    [RelayCommand]
    private void ToggleCatalog()
    {
        IsFocusMode = false;
        IsCatalogVisible = !IsCatalogVisible;
    }

    [RelayCommand]
    private void ToggleAnnotations()
    {
        IsFocusMode = false;
        IsAnnotationsVisible = !IsAnnotationsVisible;
    }

    [RelayCommand]
    private void ToggleFocusMode()
    {
        IsFocusMode = !IsFocusMode;
        if (IsFocusMode)
        {
            catalogBeforeFocus = IsCatalogVisible;
            annotationsBeforeFocus = IsAnnotationsVisible;
            IsCatalogVisible = false;
            IsAnnotationsVisible = false;
        }
        else
        {
            IsCatalogVisible = catalogBeforeFocus;
            IsAnnotationsVisible = annotationsBeforeFocus;
        }
    }

    private bool catalogBeforeFocus;
    private bool annotationsBeforeFocus;

    [RelayCommand]
    private void IncreaseFontSize()
    {
        ReaderFontSize = Math.Min(30, ReaderFontSize + 1);
    }

    [RelayCommand]
    private void DecreaseFontSize()
    {
        ReaderFontSize = Math.Max(14, ReaderFontSize - 1);
    }

    [RelayCommand]
    private void UseScrollLayout()
    {
        ReadingLayoutMode = ReaderLayoutMode.Scroll;
    }

    [RelayCommand]
    private void UsePagedLayout()
    {
        ReadingLayoutMode = ReaderLayoutMode.Paged;
    }

    [RelayCommand]
    private async Task JumpToChapterAsync(ChapterInfo? chapter)
    {
        if (chapter is null || session is null)
        {
            return;
        }

        await LoadWindowAsync(
            session,
            chapter.StartOffset,
            chapter.StartOffset,
            sessionCancellationSource?.Token ?? CancellationToken.None);
        UpdateVisibleAnchor(chapter.StartOffset);
    }

    [RelayCommand]
    private async Task AddBookmarkAsync()
    {
        if (currentBook is null || session is null)
        {
            return;
        }

        try
        {
            var anchor = CurrentCharacterOffset;
            var (prefix, suffix) = GetRangeContext(anchor, 0);
            await annotationService.AddBookmarkAsync(
                new CreateBookmarkRequest(
                    currentBook.Id,
                    anchor,
                    FindCurrentChapter()?.Title,
                    GetPreviewAt(anchor),
                    prefix,
                    suffix,
                    session.OpenedFileVersion,
                    session.EncodingName),
                GetSessionToken());
            IsAnnotationsVisible = true;
            await LoadAnnotationsAsync(session.OpenedFileVersion, GetSessionToken());
        }
        catch (Exception exception)
        {
            WarningMessage = $"无法添加书签：{exception.Message}";
        }
    }

    public async Task AddHighlightAsync(
        ReaderTextSelection selection,
        HighlightStyle style,
        CancellationToken cancellationToken = default)
    {
        if (currentBook is null || session is null || !TryValidateSelection(selection))
        {
            return;
        }

        try
        {
            await annotationService.AddHighlightAsync(
                CreateHighlightRequest(selection, style),
                CombineSessionToken(cancellationToken));
            await LoadAnnotationsAsync(session.OpenedFileVersion, CombineSessionToken(cancellationToken));
        }
        catch (Exception exception)
        {
            WarningMessage = $"无法添加高亮：{exception.Message}";
        }
    }

    public void BeginSelectionNote(ReaderTextSelection selection)
    {
        if (!TryValidateSelection(selection))
        {
            return;
        }

        pendingNoteSelection = selection;
        pendingNoteCharacterOffset = null;
        editingNoteId = null;
        NoteDraftPreview = CreateDisplayPreview(selection.SelectedText, 220);
        NoteDraftText = string.Empty;
        IsNoteEditorVisible = true;
        IsAnnotationsVisible = true;
    }

    [RelayCommand]
    private void BeginPositionNote()
    {
        if (session is null)
        {
            return;
        }

        pendingNoteSelection = null;
        pendingNoteCharacterOffset = CurrentCharacterOffset;
        editingNoteId = null;
        NoteDraftPreview = GetPreviewAt(CurrentCharacterOffset);
        NoteDraftText = string.Empty;
        IsNoteEditorVisible = true;
        IsAnnotationsVisible = true;
    }

    [RelayCommand]
    private void EditNote(NoteSnapshot? note)
    {
        if (note is null)
        {
            return;
        }

        pendingNoteSelection = null;
        pendingNoteCharacterOffset = null;
        editingNoteId = note.Id;
        NoteDraftPreview = note.PreviewText;
        NoteDraftText = note.Content;
        IsNoteEditorVisible = true;
        IsAnnotationsVisible = true;
    }

    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        if (currentBook is null || session is null || string.IsNullOrWhiteSpace(NoteDraftText))
        {
            return;
        }

        try
        {
            var token = GetSessionToken();
            if (editingNoteId is { } noteId)
            {
                await annotationService.UpdateNoteAsync(
                    noteId,
                    NoteDraftText,
                    session.OpenedFileVersion,
                    token);
            }
            else if (pendingNoteSelection is { } selection)
            {
                await annotationService.AddHighlightNoteAsync(
                    new CreateHighlightNoteRequest(
                        CreateHighlightRequest(selection, HighlightStyle.Yellow),
                        NoteDraftText),
                    token);
            }
            else if (pendingNoteCharacterOffset is { } characterOffset)
            {
                var (prefix, suffix) = GetRangeContext(characterOffset, 0);
                await annotationService.AddPositionNoteAsync(
                    new CreatePositionNoteRequest(
                        currentBook.Id,
                        characterOffset,
                        GetPreviewAt(characterOffset),
                        NoteDraftText,
                        prefix,
                        suffix,
                        session.OpenedFileVersion,
                        session.EncodingName),
                    token);
            }

            CancelNote();
            await LoadAnnotationsAsync(session.OpenedFileVersion, token);
        }
        catch (Exception exception)
        {
            WarningMessage = $"无法保存笔记：{exception.Message}";
        }
    }

    [RelayCommand]
    private void CancelNote()
    {
        pendingNoteSelection = null;
        pendingNoteCharacterOffset = null;
        editingNoteId = null;
        NoteDraftText = string.Empty;
        NoteDraftPreview = string.Empty;
        IsNoteEditorVisible = false;
    }

    [RelayCommand]
    private Task JumpToBookmarkAsync(BookmarkSnapshot? bookmark)
    {
        return bookmark is null
            ? Task.CompletedTask
            : JumpToAnnotationOffsetAsync(bookmark.CharacterOffset, bookmark.NeedsReview);
    }

    [RelayCommand]
    private Task JumpToHighlightAsync(HighlightSnapshot? highlight)
    {
        return highlight is null
            ? Task.CompletedTask
            : JumpToAnnotationOffsetAsync(highlight.StartOffset, highlight.NeedsReview);
    }

    [RelayCommand]
    private Task JumpToNoteAsync(NoteSnapshot? note)
    {
        return note is null
            ? Task.CompletedTask
            : JumpToAnnotationOffsetAsync(note.CharacterOffset, note.NeedsReview);
    }

    [RelayCommand]
    private async Task DeleteBookmarkAsync(BookmarkSnapshot? bookmark)
    {
        if (bookmark is null || session is null)
        {
            return;
        }

        await DeleteAnnotationAsync(
            token => annotationService.DeleteBookmarkAsync(bookmark.Id, token),
            "书签");
    }

    [RelayCommand]
    private async Task DeleteHighlightAsync(HighlightSnapshot? highlight)
    {
        if (highlight is null || session is null)
        {
            return;
        }

        await DeleteAnnotationAsync(
            token => annotationService.DeleteHighlightAsync(highlight.Id, token),
            "高亮");
    }

    [RelayCommand]
    private async Task DeleteNoteAsync(NoteSnapshot? note)
    {
        if (note is null || session is null)
        {
            return;
        }

        await DeleteAnnotationAsync(
            token => annotationService.DeleteNoteAsync(note.Id, token),
            "笔记");
    }

    private async Task DeleteAnnotationAsync(
        Func<CancellationToken, Task> delete,
        string displayName)
    {
        if (session is null)
        {
            return;
        }

        try
        {
            var token = GetSessionToken();
            await delete(token);
            await LoadAnnotationsAsync(session.OpenedFileVersion, token);
        }
        catch (Exception exception)
        {
            WarningMessage = $"无法删除{displayName}：{exception.Message}";
        }
    }

    private async Task JumpToAnnotationOffsetAsync(long characterOffset, bool needsReview)
    {
        if (session is null)
        {
            return;
        }

        var anchor = Math.Max(0, characterOffset);
        if (TotalCharacterCount > 0)
        {
            anchor = Math.Min(anchor, TotalCharacterCount);
        }

        await LoadWindowAsync(session, anchor, anchor, GetSessionToken());
        UpdateVisibleAnchor(anchor);
        if (needsReview)
        {
            WarningMessage = AnnotationReviewWarning;
        }
    }

    private async Task LoadWindowAsync(
        ITextDocumentSession activeSession,
        long requestedStart,
        long restoreOffset,
        CancellationToken cancellationToken)
    {
        var linkedToken = sessionCancellationSource?.Token ?? cancellationToken;
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            linkedToken,
            cancellationToken);
        await windowGate.WaitAsync(operationCancellation.Token);
        IsLoadingText = true;
        try
        {
            if (!ReferenceEquals(session, activeSession))
            {
                return;
            }

            var window = await activeSession.ReadAsync(
                Math.Max(0, requestedStart),
                WindowCharacterCount,
                operationCancellation.Token);
            CurrentWindow = window;
            RestoreCharacterOffset = ClampToWindow(restoreOffset, window);
            CurrentCharacterOffset = RestoreCharacterOffset;
            UpdatePositionDerivedState();
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
        }
        catch (ReaderFileChangedException)
        {
            ErrorMessage = "TXT 文件在阅读期间发生了变化。请返回书库刷新文件信息后重新打开。";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"无法载入正文：{exception.Message}";
        }
        finally
        {
            IsLoadingText = false;
            windowGate.Release();
        }
    }

    private async Task ObserveIndexingAsync(
        ITextDocumentSession observedSession,
        CancellationToken cancellationToken)
    {
        try
        {
            var index = await observedSession.IndexingTask;
            if (ReferenceEquals(session, observedSession))
            {
                ApplyIndex(index);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ReaderFileChangedException)
        {
            if (ReferenceEquals(session, observedSession))
            {
                IsIndexing = false;
                ErrorMessage = "建立索引时检测到 TXT 文件变化，请返回书库刷新后重新打开。";
                IndexStatus = "索引已停止";
            }
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(session, observedSession))
            {
                IsIndexing = false;
                WarningMessage = $"章节索引暂不可用：{exception.Message}";
                IndexStatus = "可继续阅读，目录索引失败";
            }
        }
    }

    private void OnIndexProgress(TextIndexProgress progress)
    {
        if (session is null)
        {
            return;
        }

        IsIndexing = true;
        IndexingProgress = progress.Fraction;
        IndexStatus = $"正在建立索引 {progress.Fraction:P0} · {progress.CharactersProcessed:N0} 字符";
        foreach (var chapter in progress.ChaptersAdded)
        {
            if (!Chapters.Any(existing => existing.StartOffset == chapter.StartOffset))
            {
                Chapters.Add(chapter);
            }
        }
    }

    private void ApplyIndex(TextIndexSnapshot index)
    {
        Chapters.Clear();
        foreach (var chapter in index.Chapters)
        {
            Chapters.Add(chapter);
        }

        TotalCharacterCount = index.TotalCharacterCount;
        IsIndexing = !index.IsComplete;
        IndexingProgress = index.IsComplete ? 1 : 0;
        IndexStatus = index.IsComplete
            ? $"目录已就绪 · {index.Chapters.Count} 章"
            : "正在后台建立章节与字符索引…";
        UpdatePositionDerivedState();
    }

    private void UpdatePositionDerivedState()
    {
        ReadingProgress = TotalCharacterCount > 0
            ? Math.Clamp((double)CurrentCharacterOffset / TotalCharacterCount, 0, 1)
            : 0;
        CurrentChapterTitle = FindCurrentChapter()?.Title ?? "正文";
    }

    private ChapterInfo? FindCurrentChapter()
    {
        for (var index = Chapters.Count - 1; index >= 0; index--)
        {
            if (Chapters[index].StartOffset <= CurrentCharacterOffset)
            {
                return Chapters[index];
            }
        }

        return null;
    }

    private void SchedulePositionSave()
    {
        CancelPendingPositionSave();
        if (sessionCancellationSource is null)
        {
            return;
        }

        positionSaveCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
            sessionCancellationSource.Token);
        _ = SaveAfterDelayAsync(positionSaveCancellationSource.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(PositionSaveDelay, cancellationToken);
            await SavePositionCoreAsync(force: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            WarningMessage = $"阅读位置暂未保存：{exception.Message}";
        }
    }

    private async Task SavePositionCoreAsync(bool force, CancellationToken cancellationToken)
    {
        await saveGate.WaitAsync(cancellationToken);
        try
        {
            var activeSession = session;
            var book = currentBook;
            if (activeSession is null || book is null)
            {
                return;
            }

            var anchor = CurrentCharacterOffset;
            if (!force && anchor == lastSavedCharacterOffset)
            {
                return;
            }

            var (prefix, suffix) = GetAnchorContext(anchor);
            await readingPositionService.SaveAsync(
                new SaveReadingPositionRequest(
                    book.Id,
                    anchor,
                    FindByteOffsetHint(anchor),
                    ReadingProgress,
                    FindCurrentChapter()?.Title,
                    prefix,
                    suffix,
                    activeSession.OpenedFileVersion,
                    activeSession.EncodingName),
                cancellationToken);
            lastSavedCharacterOffset = anchor;
        }
        finally
        {
            saveGate.Release();
        }
    }

    private long? FindByteOffsetHint(long characterOffset)
    {
        var checkpoints = session?.CurrentIndex?.Checkpoints;
        if (checkpoints is null)
        {
            return null;
        }

        for (var index = checkpoints.Count - 1; index >= 0; index--)
        {
            if (checkpoints[index].CharacterOffset <= characterOffset)
            {
                return checkpoints[index].ByteOffset;
            }
        }

        return null;
    }

    private (string? Prefix, string? Suffix) GetAnchorContext(long characterOffset)
    {
        return GetRangeContext(characterOffset, 0);
    }

    private (string? Prefix, string? Suffix) GetRangeContext(
        long characterOffset,
        int selectedLength)
    {
        if (CurrentWindow is not { } window || window.Text.Length == 0)
        {
            return (null, null);
        }

        var localOffset = (int)Math.Clamp(
            characterOffset - window.StartCharacterOffset,
            0,
            window.Text.Length);
        var prefixStart = Math.Max(0, localOffset - 64);
        var suffixStart = Math.Min(window.Text.Length, localOffset + Math.Max(0, selectedLength));
        var suffixLength = Math.Min(64, window.Text.Length - suffixStart);
        return (
            localOffset > prefixStart
                ? window.Text[prefixStart..localOffset]
                : null,
            suffixLength > 0
                ? window.Text.Substring(suffixStart, suffixLength)
                : null);
    }

    private string GetPreviewAt(long characterOffset)
    {
        if (CurrentWindow is not { } window || window.Text.Length == 0)
        {
            return "文档末尾";
        }

        var localOffset = (int)Math.Clamp(
            characterOffset - window.StartCharacterOffset,
            0,
            window.Text.Length);
        var previewStart = Math.Max(0, localOffset - 28);
        var previewLength = Math.Min(120, window.Text.Length - previewStart);
        return CreateDisplayPreview(window.Text.Substring(previewStart, previewLength), 120);
    }

    private bool TryValidateSelection(ReaderTextSelection selection)
    {
        if (selection.Length is <= 0 or > 16384
            || selection.SelectedText.Length != selection.Length
            || string.IsNullOrWhiteSpace(selection.SelectedText)
            || CurrentWindow is not { } window)
        {
            return false;
        }

        var localStart = selection.StartOffset - window.StartCharacterOffset;
        return localStart >= 0
            && localStart <= window.Text.Length - selection.Length
            && string.Equals(
                window.Text.Substring((int)localStart, selection.Length),
                selection.SelectedText,
                StringComparison.Ordinal);
    }

    private CreateHighlightRequest CreateHighlightRequest(
        ReaderTextSelection selection,
        HighlightStyle style)
    {
        var (prefix, suffix) = GetRangeContext(selection.StartOffset, selection.Length);
        return new CreateHighlightRequest(
            currentBook?.Id ?? throw new InvalidOperationException("当前未打开书籍。"),
            selection.StartOffset,
            selection.Length,
            selection.SelectedText,
            style,
            prefix,
            suffix,
            session?.OpenedFileVersion ?? throw new InvalidOperationException("当前未打开阅读会话。"),
            session.EncodingName);
    }

    private async Task LoadAnnotationsAsync(
        ReaderFileVersion currentFileVersion,
        CancellationToken cancellationToken)
    {
        if (currentBook is null)
        {
            return;
        }

        var annotations = await annotationService.GetAsync(
            currentBook.Id,
            currentFileVersion,
            cancellationToken);
        ReplaceItems(Bookmarks, annotations.Bookmarks);
        ReplaceItems(Highlights, annotations.Highlights);
        ReplaceItems(Notes, annotations.Notes);
        HasAnnotationsNeedingReview = annotations.NeedsReview;
        AnnotationsRevision++;
        if (annotations.NeedsReview)
        {
            WarningMessage = AnnotationReviewWarning;
        }
        else if (WarningMessage == AnnotationReviewWarning)
        {
            WarningMessage = null;
        }
    }

    private void ClearAnnotations()
    {
        Bookmarks.Clear();
        Highlights.Clear();
        Notes.Clear();
        HasAnnotationsNeedingReview = false;
        CancelNote();
        AnnotationsRevision++;
    }

    private void ResetEditState()
    {
        IsEditMode = false;
        EditText = string.Empty;
        EditStatusMessage = null;
    }

    private CancellationToken GetSessionToken()
    {
        return sessionCancellationSource?.Token ?? CancellationToken.None;
    }

    private CancellationToken CombineSessionToken(CancellationToken cancellationToken)
    {
        return cancellationToken.CanBeCanceled ? cancellationToken : GetSessionToken();
    }

    private static string CreateDisplayPreview(string value, int maximumLength)
    {
        var normalized = string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
        {
            return "空白文本";
        }

        return normalized.Length <= maximumLength
            ? normalized
            : $"{normalized[..maximumLength]}…";
    }

    private static void ReplaceItems<T>(
        ObservableCollection<T> destination,
        IEnumerable<T> items)
    {
        destination.Clear();
        foreach (var item in items)
        {
            destination.Add(item);
        }
    }

    private void CancelPendingPositionSave()
    {
        positionSaveCancellationSource?.Cancel();
        positionSaveCancellationSource?.Dispose();
        positionSaveCancellationSource = null;
    }

    private async Task CloseSessionAsync(bool savePosition, CancellationToken cancellationToken)
    {
        CancelPendingPositionSave();
        try
        {
            if (savePosition && session is not null)
            {
                await SavePositionCoreAsync(force: true, cancellationToken);
            }
        }
        finally
        {
            var closingSession = session;
            session = null;
            currentBook = null;
            sessionCancellationSource?.Cancel();
            closingSession?.CancelIndexing();
            if (closingSession is not null)
            {
                await closingSession.DisposeAsync();
            }

            sessionCancellationSource?.Dispose();
            sessionCancellationSource = null;
            ContentLayoutVersion = string.Empty;
            CurrentWindow = null;
            ClearAnnotations();
            IsIndexing = false;
            IsLoadingText = false;
        }
    }

    private static long ClampToWindow(long characterOffset, TextReadWindow window)
    {
        return Math.Clamp(
            characterOffset,
            window.StartCharacterOffset,
            window.EndCharacterOffset);
    }

    private static string CreateContentLayoutVersion(ITextDocumentSession activeSession)
    {
        var version = activeSession.OpenedFileVersion;
        return $"{version.FileSize}:{version.LastWriteTimeUtc.Ticks}:{activeSession.EncodingName}";
    }

    partial void OnReadingLayoutModeChanged(ReaderLayoutMode value)
    {
        RestoreCharacterOffset = CurrentCharacterOffset;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        ApplyReaderSettings(appSettingsService.Current);
    }

    private void ApplyReaderSettings(AppSettings settings)
    {
        ReaderTheme = settings.ReaderTheme;
        ReaderFontFamily = settings.FontFamily;
        ReaderFontSize = settings.FontSize;
        ReaderLineHeight = settings.LineHeight;
        ReaderParagraphSpacing = settings.ParagraphSpacing;
        ReaderMaximumContentWidth = settings.MaximumContentWidth;
        ReaderHorizontalMargin = settings.HorizontalMargin;
        ReadingLayoutMode = settings.ReadingMode == ReadingModePreference.Paged
            ? ReaderLayoutMode.Paged
            : ReaderLayoutMode.Scroll;
    }
}
