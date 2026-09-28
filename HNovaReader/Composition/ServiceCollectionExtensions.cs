using HNovaReader.Data;
using HNovaReader.Features.Library;
using HNovaReader.Features.Reader;
using HNovaReader.Features.Settings;
using HNovaReader.Features.Shell;
using HNovaReader.Infrastructure.Navigation;
using HNovaReader.Infrastructure.Persistence;
using HNovaReader.Infrastructure.Storage;
using HNovaReader.Services.Import;
using HNovaReader.Services.Library;
using HNovaReader.Services.Reader;
using HNovaReader.Services.Settings;
using HNovaReader.Services.Theming;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HNovaReader.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHNovaReader(this IServiceCollection services)
    {
        services.AddSingleton<IAppPaths, AppPaths>();

        services.AddDbContextFactory<HNovaReaderDbContext>((serviceProvider, options) =>
        {
            var appPaths = serviceProvider.GetRequiredService<IAppPaths>();
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = appPaths.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
            }.ToString();

            options.UseSqlite(connectionString);
        });

        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAppSettingsService, JsonAppSettingsService>();
        services.AddSingleton<IFilePathNormalizer, FilePathNormalizer>();
        services.AddSingleton<ILibraryBackupService, LibraryBackupService>();
        services.AddSingleton<ILibraryOrganizationService, LibraryOrganizationService>();
        services.AddSingleton<ILibraryService, LibraryService>();
        services.AddSingleton<ITextFileScanner, TextFileScanner>();
        services.AddSingleton<ITextEncodingDetector, TextEncodingDetector>();
        services.AddSingleton<IManagedBookFileStore, ManagedBookFileStore>();
        services.AddSingleton<ITextImportService, TextImportService>();
        services.AddSingleton<ILibrarySyncService, LibrarySyncService>();
        services.AddSingleton<IChapterDetector, ChapterDetector>();
        services.AddSingleton<ITextIndexStore, EfTextIndexStore>();
        services.AddSingleton<IReadingPositionService, EfReadingPositionService>();
        services.AddSingleton<IReaderAnnotationService, EfReaderAnnotationService>();
        services.AddSingleton<ITextFileEditService, StreamingTextFileEditService>();
        services.AddSingleton<IReaderEngine, TxtReaderEngine>();
        services.AddSingleton<IReaderEngineFactory, ReaderEngineFactory>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ICoverFileStore, CoverFileStore>();
        services.AddSingleton<ILibraryInteractionService, WpfLibraryInteractionService>();
        services.AddSingleton<ISettingsInteractionService, WpfSettingsInteractionService>();

        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ReaderViewModel>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ShellViewModel>();

        services.AddSingleton<MainWindow>();

        return services;
    }
}
