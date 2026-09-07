using System.Text;
using System.Windows;
using HNovaReader.Composition;
using HNovaReader.Features.Library;
using HNovaReader.Infrastructure.Navigation;
using HNovaReader.Infrastructure.Persistence;
using HNovaReader.Infrastructure.Storage;
using HNovaReader.Services.Theming;
using HNovaReader.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace HNovaReader;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var services = new ServiceCollection();
            services.AddHNovaReader();

            _serviceProvider = services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true,
                });
            var appPaths = _serviceProvider.GetRequiredService<IAppPaths>();
            appPaths.EnsureDirectories();

            var databaseInitializer = _serviceProvider.GetRequiredService<IDatabaseInitializer>();
            await databaseInitializer.InitializeAsync(CancellationToken.None);

            var settingsService = _serviceProvider.GetRequiredService<IAppSettingsService>();
            var settings = await settingsService.LoadAsync(CancellationToken.None);
            var themeService = _serviceProvider.GetRequiredService<IThemeService>();
            themeService.Apply(settings.Theme);

            var libraryViewModel = _serviceProvider.GetRequiredService<LibraryViewModel>();
            await libraryViewModel.InitializeAsync(CancellationToken.None);

            var navigationService = _serviceProvider.GetRequiredService<INavigationService>();
            await navigationService.NavigateAsync(NavigationRoute.Library, CancellationToken.None);

            var window = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"HNovaReader 无法启动。\n\n{exception.Message}",
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

}
