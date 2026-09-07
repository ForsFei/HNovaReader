using System.Diagnostics;
using System.IO;
using System.Windows;
using HNovaReader.Services.Library;
using Microsoft.Win32;
using Microsoft.VisualBasic.FileIO;

namespace HNovaReader.Features.Library;

public sealed class WpfLibraryInteractionService : ILibraryInteractionService
{
    public IReadOnlyList<string> PickTextFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 TXT 书籍",
            Filter = "TXT 文本文件 (*.txt)|*.txt",
            CheckFileExists = true,
            Multiselect = true,
        };

        return dialog.ShowDialog(Application.Current.MainWindow) == true
            ? dialog.FileNames
            : Array.Empty<string>();
    }

    public string? PickReplacementTextFile(BookDetails book)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"重新定位《{book.Title}》",
            Filter = "TXT 文本文件 (*.txt)|*.txt",
            CheckFileExists = true,
            Multiselect = false,
            FileName = Path.GetFileName(book.FilePath),
        };

        return dialog.ShowDialog(Application.Current.MainWindow) == true
            ? dialog.FileName
            : null;
    }

    public string? PickCoverImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择书籍封面",
            Filter = "图片文件 (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp",
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog(Application.Current.MainWindow) == true
            ? dialog.FileName
            : null;
    }

    public BookEditorResult? EditBook(BookDetails book)
    {
        var viewModel = new BookEditorViewModel(book, PickCoverImage);
        var window = new BookEditorWindow(viewModel)
        {
            Owner = Application.Current.MainWindow,
        };

        return window.ShowDialog() == true ? window.Result : null;
    }

    public string? PromptForName(string title, string label, string? initialValue = null)
    {
        var window = new LibraryNameWindow(title, label, initialValue)
        {
            Owner = Application.Current.MainWindow,
        };

        return window.ShowDialog() == true ? window.Result : null;
    }

    public bool Confirm(string title, string message, bool isDangerous = false)
    {
        return MessageBox.Show(
                Application.Current.MainWindow,
                message,
                title,
                MessageBoxButton.YesNo,
                isDangerous ? MessageBoxImage.Warning : MessageBoxImage.Question,
                MessageBoxResult.No)
            == MessageBoxResult.Yes;
    }

    public void RevealFile(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("/select,");
        startInfo.ArgumentList.Add(fullPath);
        Process.Start(startInfo);
    }

    public void MoveFileToRecycleBin(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到要删除的 TXT 文件。", fullPath);
        }

        FileSystem.DeleteFile(
            fullPath,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin);
    }

    public void ShowMessage(string title, string message, bool isError = false)
    {
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            isError ? MessageBoxImage.Error : MessageBoxImage.Information);
    }
}
