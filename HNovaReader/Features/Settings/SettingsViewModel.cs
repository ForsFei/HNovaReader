using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HNovaReader.Data.Entities;
using HNovaReader.Features.Shared;
using HNovaReader.Infrastructure.Storage;
using HNovaReader.Services.Library;
using HNovaReader.Services.Settings;
using HNovaReader.Services.Theming;

namespace HNovaReader.Features.Settings;

public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly IThemeService themeService;
    private readonly IAppSettingsService settingsService;
    private readonly ISettingsInteractionService interactionService;
    private readonly ILibraryOrganizationService organizationService;
    private readonly IAppPaths appPaths;

    public SettingsViewModel(
        IThemeService themeService,
        IAppSettingsService settingsService,
        ISettingsInteractionService interactionService,
        ILibraryOrganizationService organizationService,
        IAppPaths appPaths)
        : base("设置", "调整应用外观、阅读排版与本地导入偏好")
    {
        this.themeService = themeService;
        this.settingsService = settingsService;
        this.interactionService = interactionService;
        this.organizationService = organizationService;
        this.appPaths = appPaths;
        var settings = settingsService.Current;
        selectedTheme = settings.Theme;
        selectedReaderTheme = settings.ReaderTheme;
        readerFontFamily = settings.FontFamily;
        readerFontSize = settings.FontSize;
        readerLineHeight = settings.LineHeight;
        readerParagraphSpacing = settings.ParagraphSpacing;
        readerMaximumContentWidth = settings.MaximumContentWidth;
        readerHorizontalMargin = settings.HorizontalMargin;
        selectedReadingMode = settings.ReadingMode;
        libraryFolder = settings.LibraryFolder ?? string.Empty;
        selectedImportBehavior = settings.ImportBehavior;
        rememberWindowSize = settings.RememberWindowSize;
        defaultTextEncoding = settings.DefaultTextEncoding;
        statusMessage = "设置已载入";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedThemeDisplay))]
    private AppTheme selectedTheme;

    [ObservableProperty]
    private ReaderTheme selectedReaderTheme;

    [ObservableProperty]
    private string readerFontFamily;

    [ObservableProperty]
    private double readerFontSize;

    [ObservableProperty]
    private double readerLineHeight;

    [ObservableProperty]
    private double readerParagraphSpacing;

    [ObservableProperty]
    private double readerMaximumContentWidth;

    [ObservableProperty]
    private double readerHorizontalMargin;

    [ObservableProperty]
    private ReadingModePreference selectedReadingMode;

    [ObservableProperty]
    private string libraryFolder;

    [ObservableProperty]
    private BookStorageMode selectedImportBehavior;

    [ObservableProperty]
    private bool rememberWindowSize;

    [ObservableProperty]
    private string defaultTextEncoding;

    [ObservableProperty]
    private string statusMessage;

    public string SelectedThemeDisplay => SelectedTheme switch
    {
        AppTheme.Dark => "深色",
        AppTheme.Light => "浅色",
        AppTheme.System => "跟随系统",
        _ => SelectedTheme.ToString(),
    };

    [RelayCommand]
    private async Task ApplyThemeAsync(AppTheme theme)
    {
        try
        {
            themeService.Apply(theme);
            SelectedTheme = theme;
            await settingsService.SaveAsync(
                settingsService.Current with { Theme = theme },
                CancellationToken.None);
            StatusMessage = "主题已保存";
        }
        catch (Exception exception)
        {
            StatusMessage = $"主题已应用，但未能保存：{exception.Message}";
        }
    }

    [RelayCommand]
    private void BrowseLibraryFolder()
    {
        var selectedFolder = interactionService.PickLibraryFolder(LibraryFolder);
        if (!string.IsNullOrWhiteSpace(selectedFolder))
        {
            LibraryFolder = selectedFolder;
            StatusMessage = "已选择书库存书根目录，保存设置后生效";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _ = Encoding.GetEncoding(DefaultTextEncoding.Trim());
            var previous = settingsService.Current;
            var sourceRoot = ResolveLibraryRoot(previous.LibraryFolder);
            var targetRoot = ResolveLibraryRoot(LibraryFolder);
            var rootChanged = !sourceRoot.Equals(targetRoot, StringComparison.OrdinalIgnoreCase);
            if (rootChanged && !interactionService.ConfirmLibraryMigration(sourceRoot, targetRoot))
            {
                StatusMessage = "已取消书库迁移，其他设置尚未保存";
                return;
            }

            var updated = CreateSettings(previous);
            await settingsService.SaveAsync(updated, CancellationToken.None);
            try
            {
                if (rootChanged)
                {
                    await organizationService.MigrateRootAsync(targetRoot, CancellationToken.None);
                }
            }
            catch
            {
                await settingsService.SaveAsync(previous, CancellationToken.None);
                throw;
            }

            StatusMessage = "设置已保存";
        }
        catch (Exception exception)
        {
            StatusMessage = $"设置未保存：{exception.Message}";
        }
    }

    private AppSettings CreateSettings(AppSettings current)
    {
        return current with
        {
            Theme = SelectedTheme,
            ReaderTheme = SelectedReaderTheme,
            FontFamily = ReaderFontFamily,
            FontSize = ReaderFontSize,
            LineHeight = ReaderLineHeight,
            ParagraphSpacing = ReaderParagraphSpacing,
            MaximumContentWidth = ReaderMaximumContentWidth,
            HorizontalMargin = ReaderHorizontalMargin,
            ReadingMode = SelectedReadingMode,
            LibraryFolder = LibraryFolder,
            ImportBehavior = SelectedImportBehavior,
            RememberWindowSize = RememberWindowSize,
            DefaultTextEncoding = DefaultTextEncoding,
        };
    }

    private string ResolveLibraryRoot(string? value)
    {
        return Path.GetFullPath(string.IsNullOrWhiteSpace(value)
            ? appPaths.ManagedLibraryDirectory
            : value.Trim());
    }
}
