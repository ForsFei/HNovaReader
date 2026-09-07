using HNovaReader.Features.Library;
using HNovaReader.Features.Settings;
using HNovaReader.Features.Reader;
using HNovaReader.Infrastructure.Navigation;
using HNovaReader.Services.Theming;
using HNovaReader.Tests.TestSupport;

namespace HNovaReader.Tests.Infrastructure;

public sealed class NavigationServiceTests
{
    [Fact]
    public async Task NavigateAndGoBack_UpdatesCurrentPageAndBackStack()
    {
        using var library = LibraryViewModelTestFactory.Create();
        var appSettings = new StubAppSettingsService();
        var settings = new SettingsViewModel(
            new StubThemeService(),
            appSettings,
            new StubSettingsInteractionService(),
            new StubLibraryOrganizationService(new StubLibraryService()),
            new StubAppPaths());
        var reader = new ReaderViewModel(
            new StubReaderEngineFactory(),
            new StubReadingPositionService(),
            new StubReaderAnnotationService(),
            appSettings,
            new StubTextFileEditService(),
            new StubLibraryService());
        var navigation = new NavigationService(library, settings, reader);

        await navigation.NavigateAsync(NavigationRoute.Library, CancellationToken.None);
        await navigation.NavigateAsync(NavigationRoute.Settings, CancellationToken.None);

        Assert.Same(settings, navigation.CurrentPage);
        Assert.True(navigation.CanGoBack);

        await navigation.GoBackAsync(CancellationToken.None);

        Assert.Same(library, navigation.CurrentPage);
        Assert.False(navigation.CanGoBack);
    }

    [Theory]
    [InlineData(NavigationRoute.Library, LibrarySection.All)]
    [InlineData(NavigationRoute.Recent, LibrarySection.Recent)]
    [InlineData(NavigationRoute.Reading, LibrarySection.Reading)]
    [InlineData(NavigationRoute.Unread, LibrarySection.Unread)]
    [InlineData(NavigationRoute.Finished, LibrarySection.Finished)]
    [InlineData(NavigationRoute.Favorites, LibrarySection.Favorites)]
    [InlineData(NavigationRoute.Series, LibrarySection.Series)]
    [InlineData(NavigationRoute.Tags, LibrarySection.Tags)]
    public async Task NavigateToLibraryCategory_AppliesRequestedSection(
        NavigationRoute route,
        LibrarySection expectedSection)
    {
        using var library = LibraryViewModelTestFactory.Create();
        var appSettings = new StubAppSettingsService();
        var settings = new SettingsViewModel(
            new StubThemeService(),
            appSettings,
            new StubSettingsInteractionService(),
            new StubLibraryOrganizationService(new StubLibraryService()),
            new StubAppPaths());
        using var reader = new ReaderViewModel(
            new StubReaderEngineFactory(),
            new StubReadingPositionService(),
            new StubReaderAnnotationService(),
            appSettings,
            new StubTextFileEditService(),
            new StubLibraryService());
        var navigation = new NavigationService(library, settings, reader);

        await navigation.NavigateAsync(route, TestContext.Current.CancellationToken);

        Assert.Same(library, navigation.CurrentPage);
        Assert.Equal(expectedSection, library.Section);
    }

    private sealed class StubThemeService : IThemeService
    {
        public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

        public AppTheme EffectiveTheme => CurrentTheme;

        public void Apply(AppTheme theme)
        {
            CurrentTheme = theme;
        }
    }
}
