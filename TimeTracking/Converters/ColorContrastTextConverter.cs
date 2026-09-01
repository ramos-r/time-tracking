using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TimeTracking.Converters;

public class ColorContrastTextConverter : IValueConverter
{
    private static readonly Brush DarkText = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly Brush LightText = Brushes.White;

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string hex || !TryParseHex(hex, out var color))
        {
            return LightText;
        }

        var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        return luminance > 0.6 ? DarkText : LightText;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool TryParseHex(string hex, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex)!;
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }
}
