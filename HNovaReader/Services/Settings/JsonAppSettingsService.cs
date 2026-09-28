using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HNovaReader.Infrastructure.Storage;

namespace HNovaReader.Services.Settings;

public sealed class JsonAppSettingsService(
    IAppPaths appPaths,
    TimeProvider timeProvider) : IAppSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private readonly SemaphoreSlim gate = new(1, 1);

    public AppSettings Current { get; private set; } = new();

    public event EventHandler? SettingsChanged;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(appPaths.SettingsPath))
            {
                Current = new AppSettings();
                return Current;
            }

            try
            {
                await using var stream = new FileStream(
                    appPaths.SettingsPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(
                    stream,
                    SerializerOptions,
                    cancellationToken);
                Current = Normalize(loaded ?? throw new JsonException("设置文件内容为空。"));
            }
            catch (Exception exception) when (exception is JsonException
                or NotSupportedException
                or ArgumentException
                or PathTooLongException)
            {
                PreserveCorruptSettingsFile();
                Current = new AppSettings();
            }

            return Current;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = Normalize(settings);
        await gate.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        try
        {
            appPaths.EnsureDirectories();
            temporaryPath = $"{appPaths.SettingsPath}.{Guid.NewGuid():N}.tmp";
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    normalized,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(appPaths.SettingsPath))
            {
                File.Replace(temporaryPath, appPaths.SettingsPath, null);
            }
            else
            {
                File.Move(temporaryPath, appPaths.SettingsPath);
            }

            temporaryPath = null;
            Current = normalized;
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            gate.Release();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PreserveCorruptSettingsFile()
    {
        var suffix = timeProvider.GetUtcNow().ToString("yyyyMMddHHmmssfff");
        var corruptPath = $"{appPaths.SettingsPath}.corrupt-{suffix}";
        for (var attempt = 1; File.Exists(corruptPath); attempt++)
        {
            corruptPath = $"{appPaths.SettingsPath}.corrupt-{suffix}-{attempt}";
        }

        File.Move(appPaths.SettingsPath, corruptPath);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var defaults = new AppSettings();
        var fontFamily = string.IsNullOrWhiteSpace(settings.FontFamily)
            ? defaults.FontFamily
            : settings.FontFamily.Trim();
        var encoding = string.IsNullOrWhiteSpace(settings.DefaultTextEncoding)
            ? defaults.DefaultTextEncoding
            : settings.DefaultTextEncoding.Trim();
        var libraryFolder = string.IsNullOrWhiteSpace(settings.LibraryFolder)
            ? null
            : Path.GetFullPath(settings.LibraryFolder.Trim());

        return settings with
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            Theme = Enum.IsDefined(settings.Theme) ? settings.Theme : defaults.Theme,
            ReaderTheme = Enum.IsDefined(settings.ReaderTheme)
                ? settings.ReaderTheme
                : defaults.ReaderTheme,
            FontFamily = fontFamily,
            FontSize = ClampFinite(settings.FontSize, 14, 30, defaults.FontSize),
            LineHeight = ClampFinite(settings.LineHeight, 1.2, 2.4, defaults.LineHeight),
            ParagraphSpacing = ClampFinite(settings.ParagraphSpacing, 0, 36, defaults.ParagraphSpacing),
            MaximumContentWidth = ClampFinite(
                settings.MaximumContentWidth,
                560,
                1600,
                defaults.MaximumContentWidth),
            HorizontalMargin = ClampFinite(
                settings.HorizontalMargin,
                24,
                160,
                defaults.HorizontalMargin),
            ReadingMode = Enum.IsDefined(settings.ReadingMode)
                ? settings.ReadingMode
                : defaults.ReadingMode,
            LibraryFolder = libraryFolder,
            LibraryUpdateIntervalMinutes = Math.Clamp(settings.LibraryUpdateIntervalMinutes, 1, 1440),
            ImportBehavior = Enum.IsDefined(settings.ImportBehavior)
                ? settings.ImportBehavior
                : defaults.ImportBehavior,
            DefaultTextEncoding = encoding,
            WindowWidth = NormalizeWindowDimension(settings.WindowWidth, 1080, 7680),
            WindowHeight = NormalizeWindowDimension(settings.WindowHeight, 680, 4320),
        };
    }

    private static double ClampFinite(double value, double minimum, double maximum, double fallback)
    {
        return double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
    }

    private static double? NormalizeWindowDimension(double? value, double minimum, double maximum)
    {
        return value is { } dimension && double.IsFinite(dimension)
            ? Math.Clamp(dimension, minimum, maximum)
            : null;
    }
}
