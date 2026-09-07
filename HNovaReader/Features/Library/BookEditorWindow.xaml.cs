using System.Windows;

namespace HNovaReader.Features.Library;

public partial class BookEditorWindow : Window
{
    private readonly BookEditorViewModel _viewModel;

    public BookEditorWindow(BookEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public BookEditorResult? Result { get; private set; }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.TryCreateResult(out var result, out var error))
        {
            MessageBox.Show(
                this,
                error,
                "无法保存",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
