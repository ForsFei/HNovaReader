using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HNovaReader.Features.Library;

public partial class LibraryView : UserControl
{
    public const string BookDragDataFormat = "HNovaReader.LibraryBookIds";

    private Point _dragStart;
    private LibraryBookItemViewModel? _dragItem;

    public LibraryView()
    {
        InitializeComponent();
    }

    private void OnMoreClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void OnBookDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView { SelectedItem: LibraryBookItemViewModel book }
            && book.OpenCommand.CanExecute(null))
        {
            book.OpenCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnBookPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        var item = FindBookItem(e.OriginalSource as DependencyObject);
        if (item is null)
        {
            return;
        }

        _dragStart = e.GetPosition(this);
        _dragItem = item;
        var modifiers = Keyboard.Modifiers;
        viewModel.SelectBook(
            item,
            modifiers.HasFlag(ModifierKeys.Control),
            modifiers.HasFlag(ModifierKeys.Shift));
    }

    private void OnBookPointerMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || _dragItem is null
            || DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _dragItem;
        _dragItem = null;
        var data = new DataObject();
        data.SetData(BookDragDataFormat, viewModel.GetSelectedBookIdsForDrag(item).ToArray());
        DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
    }

    private static LibraryBookItemViewModel? FindBookItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: LibraryBookItemViewModel item })
            {
                return item;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
