using System.IO;
using System.Windows;
using HNovaReader.Infrastructure.Storage;
using Microsoft.Win32;

namespace HNovaReader.Features.Settings;

public sealed class WpfSettingsInteractionService(IAppPaths appPaths)
    : ISettingsInteractionService
{
    public string? PickLibraryFolder(string? currentPath)
    {
        var initialDirectory = string.IsNullOrWhiteSpace(currentPath)
            ? appPaths.ManagedLibraryDirectory
            : Path.GetFullPath(currentPath);
        var dialog = new OpenFolderDialog
        {
            Title = "选择书库存书根目录",
            InitialDirectory = Directory.Exists(initialDirectory)
                ? initialDirectory
                : appPaths.ManagedLibraryDirectory,
            Multiselect = false,
        };

        return dialog.ShowDialog(Application.Current.MainWindow) == true
            ? dialog.FolderName
            : null;
    }

    public bool ConfirmLibraryMigration(string sourcePath, string targetPath)
    {
        return MessageBox.Show(
                Application.Current.MainWindow,
                $"要把管理型书库迁移到新目录吗？\n\n当前：{sourcePath}\n新位置：{targetPath}\n\n迁移前会在新书库根目录创建隐藏备份；任何一步失败都会停止迁移。",
                "迁移书库根目录",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No)
            == MessageBoxResult.Yes;
    }
}
