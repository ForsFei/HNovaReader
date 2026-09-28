using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HNovaReader.Data.Entities;
using HNovaReader.Services.Reader;
using HNovaReader.Services.Settings;

namespace HNovaReader.Features.Reader;

public partial class ReaderView : UserControl
{
    private readonly List<RenderedTextSegment> renderedSegments = [];
    private readonly PagedLayoutState pagedLayoutState = new();
    private readonly DispatcherTimer pagedReflowTimer;
    private ReaderViewModel? viewModel;
    private ReaderTextSelection? activeSelection;
    private DynamicDocumentPaginator? pagedPaginator;
    private ReaderWindowDirection? pendingPagedNavigation;
    private long pendingPagedAnchor;
    private int displayedPagedPageNumber;
    private bool renderScheduled;
    private bool isRendering;
    private bool isMovingWindow;
    private bool isPagedNavigation;
    private bool pagedAnchorUpdateScheduled;
    private DateTimeOffset suppressEdgeLoadingUntil;

    public ReaderView()
    {
        InitializeComponent();
        pagedReflowTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(180),
            DispatcherPriority.Background,
            OnPagedReflowTimerTick,
            Dispatcher)
        {
            IsEnabled = false,
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as ReaderViewModel);
        ScheduleRender();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        pagedReflowTimer.Stop();
        AttachViewModel(null);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachViewModel(e.NewValue as ReaderViewModel);
        ScheduleRender();
    }

    private void AttachViewModel(ReaderViewModel? candidate)
    {
        if (ReferenceEquals(viewModel, candidate))
        {
            return;
        }

        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        viewModel = candidate;
        pagedLayoutState.Reset(candidate?.CurrentCharacterOffset ?? 0);
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyReaderChrome();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReaderViewModel.ReaderTheme))
        {
            ApplyReaderChrome();
        }

        if (e.PropertyName is nameof(ReaderViewModel.CurrentWindow)
            or nameof(ReaderViewModel.RestoreCharacterOffset)
            or nameof(ReaderViewModel.ReaderFontSize)
            or nameof(ReaderViewModel.ReaderFontFamily)
            or nameof(ReaderViewModel.ReaderLineHeight)
            or nameof(ReaderViewModel.ReaderParagraphSpacing)
            or nameof(ReaderViewModel.ReaderMaximumContentWidth)
            or nameof(ReaderViewModel.ReaderHorizontalMargin)
            or nameof(ReaderViewModel.ReaderTheme)
            or nameof(ReaderViewModel.ReadingLayoutMode)
            or nameof(ReaderViewModel.ContentLayoutVersion)
            or nameof(ReaderViewModel.AnnotationsRevision))
        {
            ScheduleRender();
        }
    }

    private void ScheduleRender()
    {
        if (renderScheduled || !IsLoaded)
        {
            return;
        }

        renderScheduled = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                renderScheduled = false;
                RenderCurrentWindow();
            });
    }

    private void RenderCurrentWindow()
    {
        isRendering = true;
        try
        {
            if (viewModel?.ReadingLayoutMode == ReaderLayoutMode.Paged)
            {
                RenderPagedDocument();
            }
            else
            {
                RenderScrollDocument();
            }
        }
        finally
        {
            isRendering = false;
        }
    }

    private void RenderScrollDocument()
    {
        pagedReflowTimer.Stop();
        pendingPagedNavigation = null;
        pagedPaginator = null;
        PagedReader.Document = null;
        renderedSegments.Clear();

        var window = viewModel?.CurrentWindow;
        ReaderBox.Document = CreateDocument(window, GetPagePadding(isPaged: false));
        suppressEdgeLoadingUntil = DateTimeOffset.UtcNow.AddMilliseconds(350);
        if (window is not null)
        {
            RestoreScrollAnchor(viewModel?.RestoreCharacterOffset ?? window.StartCharacterOffset);
        }
    }

    private void RenderPagedDocument()
    {
        ReaderBox.Document = new FlowDocument();
        renderedSegments.Clear();
        var window = viewModel?.CurrentWindow;
        if (window is null || viewModel is null)
        {
            PagedReader.Document = CreateDocument(null, GetPagePadding(isPaged: true));
            UpdatePageControls();
            return;
        }

        var viewportWidth = Math.Max(320, PagedReader.ActualWidth - 20);
        var viewportHeight = Math.Max(320, PagedReader.ActualHeight - 20);
        var anchor = Math.Clamp(
            pendingPagedNavigation is null
                ? viewModel.CurrentCharacterOffset
                : pendingPagedAnchor,
            window.StartCharacterOffset,
            window.EndCharacterOffset);
        var contentStart = pendingPagedNavigation == ReaderWindowDirection.Previous
            ? window.StartCharacterOffset
            : anchor;
        var pagedWindow = SliceWindow(window, contentStart);
        var pagePadding = GetPagePadding(isPaged: true);
        var pageWidth = Math.Min(viewportWidth, viewModel.ReaderMaximumContentWidth);
        var document = CreateDocument(pagedWindow, pagePadding);
        document.PageWidth = pageWidth;
        document.PageHeight = viewportHeight;
        document.MinPageWidth = pageWidth;
        document.MaxPageWidth = pageWidth;
        document.MinPageHeight = viewportHeight;
        document.MaxPageHeight = viewportHeight;

        var dpi = VisualTreeHelper.GetDpi(PagedReader);
        var signature = new PagedLayoutSignature(
            viewModel.ContentLayoutVersion,
            pagedWindow.StartCharacterOffset,
            pagedWindow.Text.Length,
            RoundLayoutValue(viewportWidth),
            RoundLayoutValue(viewportHeight),
            RoundLayoutValue(dpi.DpiScaleX),
            RoundLayoutValue(dpi.DpiScaleY),
            viewModel.ReaderFontFamily,
            viewModel.ReaderFontSize,
            400,
            viewModel.ReaderFontSize * viewModel.ReaderLineHeight,
            viewModel.ReaderParagraphSpacing,
            RoundLayoutValue(pageWidth),
            pagePadding.Left,
            pagePadding.Top,
            pagePadding.Right,
            pagePadding.Bottom);
        pagedLayoutState.EnsureLayout(signature, anchor);

        PagedReader.Document = document;
        pagedPaginator = ((IDocumentPaginatorSource)document).DocumentPaginator
            as DynamicDocumentPaginator;
        if (pagedPaginator is null)
        {
            PageStatusText.Text = "分页不可用";
            PreviousPageButton.IsEnabled = false;
            NextPageButton.IsEnabled = false;
            return;
        }

        pagedPaginator.IsBackgroundPaginationEnabled = false;
        pagedPaginator.PageSize = new Size(pageWidth, viewportHeight);
        var desiredPageNumber = GetPageNumberForAnchor(anchor);
        if (pendingPagedNavigation == ReaderWindowDirection.Previous)
        {
            desiredPageNumber = Math.Max(0, desiredPageNumber - 1);
        }

        pendingPagedNavigation = null;
        displayedPagedPageNumber = desiredPageNumber;
        PagedReader.GoToPage(desiredPageNumber + 1);
        SchedulePagedAnchorUpdate();
    }

    private FlowDocument CreateDocument(TextReadWindow? window, Thickness pagePadding)
    {
        // RichTextBox propagates its Padding to the hosted document.
        ReaderBox.Padding = pagePadding;
        var fontSize = viewModel?.ReaderFontSize ?? 18;
        var fontFamily = viewModel?.ReaderFontFamily ?? "Microsoft YaHei UI";
        var document = new FlowDocument
        {
            PagePadding = pagePadding,
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily(fontFamily),
            FontSize = fontSize,
        };
        var backgroundKey = GetReaderBrushKey(isForeground: false);
        var foregroundKey = GetReaderBrushKey(isForeground: true);
        if (TryFindResource(backgroundKey) is Brush background)
        {
            document.Background = background;
            ReaderBox.Background = background;
            PagedReader.Background = background;
            ReaderEditor.Background = background;
        }

        if (TryFindResource(foregroundKey) is Brush foreground)
        {
            document.Foreground = foreground;
            ReaderEditor.Foreground = foreground;
        }

        var paragraph = new Paragraph
        {
            LineHeight = fontSize * (viewModel?.ReaderLineHeight ?? 1.75),
            Margin = new Thickness(0, 0, 0, viewModel?.ReaderParagraphSpacing ?? 0),
            TextAlignment = TextAlignment.Justify,
        };
        document.Blocks.Add(paragraph);

        if (window is null)
        {
            paragraph.Inlines.Add(new Run("正在准备正文…"));
        }
        else if (window.Text.Length == 0)
        {
            paragraph.Inlines.Add(new Run("这里已经是文档末尾。"));
        }
        else
        {
            AddTextRuns(paragraph, window);
            ApplyHighlights(window);
        }

        return document;
    }

    private void AddTextRuns(Paragraph paragraph, TextReadWindow window)
    {
        var text = window.Text;
        var segmentStart = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('\r' or '\n'))
            {
                continue;
            }

            AddRun(paragraph, window, segmentStart, index - segmentStart);
            var newlineLength = text[index] == '\r'
                && index + 1 < text.Length
                && text[index + 1] == '\n'
                    ? 2
                    : 1;
            var lineBreak = new LineBreak();
            paragraph.Inlines.Add(lineBreak);
            renderedSegments.Add(new RenderedTextSegment(
                window.StartCharacterOffset + index,
                newlineLength,
                lineBreak));
            index += newlineLength - 1;
            segmentStart = index + 1;
        }

        AddRun(paragraph, window, segmentStart, text.Length - segmentStart);
    }

    private void AddRun(Paragraph paragraph, TextReadWindow window, int localStart, int length)
    {
        if (length <= 0)
        {
            return;
        }

        var run = new Run(window.Text.Substring(localStart, length));
        paragraph.Inlines.Add(run);
        renderedSegments.Add(new RenderedTextSegment(
            window.StartCharacterOffset + localStart,
            length,
            run));
    }

    private void ApplyHighlights(TextReadWindow window)
    {
        if (viewModel is null || renderedSegments.Count == 0)
        {
            return;
        }

        foreach (var highlight in viewModel.Highlights)
        {
            var highlightEnd = highlight.StartOffset + highlight.Length;
            if (highlightEnd <= window.StartCharacterOffset
                || highlight.StartOffset >= window.EndCharacterOffset)
            {
                continue;
            }

            var brushKey = highlight.Style switch
            {
                HighlightStyle.Blue => "HighlightBlueBrush",
                HighlightStyle.Green => "HighlightGreenBrush",
                _ => "HighlightYellowBrush",
            };
            if (TryFindResource(brushKey) is not Brush brush)
            {
                continue;
            }

            foreach (var segment in renderedSegments.Where(item => item.Element is Run))
            {
                var overlapStart = Math.Max(highlight.StartOffset, segment.CharacterOffset);
                var overlapEnd = Math.Min(highlightEnd, segment.CharacterOffset + segment.Length);
                if (overlapStart >= overlapEnd)
                {
                    continue;
                }

                var range = new TextRange(
                    GetPointer(segment, overlapStart),
                    GetPointer(segment, overlapEnd));
                range.ApplyPropertyValue(TextElement.BackgroundProperty, brush);
            }
        }
    }

    private void OnSelectionContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        TextPointer? start;
        TextPointer? end;
        if (ReferenceEquals(sender, PagedReader))
        {
            start = PagedReader.Selection?.Start;
            end = PagedReader.Selection?.End;
        }
        else
        {
            start = ReaderBox.Selection.Start;
            end = ReaderBox.Selection.End;
        }

        activeSelection = CaptureSelection(start, end);
        if (activeSelection is null)
        {
            e.Handled = true;
        }
    }

    private void OnSelectionMouseUp(object sender, MouseButtonEventArgs e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (sender is not FrameworkElement element || element.ContextMenu is null)
                {
                    return;
                }

                TextPointer? start;
                TextPointer? end;
                if (ReferenceEquals(sender, PagedReader))
                {
                    start = PagedReader.Selection?.Start;
                    end = PagedReader.Selection?.End;
                }
                else
                {
                    start = ReaderBox.Selection.Start;
                    end = ReaderBox.Selection.End;
                }

                activeSelection = CaptureSelection(start, end);
                if (activeSelection is not null)
                {
                    element.ContextMenu.PlacementTarget = element;
                    element.ContextMenu.IsOpen = true;
                }
            });
    }

    private async void OnHighlightSelectionClick(object sender, RoutedEventArgs e)
    {
        if (viewModel is null
            || activeSelection is null
            || sender is not MenuItem { Tag: HighlightStyle style })
        {
            return;
        }

        await viewModel.AddHighlightAsync(activeSelection, style);
        activeSelection = null;
    }

    private void OnAddNoteSelectionClick(object sender, RoutedEventArgs e)
    {
        if (viewModel is null || activeSelection is null)
        {
            return;
        }

        viewModel.BeginSelectionNote(activeSelection);
        activeSelection = null;
    }

    private void OnCopySelectionClick(object sender, RoutedEventArgs e)
    {
        if (activeSelection is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(activeSelection.SelectedText);
        }
        catch (Exception exception)
        {
            if (viewModel is not null)
            {
                viewModel.WarningMessage = $"无法复制所选文字：{exception.Message}";
            }
        }

        activeSelection = null;
    }


    private ReaderTextSelection? CaptureSelection(TextPointer? start, TextPointer? end)
    {
        if (start is null
            || end is null
            || start.CompareTo(end) >= 0
            || viewModel?.CurrentWindow is not { } window)
        {
            return null;
        }

        var startOffset = GetCharacterOffset(start, window.StartCharacterOffset);
        var endOffset = GetCharacterOffset(end, startOffset);
        var length = endOffset - startOffset;
        var localStart = startOffset - window.StartCharacterOffset;
        if (length is <= 0 or > 16384
            || localStart < 0
            || localStart > window.Text.Length - length)
        {
            return null;
        }

        return new ReaderTextSelection(
            startOffset,
            (int)length,
            window.Text.Substring((int)localStart, (int)length));
    }

    private void RestoreScrollAnchor(long characterOffset)
    {
        if (viewModel?.CurrentWindow is { IsStartOfDocument: true } window
            && characterOffset <= window.StartCharacterOffset)
        {
            ReaderBox.ScrollToHome();
            viewModel.UpdateVisibleAnchor(characterOffset);
            return;
        }

        var segment = FindSegment(characterOffset);
        if (segment is null)
        {
            ReaderBox.ScrollToHome();
            viewModel?.UpdateVisibleAnchor(characterOffset);
            return;
        }

        var pointer = GetPointer(segment, characterOffset);
        ReaderBox.CaretPosition = pointer;
        segment.Element.BringIntoView();
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                var rectangle = pointer.GetCharacterRect(LogicalDirection.Forward);
                if (!rectangle.IsEmpty)
                {
                    ReaderBox.ScrollToVerticalOffset(
                        Math.Max(0, ReaderBox.VerticalOffset + rectangle.Top - ReaderBox.Padding.Top));
                }

                viewModel?.UpdateVisibleAnchor(characterOffset);
            });
    }

    private async void OnReaderScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (isRendering
            || viewModel?.IsEditMode == true
            || viewModel?.ReadingLayoutMode != ReaderLayoutMode.Scroll
            || viewModel.CurrentWindow is not { } window)
        {
            return;
        }

        var visibleOffset = GetFirstVisibleCharacterOffset(window.StartCharacterOffset);
        viewModel.UpdateVisibleAnchor(visibleOffset);
        if (isMovingWindow
            || DateTimeOffset.UtcNow < suppressEdgeLoadingUntil
            || Math.Abs(e.VerticalChange) < double.Epsilon)
        {
            return;
        }

        ReaderWindowDirection? direction = null;
        if (ReaderBox.VerticalOffset <= 220 && !window.IsStartOfDocument)
        {
            direction = ReaderWindowDirection.Previous;
        }
        else if (ReaderBox.VerticalOffset + ReaderBox.ViewportHeight
                 >= ReaderBox.ExtentHeight - 300
                 && !window.IsEndOfDocument)
        {
            direction = ReaderWindowDirection.Next;
        }

        if (direction is null)
        {
            return;
        }

        isMovingWindow = true;
        try
        {
            await viewModel.MoveWindowAsync(direction.Value, visibleOffset);
        }
        finally
        {
            isMovingWindow = false;
        }
    }

    private async void OnPreviousPageClick(object sender, RoutedEventArgs e)
    {
        await NavigatePagedAsync(ReaderWindowDirection.Previous);
    }

    private async void OnNextPageClick(object sender, RoutedEventArgs e)
    {
        await NavigatePagedAsync(ReaderWindowDirection.Next);
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (viewModel?.IsEditMode == true
            || viewModel?.ReadingLayoutMode != ReaderLayoutMode.Paged
            || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        var direction = e.Key switch
        {
            Key.Left or Key.PageUp => ReaderWindowDirection.Previous,
            Key.Right or Key.PageDown => ReaderWindowDirection.Next,
            _ => (ReaderWindowDirection?)null,
        };
        if (direction is null)
        {
            return;
        }

        e.Handled = true;
        await NavigatePagedAsync(direction.Value);
    }

    private async Task NavigatePagedAsync(ReaderWindowDirection direction)
    {
        if (isPagedNavigation
            || viewModel?.IsEditMode == true
            || viewModel?.ReadingLayoutMode != ReaderLayoutMode.Paged
            || viewModel.CurrentWindow is not { } window
            || pagedPaginator is null)
        {
            return;
        }

        isPagedNavigation = true;
        try
        {
            var currentPageNumber = GetDisplayedPageNumber();
            var currentAnchor = GetPageStartCharacterOffset(
                currentPageNumber,
                viewModel.CurrentCharacterOffset);
            if (direction == ReaderWindowDirection.Previous)
            {
                if (currentPageNumber > 0)
                {
                    displayedPagedPageNumber = currentPageNumber - 1;
                    PagedReader.GoToPage(displayedPagedPageNumber + 1);
                    SchedulePagedAnchorUpdate();
                    return;
                }

                if (window.IsStartOfDocument || currentAnchor <= 0)
                {
                    return;
                }

                pendingPagedNavigation = ReaderWindowDirection.Previous;
                pendingPagedAnchor = currentAnchor;
                await viewModel.MovePagedWindowAsync(direction, currentAnchor);
                return;
            }

            var nextPageNumber = currentPageNumber + 1;
            if (!TryGetPageStartCharacterOffset(nextPageNumber, out var nextAnchor))
            {
                if (!window.IsEndOfDocument)
                {
                    pendingPagedNavigation = ReaderWindowDirection.Next;
                    pendingPagedAnchor = window.EndCharacterOffset;
                    await viewModel.MovePagedWindowAsync(direction, pendingPagedAnchor);
                }

                return;
            }

            var nextPageIsTrailingWindowPage = !window.IsEndOfDocument
                && !TryGetPageStartCharacterOffset(nextPageNumber + 1, out _);
            if (nextPageIsTrailingWindowPage)
            {
                pendingPagedNavigation = ReaderWindowDirection.Next;
                pendingPagedAnchor = nextAnchor;
                await viewModel.MovePagedWindowAsync(direction, nextAnchor);
                return;
            }

            displayedPagedPageNumber = nextPageNumber;
            PagedReader.GoToPage(displayedPagedPageNumber + 1);
            SchedulePagedAnchorUpdate();
        }
        finally
        {
            isPagedNavigation = false;
        }
    }

    private void OnPagedPageViewsChanged(object sender, EventArgs e)
    {
        if (!isRendering)
        {
            SchedulePagedAnchorUpdate();
        }
    }

    private void OnPagedReaderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded
            || viewModel?.ReadingLayoutMode != ReaderLayoutMode.Paged
            || Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 0.5
                && Math.Abs(e.NewSize.Height - e.PreviousSize.Height) < 0.5)
        {
            return;
        }

        UpdatePagedAnchor();
        pagedReflowTimer.Stop();
        pagedReflowTimer.Start();
    }

    private void OnPagedReflowTimerTick(object? sender, EventArgs e)
    {
        pagedReflowTimer.Stop();
        ScheduleRender();
    }

    private void SchedulePagedAnchorUpdate()
    {
        if (pagedAnchorUpdateScheduled)
        {
            return;
        }

        pagedAnchorUpdateScheduled = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                pagedAnchorUpdateScheduled = false;
                UpdatePagedAnchor();
            });
    }

    private void UpdatePagedAnchor()
    {
        if (viewModel?.ReadingLayoutMode != ReaderLayoutMode.Paged || pagedPaginator is null)
        {
            return;
        }

        displayedPagedPageNumber = GetDisplayedPageNumber();
        var anchor = GetPageStartCharacterOffset(
            displayedPagedPageNumber,
            pagedLayoutState.ReflowAnchor);
        pagedLayoutState.SelectPage(displayedPagedPageNumber, anchor);
        viewModel.UpdateVisibleAnchor(anchor);
        UpdatePageControls();
    }

    private void UpdatePageControls()
    {
        if (viewModel?.CurrentWindow is not { } window || pagedPaginator is null)
        {
            PageStatusText.Text = "正在分页…";
            PreviousPageButton.IsEnabled = false;
            NextPageButton.IsEnabled = false;
            return;
        }

        var pageNumber = Math.Max(0, GetDisplayedPageNumber());
        PageStatusText.Text = $"本段第 {pageNumber + 1} 页";
        PreviousPageButton.IsEnabled = pageNumber > 0 || !window.IsStartOfDocument;
        NextPageButton.IsEnabled = !window.IsEndOfDocument
            || TryGetPageStartCharacterOffset(pageNumber + 1, out _);
    }

    private int GetDisplayedPageNumber()
    {
        var pageView = PagedReader.PageViews.FirstOrDefault();
        return pageView is null
            ? displayedPagedPageNumber
            : Math.Max(0, pageView.PageNumber);
    }

    private int GetPageNumberForAnchor(long characterOffset)
    {
        var segment = FindSegment(characterOffset);
        if (segment is null || pagedPaginator is null)
        {
            return 0;
        }

        return Math.Max(0, pagedPaginator.GetPageNumber(GetPointer(segment, characterOffset)));
    }

    private long GetPageStartCharacterOffset(int pageNumber, long fallback)
    {
        return TryGetPageStartCharacterOffset(pageNumber, out var characterOffset)
            ? characterOffset
            : fallback;
    }

    private bool TryGetPageStartCharacterOffset(int pageNumber, out long characterOffset)
    {
        characterOffset = 0;
        if (pageNumber < 0 || pagedPaginator is null)
        {
            return false;
        }

        if (pagedLayoutState.TryGetPageBoundary(pageNumber, out characterOffset))
        {
            return true;
        }

        var page = pagedPaginator.GetPage(pageNumber);
        if (ReferenceEquals(page, DocumentPage.Missing))
        {
            return false;
        }

        var position = pagedPaginator.GetPagePosition(page) as TextPointer;
        if (position is null)
        {
            return false;
        }

        characterOffset = GetCharacterOffset(position, pagedLayoutState.ReflowAnchor);
        pagedLayoutState.CachePageBoundary(pageNumber, characterOffset);
        return true;
    }

    private long GetFirstVisibleCharacterOffset(long fallback)
    {
        var point = new Point(Math.Min(76, Math.Max(8, ReaderBox.ActualWidth - 8)), 10);
        var pointer = ReaderBox.GetPositionFromPoint(point, snapToText: true);
        return pointer is null
            ? fallback
            : GetCharacterOffset(pointer, fallback);
    }

    private long GetCharacterOffset(TextPointer pointer, long fallback)
    {
        RenderedTextSegment? preceding = null;
        foreach (var segment in renderedSegments)
        {
            if (segment.Element.ContentStart.CompareTo(pointer) <= 0)
            {
                preceding = segment;
            }

            if (segment.Element.ContentStart.CompareTo(pointer) <= 0
                && segment.Element.ContentEnd.CompareTo(pointer) >= 0)
            {
                if (segment.Element is Run run)
                {
                    var characterCount = new TextRange(run.ContentStart, pointer).Text.Length;
                    return segment.CharacterOffset + Math.Clamp(characterCount, 0, segment.Length);
                }

                return segment.CharacterOffset;
            }
        }

        if (preceding is not null)
        {
            return preceding.CharacterOffset + preceding.Length;
        }

        return renderedSegments.Count > 0
            ? renderedSegments[0].CharacterOffset
            : fallback;
    }

    private RenderedTextSegment? FindSegment(long characterOffset)
    {
        for (var index = 0; index < renderedSegments.Count; index++)
        {
            var segment = renderedSegments[index];
            var isLastSegment = index == renderedSegments.Count - 1;
            if (characterOffset >= segment.CharacterOffset
                && (characterOffset < segment.CharacterOffset + segment.Length
                    || (isLastSegment
                        && characterOffset == segment.CharacterOffset + segment.Length)))
            {
                return segment;
            }
        }

        return renderedSegments.LastOrDefault();
    }

    private static TextPointer GetPointer(RenderedTextSegment segment, long characterOffset)
    {
        if (segment.Element is not Run run)
        {
            return segment.Element.ContentStart;
        }

        var localOffset = (int)Math.Clamp(
            characterOffset - segment.CharacterOffset,
            0,
            segment.Length);
        return run.ContentStart.GetPositionAtOffset(localOffset, LogicalDirection.Forward)
            ?? run.ContentEnd;
    }

    private static TextReadWindow SliceWindow(TextReadWindow window, long startCharacterOffset)
    {
        var localStart = (int)Math.Clamp(
            startCharacterOffset - window.StartCharacterOffset,
            0,
            window.Text.Length);
        return new TextReadWindow(
            window.StartCharacterOffset + localStart,
            window.Text[localStart..],
            window.IsStartOfDocument && localStart == 0,
            window.IsEndOfDocument);
    }

    private static double RoundLayoutValue(double value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private Thickness GetPagePadding(bool isPaged)
    {
        var horizontal = viewModel?.ReaderHorizontalMargin ?? 64;
        return isPaged
            ? new Thickness(horizontal, 46, horizontal, 88)
            : new Thickness(horizontal, 42, horizontal, 86);
    }

    private string GetReaderBrushKey(bool isForeground)
    {
        return (viewModel?.ReaderTheme ?? ReaderTheme.Sepia, isForeground) switch
        {
            (ReaderTheme.Light, true) => "ReaderLightForegroundBrush",
            (ReaderTheme.Light, false) => "ReaderLightBackgroundBrush",
            (ReaderTheme.Dark, true) => "ReaderDarkForegroundBrush",
            (ReaderTheme.Dark, false) => "ReaderDarkBackgroundBrush",
            (ReaderTheme.Sepia, true) => "ReaderSepiaForegroundBrush",
            _ => "ReaderSepiaBackgroundBrush",
        };
    }

    private void ApplyReaderChrome()
    {
        var theme = viewModel?.ReaderTheme ?? ReaderTheme.Sepia;
        var palette = new ResourceDictionary
        {
            Source = new Uri(
                theme == ReaderTheme.Dark
                    ? "/HNovaReader;component/Resources/Themes/DarkTheme.xaml"
                    : "/HNovaReader;component/Resources/Themes/LightTheme.xaml",
                UriKind.Relative),
        };
        palette["AppBackgroundBrush"] = palette[GetReaderBrushKey(isForeground: false)];
        palette["TextPrimaryBrush"] = palette[GetReaderBrushKey(isForeground: true)];
        if (theme == ReaderTheme.Sepia)
        {
            palette["SidebarBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xEA, 0xDF, 0xCA));
            palette["SurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0xF8, 0xF1, 0xE4));
            palette["SurfaceRaisedBrush"] = new SolidColorBrush(Color.FromRgb(0xF8, 0xF1, 0xE4));
            palette["SurfaceHoverBrush"] = new SolidColorBrush(Color.FromRgb(0xE5, 0xD8, 0xBF));
            palette["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0xD4, 0xC6, 0xAC));
            palette["BorderSubtleBrush"] = new SolidColorBrush(Color.FromRgb(0xE1, 0xD5, 0xBF));
            palette["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x6B, 0x5D, 0x48));
            palette["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x83, 0x73, 0x5B));
        }

        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(palette);
    }

    private void OnReaderSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (viewModel?.ReadingLayoutMode == ReaderLayoutMode.Scroll && e.WidthChanged)
        {
            viewModel.RestoreCharacterOffset = viewModel.CurrentCharacterOffset;
            ScheduleRender();
        }
    }

    private sealed record RenderedTextSegment(
        long CharacterOffset,
        int Length,
        Inline Element);
}
