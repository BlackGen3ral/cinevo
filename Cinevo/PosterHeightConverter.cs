using System.Globalization;
using System.Windows.Data;
namespace Cinevo;
public sealed class PosterHeightConverter : IValueConverter
{
    public object Convert(object value, Type type, object parameter, CultureInfo culture) => value is double width ? Math.Clamp(width * 1.48, 200, 360) : 260d;
    public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
