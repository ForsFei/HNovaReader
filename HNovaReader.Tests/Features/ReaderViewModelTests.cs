using HNovaReader.Features.Reader;
using HNovaReader.Data.Entities;
using HNovaReader.Services.Reader;
using HNovaReader.Services.Settings;
using HNovaReader.Tests.TestSupport;

namespace HNovaReader.Tests.Features;

public sealed class ReaderViewModelTests
{
    [Fact]
    public void Panels_StartClosedAndFocusRestoresOnlyPreviouslyOpenedPanels()
    {
        var viewModel = new ReaderViewModel(
            new StubReaderEngineFactory(),
            new StubReadingPositionService(),
            new StubReaderAnnotationService(),
            new StubAppSettingsService(),
            new StubTextFileEditService(),
            new StubLibraryService());
        Assert.False(viewModel.IsCatalogVisible);
        Assert.False(viewModel.IsAnnotationsVisible);
        viewModel.ToggleCatalogCommand.Execute(null);
        viewModel.ToggleFocusModeCommand.Execute(null);
        Assert.False(viewModel.IsCatalogVisible);
        Assert.False(viewModel.IsAnnotationsVisible);
        viewModel.ToggleFocusModeCommand.Execute(null);
        Assert.True(viewModel.IsCatalogVisible);
        Assert.False(viewModel.IsAnnotationsVisible);
    }

    [Fact]
    public async Task OpenBookAsync_RestoresCharacterAnchorAndFlushesOnLeave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var filePath = await CreateTextFileAsync(cancellationToken);
        try
        {
            var book = TestBookFactory.Create(title: "锚点恢复", filePath: filePath);
            var version = ReaderFileVersion.FromPath(filePath);
            var chapter = new ChapterInfo("第二章", 2, 400, 1200);
            var index = CreateIndex(version, 5000, [chapter]);
            var textSession = new StubTextDocumentSession(
                book.Id,
                filePath,
                version,
                new TextReadWindow(450, new string('文', 1000), false, false),
                index);
            var engine = new StubReaderEngineFactory { Session = textSession };
            var positions = new StubReadingPositionService
            {
                Position = new ReadingPositionSnapshot(
                    book.Id,
                    450,
                    512,
                    0.09,
                    "第二章",
                    "前文",
                    "后文",
                    version,
                    "utf-8",
                    DateTime.UtcNow),
            };
            var viewModel = new ReaderViewModel(
                engine,
                positions,
                new StubReaderAnnotationService(),
                new StubAppSettingsService(),
                new StubTextFileEditService(),
                new StubLibraryService());

            await viewModel.OpenBookAsync(book, cancellationToken);

            Assert.Equal(450, engine.LastRequest?.InitialCharacterOffset);
            Assert.Equal(ReaderViewModel.WindowCharacterCount, engine.LastRequest?.InitialWindowLength);
            Assert.Equal(450, viewModel.CurrentCharacterOffset);
            Assert.Equal("第二章", viewModel.CurrentChapterTitle);
            Assert.Equal(5000, viewModel.TotalCharacterCount);
            Assert.Single(viewModel.Chapters);

            await viewModel.OnNavigatedFromAsync(cancellationToken);

            Assert.NotNull(positions.LastSaveRequest);
            Assert.Equal(450, positions.LastSaveRequest.CharacterOffset);
            Assert.Equal(512, positions.LastSaveRequest.ByteOffsetHint);
            Assert.True(textSession.IndexingCancelled);
            Assert.True(textSession.IsDisposed);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task MoveWindowAsync_LoadsNextBoundedWindowAndKeepsVisibleAnchor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var filePath = await CreateTextFileAsync(cancellationToken);
        try
        {
            var book = TestBookFactory.Create(title: "滑动窗口", filePath: filePath);
            var version = ReaderFileVersion.FromPath(filePath);
            var textSession = new StubTextDocumentSession(
                book.Id,
                filePath,
                version,
                new TextReadWindow(
                    0,
                    new string('甲', ReaderViewModel.WindowCharacterCount),
                    true,
                    false),
                CreateIndex(version, 300_000, []))
            {
                WindowReader = (offset, length) => new TextReadWindow(
                    offset,
                    new string('乙', length),
                    false,
                    false),
            };
            var viewModel = new ReaderViewModel(
                new StubReaderEngineFactory { Session = textSession },
                new StubReadingPositionService(),
                new StubReaderAnnotationService(),
                new StubAppSettingsService(),
                new StubTextFileEditService(),
                new StubLibraryService());
            await viewModel.OpenBookAsync(book, cancellationToken);

            await viewModel.MoveWindowAsync(
                ReaderWindowDirection.Next,
                80_000,
                cancellationToken);

            var request = Assert.Single(textSession.ReadRequests);
            Assert.Equal(ReaderViewModel.WindowShiftCharacterCount, request.Offset);
            Assert.Equal(ReaderViewModel.WindowCharacterCount, request.Length);
            Assert.Equal(80_000, viewModel.RestoreCharacterOffset);
            Assert.Equal(80_000, viewModel.CurrentCharacterOffset);
            Assert.Equal(ReaderViewModel.WindowShiftCharacterCount, viewModel.CurrentWindow?.StartCharacterOffset);

            await viewModel.OnNavigatedFromAsync(cancellationToken);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task JumpToChapterCommand_LoadsChapterAtTopAndUpdatesProgress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var filePath = await CreateTextFileAsync(cancellationToken);
        try
        {
            var book = TestBookFactory.Create(title: "目录跳转", filePath: filePath);
            var version = ReaderFileVersion.FromPath(filePath);
            var chapter = new ChapterInfo("第三章", 2, 200_000, 260_000);
            var textSession = new StubTextDocumentSession(
                book.Id,
                filePath,
                version,
                new TextReadWindow(0, new string('甲', 1000), true, false),
                CreateIndex(version, 400_000, [chapter]))
            {
                WindowReader = (offset, length) => new TextReadWindow(
                    offset,
                    new string('丙', length),
                    false,
                    false),
            };
            var viewModel = new ReaderViewModel(
                new StubReaderEngineFactory { Session = textSession },
                new StubReadingPositionService(),
                new StubReaderAnnotationService(),
                new StubAppSettingsService(),
                new StubTextFileEditService(),
                new StubLibraryService());
            await viewModel.OpenBookAsync(book, cancellationToken);

            await viewModel.JumpToChapterCommand.ExecuteAsync(chapter);

            var request = Assert.Single(textSession.ReadRequests);
            Assert.Equal(chapter.StartOffset, request.Offset);
            Assert.Equal(chapter.StartOffset, viewModel.RestoreCharacterOffset);
            Assert.Equal("第三章", viewModel.CurrentChapterTitle);
            Assert.Equal(0.5, viewModel.ReadingProgress);

            await viewModel.OnNavigatedFromAsync(cancellationToken);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task OpenBookAsync_WhenFileVersionChanged_ResetsStaleAnchor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var filePath = await CreateTextFileAsync(cancellationToken);
        try
        {
            var book = TestBookFactory.Create(title: "文件变化", filePath: filePath);
            var currentVersion = ReaderFileVersion.FromPath(filePath);
            var textSession = new StubTextDocumentSession(
                book.Id,
                filePath,
                currentVersion,
                new TextReadWindow(0, "正文", true, true),
                CreateIndex(currentVersion, 2, []));
            var engine = new StubReaderEngineFactory { Session = textSession };
            var positions = new StubReadingPositionService
            {
                Position = new ReadingPositionSnapshot(
                    book.Id,
                    1000,
                    null,
                    0.5,
                    null,
                    null,
                    null,
                    currentVersion with { FileSize = currentVersion.FileSize + 1 },
                    "utf-8",
                    DateTime.UtcNow),
            };
            var viewModel = new ReaderViewModel(
                engine,
                positions,
                new StubReaderAnnotationService(),
                new StubAppSettingsService(),
                new StubTextFileEditService(),
                new StubLibraryService());

            await viewModel.OpenBookAsync(book, cancellationToken);

            Assert.Equal(0, engine.LastRequest?.InitialCharacterOffset);
            Assert.Contains("发生了变化", viewModel.WarningMessage);
            Assert.Equal(0, viewModel.CurrentCharacterOffset);

            await viewModel.OnNavigatedFromAsync(cancellationToken);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task LayoutCommands_SwitchModeWithoutChangingCharacterAnchor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var filePath = await CreateTextFileAsync(cancellationToken);
        try
        {
            var book = TestBookFactory.Create(title: "阅读布局", filePath: filePath);
            var version = ReaderFileVersion.FromPath(filePath);
            var textSession = new StubTextDocumentSession(
                book.Id,
                filePath,
                version,
                new TextReadWindow(0, new string('甲', 2000), true, true),
                CreateIndex(version, 2000, []));
            var viewModel = new ReaderViewModel(
                new StubReaderEngineFactory { Session = textSession },
                new StubReadingPositionService(),
                new StubReaderAnnotationService(),
                new StubAppSettingsService(),
                new StubTextFileEditService(),
                new StubLibraryService());
            await viewModel.OpenBookAsync(book, cancellationToken);
            viewModel.UpdateVisibleAnchor(640);

            viewModel.UsePagedLayoutCommand.Execute(null);

            Assert.Equal(ReaderLayoutMode.Paged, viewModel.ReadingLayoutMode);
            Assert.True(viewModel.IsPagedLayout);
            Assert.Equal("分页阅读 · Paged", viewModel.ReadingLayoutText);
            Assert.Equal(640, viewModel.CurrentCharacterOffset);
            Assert.Equal(640, viewModel.RestoreCharacterOffset);

            viewModel.UseScrollLayoutCommand.Execute(null);

            Assert.Equal(ReaderLayoutMode.Scroll, viewModel.ReadingLayoutMode);
            Assert.Equal(640, viewModel.CurrentCharacterOffset);
            await viewModel.OnNavigatedFromAsync(cancellationToken);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task MovePagedWindowAsync_LoadsLeadingTextAndRestoresPageAnchor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var filePath = await CreateTextFileAsync(cancellationToken);
        try
        {
            var book = TestBookFactory.Create(title: "向前分页", filePath: filePath);
            var version = ReaderFileVersion.FromPath(filePath);
            var textSession = new StubTextDocumentSession(
                book.Id,
                filePath,
                version,
                new TextReadWindow(
                    90_000,
                    new string('甲', ReaderViewModel.WindowCharacterCount),
                    false,
                    false),
                CreateIndex(version, 300_000, []))
            {
                WindowReader = (offset, length) => new TextReadWindow(
                    offset,
                    new string('乙', length),
                    offset == 0,
                    false),
            };
            var viewModel = new ReaderViewModel(
                new StubReaderEngineFactory { Session = textSession },
                new StubReadingPositionService(),
                new StubReaderAnnotationService(),
                new StubAppSettingsService(),
                new StubTextFileEditService(),
                new StubLibraryService());
            await viewModel.OpenBookAsync(book, cancellationToken);

            await viewModel.MovePagedWindowAsync(
                ReaderWindowDirection.Previous,
                100_000,
                cancellationToken);

            var request = Assert.Single(textSession.ReadRequests);
            Assert.Equal(100_000 - ReaderViewModel.WindowShiftCharacterCount, request.Offset);
            Assert.Equal(100_000, viewModel.RestoreCharacterOffset);
            Assert.Equal(100_000, viewModel.CurrentCharacterOffset);

            await viewModel.OnNavigatedFromAsync(cancellationToken);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task OpenBookAsync_WhenAnnotationsUseOldFileVersion_MarksReviewAndWarns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var filePath = await CreateTextFileAsync(cancellationToken);
        try
        {
            var book = TestBookFactory.Create(title: "标注复核", filePath: filePath);
            var version = ReaderFileVersion.FromPath(filePath);
            var oldVersion = version with { FileSize = version.FileSize + 1 };
            var annotations = new StubReaderAnnotationService
            {
                Annotations = new ReaderAnnotationsSnapshot(
                    [new BookmarkSnapshot(
                        Guid.NewGuid(), book.Id, 2, "正文", "预览", null, null,
                        oldVersion, "utf-8", DateTime.UtcNow, true)],
                    [],
                    []),
            };
            var session = new StubTextDocumentSession(
                book.Id,
                filePath,
                version,
                new TextReadWindow(0, "测试正文", true, true),
                CreateIndex(version, 4, []));
            var viewModel = new ReaderViewModel(
                new StubReaderEngineFactory { Session = session },
                new StubReadingPositionService(),
                annotations,
                new StubAppSettingsService(),
                new StubTextFileEditService(),
                new StubLibraryService());

            await viewModel.OpenBookAsync(book, cancellationToken);

            Assert.True(viewModel.HasAnnotationsNeedingReview);
            Assert.Single(viewModel.Bookmarks);
            Assert.Equal(
                "检测到原始 TXT 已发生修改。部分书签、高亮或笔记的位置可能不准确。",
                viewModel.WarningMessage);
            await viewModel.OnNavigatedFromAsync(cancellationToken);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task AnnotationActions_PreserveSourceOffsetsContextAndSelectionText()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var filePath = await CreateTextFileAsync(cancellationToken);
        try
        {
            var text = "前文甲乙丙丁选中的原始文字后文戊己庚辛";
            var selectionText = "选中的原始文字";
            var selectionStart = text.IndexOf(selectionText, StringComparison.Ordinal);
            var book = TestBookFactory.Create(title: "标注操作", filePath: filePath);
            var version = ReaderFileVersion.FromPath(filePath);
            var annotations = new StubReaderAnnotationService();
            var session = new StubTextDocumentSession(
                book.Id,
                filePath,
                version,
                new TextReadWindow(0, text, true, true),
                CreateIndex(version, text.Length, [new ChapterInfo("第一章", 1, 0, text.Length)]));
            var viewModel = new ReaderViewModel(
                new StubReaderEngineFactory { Session = session },
                new StubReadingPositionService(),
                annotations,
                new StubAppSettingsService(),
                new StubTextFileEditService(),
                new StubLibraryService());
            await viewModel.OpenBookAsync(book, cancellationToken);
            viewModel.UpdateVisibleAnchor(selectionStart);

            await viewModel.AddBookmarkCommand.ExecuteAsync(null);
            var selection = new ReaderTextSelection(
                selectionStart,
                selectionText.Length,
                selectionText);
            await viewModel.AddHighlightAsync(selection, HighlightStyle.Blue, cancellationToken);
            viewModel.BeginSelectionNote(selection);
            viewModel.NoteDraftText = "这是一条选择笔记";
            await viewModel.SaveNoteCommand.ExecuteAsync(null);

            Assert.Equal(selectionStart, annotations.LastBookmarkRequest?.CharacterOffset);
            Assert.Equal("第一章", annotations.LastBookmarkRequest?.ChapterTitleSnapshot);
            Assert.Equal(selectionText, annotations.LastHighlightRequest?.SelectedText);
            Assert.Equal(HighlightStyle.Blue, annotations.LastHighlightRequest?.Style);
            Assert.Equal("这是一条选择笔记", annotations.LastHighlightNoteRequest?.Content);
            Assert.Equal("前文甲乙丙丁", annotations.LastHighlightNoteRequest?.Highlight.AnchorPrefix);
            Assert.StartsWith("后文戊己庚辛", annotations.LastHighlightNoteRequest?.Highlight.AnchorSuffix);
            Assert.Single(viewModel.Bookmarks);
            Assert.Equal(2, viewModel.Highlights.Count);
            Assert.Single(viewModel.Notes);
            Assert.False(viewModel.IsNoteEditorVisible);
            await viewModel.OnNavigatedFromAsync(cancellationToken);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task SaveEditCommand_ReplacesCurrentWindowRefreshesMetadataAndReopensAtAnchor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var filePath = await CreateTextFileAsync(cancellationToken);
        try
        {
            const string originalText = "甲乙旧内容丁戊";
            const string replacementText = "甲乙更新后的内容丁戊";
            var book = TestBookFactory.Create(title: "编辑保存", filePath: filePath);
            var originalVersion = ReaderFileVersion.FromPath(filePath);
            var editedVersion = originalVersion with
            {
                FileSize = originalVersion.FileSize + 12,
                LastWriteTimeUtc = originalVersion.LastWriteTimeUtc.AddSeconds(1),
            };
            var firstSession = new StubTextDocumentSession(
                book.Id,
                filePath,
                originalVersion,
                new TextReadWindow(0, originalText, true, true),
                CreateIndex(originalVersion, originalText.Length, []));
            var reopenedSession = new StubTextDocumentSession(
                book.Id,
                filePath,
                editedVersion,
                new TextReadWindow(0, replacementText, true, true),
                CreateIndex(editedVersion, replacementText.Length, []));
            var openCount = 0;
            var engine = new StubReaderEngineFactory
            {
                SessionFactory = _ => openCount++ == 0 ? firstSession : reopenedSession,
            };
            var editor = new StubTextFileEditService
            {
                Result = new TextFileRangeEditResult(editedVersion),
            };
            var library = new StubLibraryService();
            library.Books.Add(book);
            using var viewModel = new ReaderViewModel(
                engine,
                new StubReadingPositionService(),
                new StubReaderAnnotationService(),
                new StubAppSettingsService(),
                editor,
                library);
            await viewModel.OpenBookAsync(book, cancellationToken);
            viewModel.UpdateVisibleAnchor(3);

            viewModel.BeginEditCommand.Execute(null);
            viewModel.EditText = replacementText;
            await viewModel.SaveEditCommand.ExecuteAsync(null);

            Assert.False(viewModel.IsEditMode);
            Assert.Equal("修改已保存。", viewModel.EditStatusMessage);
            Assert.True(firstSession.IsDisposed);
            Assert.Equal(2, engine.Requests.Count);
            Assert.Equal(3, engine.Requests[1].InitialCharacterOffset);
            Assert.Equal(0, editor.LastRequest?.StartCharacterOffset);
            Assert.Equal(originalText.Length, editor.LastRequest?.OriginalLength);
            Assert.Equal(replacementText, editor.LastRequest?.ReplacementText);
            Assert.Equal(originalVersion, editor.LastRequest?.ExpectedVersion);
            Assert.Equal(editedVersion.FileSize, library.Books.Single().FileSize);
            Assert.Equal(editedVersion.LastWriteTimeUtc, library.Books.Single().LastWriteTimeUtc);
            Assert.Same(reopenedSession.InitialWindow, viewModel.CurrentWindow);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void SettingsChanged_AppliesIndependentReaderAppearanceAndLayout()
    {
        var settings = new StubAppSettingsService();
        using var viewModel = new ReaderViewModel(
            new StubReaderEngineFactory(),
            new StubReadingPositionService(),
            new StubReaderAnnotationService(),
            settings,
            new StubTextFileEditService(),
            new StubLibraryService());

        settings.Set(new AppSettings
        {
            ReaderTheme = ReaderTheme.Dark,
            FontFamily = "霞鹜文楷",
            FontSize = 23,
            LineHeight = 2,
            ParagraphSpacing = 11,
            MaximumContentWidth = 980,
            HorizontalMargin = 88,
            ReadingMode = ReadingModePreference.Paged,
        });

        Assert.Equal(ReaderTheme.Dark, viewModel.ReaderTheme);
        Assert.Equal("霞鹜文楷", viewModel.ReaderFontFamily);
        Assert.Equal(23, viewModel.ReaderFontSize);
        Assert.Equal(2, viewModel.ReaderLineHeight);
        Assert.Equal(11, viewModel.ReaderParagraphSpacing);
        Assert.Equal(980, viewModel.ReaderMaximumContentWidth);
        Assert.Equal(88, viewModel.ReaderHorizontalMargin);
        Assert.Equal(ReaderLayoutMode.Paged, viewModel.ReadingLayoutMode);
    }

    [Fact]
    public async Task ApplyReaderThemeCommand_UpdatesAndPersistsReaderTheme()
    {
        var settings = new StubAppSettingsService();
        using var viewModel = new ReaderViewModel(
            new StubReaderEngineFactory(),
            new StubReadingPositionService(),
            new StubReaderAnnotationService(),
            settings,
            new StubTextFileEditService(),
            new StubLibraryService());

        await viewModel.ApplyReaderThemeCommand.ExecuteAsync(ReaderTheme.Dark);

        Assert.Equal(ReaderTheme.Dark, viewModel.ReaderTheme);
        Assert.Equal("深色", viewModel.ReaderThemeDisplay);
        Assert.Equal(ReaderTheme.Dark, settings.Current.ReaderTheme);
    }

    private static TextIndexSnapshot CreateIndex(
        ReaderFileVersion version,
        long totalCharacterCount,
        IReadOnlyList<ChapterInfo> chapters)
    {
        return new TextIndexSnapshot(
            version,
            "utf-8",
            1,
            totalCharacterCount,
            true,
            [new TextIndexCheckpoint(0, 0), new TextIndexCheckpoint(400, 512)],
            chapters);
    }

    private static async Task<string> CreateTextFileAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"HNovaReader-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "测试正文", cancellationToken);
        return path;
    }
}
