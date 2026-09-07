namespace HNovaReader.Services.Settings;

public interface IAppSettingsService
{
    AppSettings Current { get; }

    event EventHandler? SettingsChanged;

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
