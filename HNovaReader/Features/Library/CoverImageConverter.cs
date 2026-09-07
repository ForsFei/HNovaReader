using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace HNovaReader.Features.Library;

public sealed class CoverImageConverter : IValueConverter
{
    private readonly Dictionary<string, WeakReference<BitmapSource>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var cacheKey = $"{Path.GetFullPath(path)}|{File.GetLastWriteTimeUtc(path).Ticks}";
        if (_cache.TryGetValue(cacheKey, out var weakReference)
            && weakReference.TryGetTarget(out var cached))
        {
            return cached;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = 240;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            _cache[cacheKey] = new WeakReference<BitmapSource>(image);
            return image;
        }
        catch (Exception exception) when (
            exception is IOException
            or NotSupportedException
            or UriFormatException)
        {
            return null;
        }
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
