using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace IptvPlayer;

/// <summary>URL → BitmapImage décodée en petite taille (rapide, peu de mémoire). À utiliser avec IsAsync=True.</summary>
public sealed class ImageUrlConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string url || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (Cache.TryGetValue(url, out var hit)) return hit;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.DecodePixelWidth = 96;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.EndInit();
            bmp.Freeze();
            if (Cache.Count > 3000) Cache.Clear();
            Cache[url] = bmp;
            return bmp;
        }
        catch { return null; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
