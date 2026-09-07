using HNovaReader.Features.Library;
using HNovaReader.Features.Reader;
using HNovaReader.Features.Settings;
using HNovaReader.Features.Shared;
using HNovaReader.Services.Library;

namespace HNovaReader.Infrastructure.Navigation;

public enum NavigationRoute
{
    Library,
    Recent,
    Reading,
    Unread,
    Finished,
    Favorites,
    Series,
    Tags,
    Settings,
    Reader,
}

public interface INavigationPageLifecycle
{
    Task OnNavigatedFromAsync(CancellationToken cancellationToken);
}

public interface INavigationService
{
    PageViewModel? CurrentPage { get; }

    bool CanGoBack { get; }

    event EventHandler? CurrentPageChanged;

    Task NavigateAsync(NavigationRoute route, CancellationToken cancellationToken);

    Task GoBackAsync(CancellationToken cancellationToken);

    Task OpenReaderAsync(BookDetails book, CancellationToken cancellationToken);

    Task ShutdownAsync(CancellationToken cancellationToken);
}

public sealed class NavigationService : INavigationService
{
    private readonly IReadOnlyDictionary<NavigationRoute, PageViewModel> _pages;
    private readonly Stack<NavigationRoute> _backStack = new();
    private NavigationRoute? _currentRoute;

    public NavigationService(
        LibraryViewModel libraryViewModel,
        SettingsViewModel settingsViewModel,
        ReaderViewModel readerViewModel)
    {
        _pages = new Dictionary<NavigationRoute, PageViewModel>
        {
            [NavigationRoute.Library] = libraryViewModel,
            [NavigationRoute.Recent] = libraryViewModel,
            [NavigationRoute.Reading] = libraryViewModel,
            [NavigationRoute.Unread] = libraryViewModel,
            [NavigationRoute.Finished] = libraryViewModel,
            [NavigationRoute.Favorites] = libraryViewModel,
            [NavigationRoute.Series] = libraryViewModel,
            [NavigationRoute.Tags] = libraryViewModel,
            [NavigationRoute.Settings] = settingsViewModel,
            [NavigationRoute.Reader] = readerViewModel,
        };
    }

    public PageViewModel? CurrentPage { get; private set; }

    public bool CanGoBack => _backStack.Count > 0;

    public event EventHandler? CurrentPageChanged;

    public async Task NavigateAsync(NavigationRoute route, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_currentRoute == route)
        {
            return;
        }

        await LeaveCurrentPageAsync(cancellationToken);
        if (_currentRoute is { } currentRoute)
        {
            _backStack.Push(currentRoute);
        }

        SetCurrentRoute(route);
    }

    public async Task GoBackAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_backStack.TryPeek(out var route))
        {
            await LeaveCurrentPageAsync(cancellationToken);
            _backStack.Pop();
            SetCurrentRoute(route);
        }
    }

    public async Task OpenReaderAsync(BookDetails book, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        var reader = (ReaderViewModel)_pages[NavigationRoute.Reader];
        try
        {
            await reader.OpenBookAsync(book, cancellationToken);
            await NavigateAsync(NavigationRoute.Reader, cancellationToken);
        }
        catch
        {
            if (_currentRoute != NavigationRoute.Reader)
            {
                await reader.OnNavigatedFromAsync(CancellationToken.None);
            }

            throw;
        }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        return LeaveCurrentPageAsync(cancellationToken);
    }

    private void SetCurrentRoute(NavigationRoute route)
    {
        _currentRoute = route;
        CurrentPage = _pages[route];
        if (CurrentPage is LibraryViewModel library)
        {
            library.SetSection(route switch
            {
                NavigationRoute.Recent => LibrarySection.Recent,
                NavigationRoute.Reading => LibrarySection.Reading,
                NavigationRoute.Unread => LibrarySection.Unread,
                NavigationRoute.Finished => LibrarySection.Finished,
                NavigationRoute.Favorites => LibrarySection.Favorites,
                NavigationRoute.Series => LibrarySection.Series,
                NavigationRoute.Tags => LibrarySection.Tags,
                _ => LibrarySection.All,
            });
        }

        CurrentPageChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task LeaveCurrentPageAsync(CancellationToken cancellationToken)
    {
        return CurrentPage is INavigationPageLifecycle lifecycle
            ? lifecycle.OnNavigatedFromAsync(cancellationToken)
            : Task.CompletedTask;
    }
}
