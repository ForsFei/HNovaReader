using System.Windows;
using Microsoft.Win32;

namespace HNovaReader.Services.Theming;

public enum AppTheme
{
    Dark,
    Light,
    System,
}

public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    AppTheme EffectiveTheme { get; }

    void Apply(AppTheme theme);
}

public sealed class ThemeService : IThemeService
{
    private const string ThemeSourcePrefix = "Resources/Themes/";

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public AppTheme EffectiveTheme { get; private set; } = AppTheme.Dark;

    public void Apply(AppTheme theme)
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("Application resources are not available.");

        var effectiveTheme = theme == AppTheme.System ? ReadSystemTheme() : theme;
        var source = new Uri(
            $"/{typeof(ThemeService).Assembly.GetName().Name};component/{ThemeSourcePrefix}{effectiveTheme}Theme.xaml",
            UriKind.Relative);

        var dictionaries = application.Resources.MergedDictionaries;
        var existingIndex = FindThemeDictionaryIndex(dictionaries);
        var themeDictionary = new ResourceDictionary { Source = source };

        if (existingIndex >= 0)
        {
            dictionaries[existingIndex] = themeDictionary;
        }
        else
        {
            dictionaries.Insert(0, themeDictionary);
        }

        CurrentTheme = theme;
        EffectiveTheme = effectiveTheme;
    }

    private static int FindThemeDictionaryIndex(IList<ResourceDictionary> dictionaries)
    {
        for (var index = 0; index < dictionaries.Count; index++)
        {
            var source = dictionaries[index].Source?.OriginalString;
            if (source?.Contains(ThemeSourcePrefix, StringComparison.OrdinalIgnoreCase) == true)
            {
                return index;
            }
        }

        return -1;
    }

    private static AppTheme ReadSystemTheme()
    {
        const string personalizeKey =
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        var value = Registry.GetValue(personalizeKey, "AppsUseLightTheme", 0);
        return value is int and not 0 ? AppTheme.Light : AppTheme.Dark;
    }
}
