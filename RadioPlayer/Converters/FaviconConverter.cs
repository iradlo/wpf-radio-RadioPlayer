using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace RadioPlayer.Converters;

/// <summary>
/// Converts a favicon URL string into a small decoded <see cref="BitmapImage"/>.
/// Returns null for missing/invalid URLs so the placeholder stays empty.
/// Downloads happen asynchronously and failures are swallowed by WPF.
/// </summary>
public sealed class FaviconConverter : IValueConverter
{
    /// <summary>Decode width in pixels; keeps memory usage low for large logos.</summary>
    private const int DecodePixelWidth = 32;

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.DecodePixelWidth = DecodePixelWidth;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
