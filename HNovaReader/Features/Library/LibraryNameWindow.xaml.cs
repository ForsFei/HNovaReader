using System.Windows;
using System.Windows.Input;

namespace HNovaReader.Features.Library;

public partial class LibraryNameWindow : Window
{
    public LibraryNameWindow(string title, string label, string? initialValue)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        LabelText.Text = label;
        NameTextBox.Text = initialValue ?? string.Empty;
        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    public string? Result { get; private set; }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var value = NameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            ErrorText.Text = "请输入名称。";
            ErrorText.Visibility = Visibility.Visible;
            NameTextBox.Focus();
            return;
        }

        Result = value;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
    }
}
