using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HNovaReader.Features.Library;
using HNovaReader.Features.Shell;
using HNovaReader.Services.Settings;

namespace HNovaReader;

public partial class MainWindow : Window
{
    private const string SeriesDragDataFormat = "HNovaReader.LibrarySeriesId";
    private readonly ShellViewModel _viewModel;
    private readonly IAppSettingsService _settingsService;
    private bool _shutdownStarted;
    private bool _shutdownCompleted;
    private Point _treeDragStart;
    private LibraryTreeNodeViewModel? _treeDragSeries;

    public MainWindow(ShellViewModel viewModel, IAppSettingsService settingsService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsService = settingsService;
        DataContext = viewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.Current;
        if (!settings.RememberWindowSize)
        {
            return;
        }

        if (settings.WindowWidth is { } width)
        {
            Width = width;
        }

        if (settings.WindowHeight is { } height)
        {
            Height = height;
        }
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(LibraryView.BookDragDataFormat)
            || e.Data.GetDataPresent(SeriesDragDataFormat))
        {
            e.Effects = DragDropEffects.None;
            // Keep the routed event alive so the LibraryTree child can accept
            // internal book/series drops. The window itself is not a target.
            e.Handled = false;
            return;
        }

        e.Effects = CanImportDrop(e.Data)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnLibraryTreeLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _treeDragStart = e.GetPosition(this);
        _treeDragSeries = FindTreeViewItem(e.OriginalSource as DependencyObject)?.DataContext
            is LibraryTreeNodeViewModel { Kind: LibraryTreeNodeKind.Series } series
            ? series
            : null;
    }

    private void OnLibraryTreeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _treeDragSeries is null)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _treeDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _treeDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var series = _treeDragSeries;
        _treeDragSeries = null;
        var data = new DataObject();
        data.SetData(SeriesDragDataFormat, series.SeriesId!.Value);
        DragDrop.DoDragDrop(LibraryTree, data, DragDropEffects.Move);
    }

    private async void OnLibraryTreeSelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is LibraryTreeNodeViewModel node)
        {
            await _viewModel.SelectLibraryNodeAsync(node);
        }
    }

    private void OnLibraryTreeRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindTreeViewItem(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
        }
    }

    private void OnLibraryTreeDragOver(object sender, DragEventArgs e)
    {
        var target = GetTreeDropTarget(e.OriginalSource as DependencyObject);
        if (target is null)
        {
            e.Effects = DragDropEffects.None;
        }
        else if (e.Data.GetDataPresent(LibraryView.BookDragDataFormat))
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (e.Data.GetDataPresent(SeriesDragDataFormat))
        {
            e.Effects = target.Kind == LibraryTreeNodeKind.Folder
                ? DragDropEffects.Move
                : DragDropEffects.None;
        }
        else
        {
            e.Effects = CanImportDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        e.Handled = true;
    }

    private async void OnLibraryTreeDrop(object sender, DragEventArgs e)
    {
        var target = GetTreeDropTarget(e.OriginalSource as DependencyObject);
        if (target is null)
        {
            return;
        }

        e.Handled = true;
        if (e.Data.GetDataPresent(LibraryView.BookDragDataFormat))
        {
            await _viewModel.MoveSelectedBooksAsync(target);
            return;
        }

        if (target.Kind == LibraryTreeNodeKind.Folder
            && e.Data.GetDataPresent(SeriesDragDataFormat)
            && e.Data.GetData(SeriesDragDataFormat) is Guid seriesId)
        {
            await _viewModel.MoveSeriesAsync(seriesId, target);
            return;
        }

        if (CanImportDrop(e.Data)
            && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            await _viewModel.ImportPathsAsync(paths, target);
        }
    }

    private LibraryTreeNodeViewModel? GetTreeDropTarget(DependencyObject? source)
    {
        if (source is null)
        {
            return null;
        }

        return FindTreeViewItem(source)?.DataContext as LibraryTreeNodeViewModel;
    }

    private static TreeViewItem? FindTreeViewItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TreeViewItem item)
            {
                return item;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!CanImportDrop(e.Data)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        e.Handled = true;
        await _viewModel.ImportPathsAsync(paths);
    }

    private bool CanImportDrop(IDataObject data)
    {
        if (_viewModel.IsImporting
            || !data.GetDataPresent(DataFormats.FileDrop)
            || data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return false;
        }

        return paths.Any(path =>
            Directory.Exists(path)
            || string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase));
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        try
        {
            await _viewModel.ShutdownAsync(CancellationToken.None);
            var settings = _settingsService.Current;
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;
            await _settingsService.SaveAsync(
                settings with
                {
                    WindowWidth = settings.RememberWindowSize ? bounds.Width : null,
                    WindowHeight = settings.RememberWindowSize ? bounds.Height : null,
                },
                CancellationToken.None);
        }
        finally
        {
            _shutdownCompleted = true;
            Close();
        }
    }
}
