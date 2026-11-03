using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
namespace Cinevo;
public sealed class CoverImageConverter : IValueConverter
{
    private readonly Dictionary<string, WeakReference<BitmapSource>> cache = new(StringComparer.OrdinalIgnoreCase);
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length == 0 || !File.Exists(path)) return null;
        if (cache.TryGetValue(path, out var weak) && weak.TryGetTarget(out var loaded)) return loaded;
        try { using var file = File.OpenRead(path); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 360; bitmap.StreamSource = file; bitmap.EndInit(); bitmap.Freeze(); cache[path] = new(bitmap); if (cache.Count > 1000) foreach (var key in cache.Where(p => !p.Value.TryGetTarget(out _)).Select(p => p.Key).ToArray()) cache.Remove(key); return bitmap; }
        catch (Exception ex) when (ex is IOException or NotSupportedException or System.IO.FileFormatException or ArgumentException) { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
public record MovieRow(int Columns, List<Movie> Movies);
