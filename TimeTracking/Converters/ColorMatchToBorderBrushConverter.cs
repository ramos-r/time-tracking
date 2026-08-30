using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TimeTracking.Converters;

/// <summary>Compara duas cores (hex) e retorna um Brush de destaque quando iguais —
/// usado para marcar visualmente o swatch atualmente selecionado na paleta de cores.</summary>
public class ColorMatchToBorderBrushConverter : IMultiValueConverter
{
    private static readonly Brush Highlight = new SolidColorBrush(Color.FromRgb(0xF2, 0xEC, 0xE5));

    public object Convert(object?[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is string a && values[1] is string b
            && string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return Highlight;
        }

        return Brushes.Transparent;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
