using HNovaReader.Features.Settings;
using HNovaReader.Services.Theming;
using HNovaReader.Tests.TestSupport;

namespace HNovaReader.Tests.Features;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task ApplyThemeCommand_UpdatesServiceSelectedThemeAndSettings()
    {
        var themeService = new StubThemeService();
        var settingsService = new StubAppSettingsService();
        var viewModel = new SettingsViewModel(
            themeService,
            settingsService,
            new StubSettingsInteractionService(),
            new StubLibraryOrganizationService(new StubLibraryService()),
            new StubAppPaths());

        await viewModel.ApplyThemeCommand.ExecuteAsync(AppTheme.Dark);

        Assert.Equal(AppTheme.Dark, themeService.CurrentTheme);
        Assert.Equal(AppTheme.Dark, viewModel.SelectedTheme);
        Assert.Equal("深色", viewModel.SelectedThemeDisplay);
        Assert.Equal(AppTheme.Dark, settingsService.Current.Theme);
    }

    [Fact]
    public async Task ApplyThemeCommand_WhenPersistenceFails_ReportsInlineStatus()
    {
        var themeService = new StubThemeService();
        var settingsService = new StubAppSettingsService
        {
            SaveException = new IOException("磁盘只读"),
        };
        var viewModel = new SettingsViewModel(
            themeService,
            settingsService,
            new StubSettingsInteractionService(),
            new StubLibraryOrganizationService(new StubLibraryService()),
            new StubAppPaths());

        await viewModel.ApplyThemeCommand.ExecuteAsync(AppTheme.Light);

        Assert.Equal(AppTheme.Light, themeService.CurrentTheme);
        Assert.Contains("未能保存", viewModel.StatusMessage);
        Assert.Contains("磁盘只读", viewModel.StatusMessage);
    }

    [Fact]
    public void BrowseLibraryFolderCommand_UsesSelectedRoot()
    {
        var interaction = new StubSettingsInteractionService
        {
            SelectedLibraryFolder = @"D:\HNovaLibrary",
        };
        var viewModel = new SettingsViewModel(
            new StubThemeService(),
            new StubAppSettingsService(),
            interaction,
            new StubLibraryOrganizationService(new StubLibraryService()),
            new StubAppPaths())
        {
            LibraryFolder = @"C:\Books",
        };

        viewModel.BrowseLibraryFolderCommand.Execute(null);

        Assert.Equal(@"C:\Books", interaction.LastCurrentPath);
        Assert.Equal(@"D:\HNovaLibrary", viewModel.LibraryFolder);
        Assert.Contains("保存设置后生效", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SaveCommand_WhenRootChanges_ConfirmsAndMigratesBeforeReportingSuccess()
    {
        var oldRoot = Path.Combine(Path.GetTempPath(), "HNova-Old");
        var newRoot = Path.Combine(Path.GetTempPath(), "HNova-New");
        var settings = new StubAppSettingsService();
        settings.Set(settings.Current with { LibraryFolder = oldRoot });
        var library = new StubLibraryService();
        var organization = new StubLibraryOrganizationService(library);
        var interaction = new StubSettingsInteractionService { ConfirmMigrationResult = true };
        var viewModel = new SettingsViewModel(
            new StubThemeService(),
            settings,
            interaction,
            organization,
            new StubAppPaths())
        {
            LibraryFolder = newRoot,
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(Path.GetFullPath(newRoot), organization.LastMigrationTarget);
        Assert.Equal(newRoot, settings.Current.LibraryFolder);
        Assert.Equal("设置已保存", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SaveCommand_WhenRootMigrationIsDeclined_DoesNotSaveOrMove()
    {
        var oldRoot = Path.Combine(Path.GetTempPath(), "HNova-Old");
        var newRoot = Path.Combine(Path.GetTempPath(), "HNova-New");
        var settings = new StubAppSettingsService();
        settings.Set(settings.Current with { LibraryFolder = oldRoot });
        var organization = new StubLibraryOrganizationService(new StubLibraryService());
        var viewModel = new SettingsViewModel(
            new StubThemeService(),
            settings,
            new StubSettingsInteractionService { ConfirmMigrationResult = false },
            organization,
            new StubAppPaths())
        {
            LibraryFolder = newRoot,
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Null(organization.LastMigrationTarget);
        Assert.Equal(oldRoot, settings.Current.LibraryFolder);
        Assert.Contains("取消", viewModel.StatusMessage);
    }

    private sealed class StubThemeService : IThemeService
    {
        public AppTheme CurrentTheme { get; private set; } = AppTheme.System;

        public AppTheme EffectiveTheme => CurrentTheme;

        public void Apply(AppTheme theme)
        {
            CurrentTheme = theme;
        }
    }
}
