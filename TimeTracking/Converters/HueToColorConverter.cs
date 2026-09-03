using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TimeTracking.Services;

namespace TimeTracking.Converters;

/// <summary>Converte um matiz (0-359) em Color a saturação/luminosidade fixas — usado para
/// colorir a ponta do gradiente do slider de saturação do seletor de cor livre (Seção 69),
/// que precisa acompanhar o matiz escolhido no outro slider.</summary>
public class HueToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var hue = value is double d ? d : 0.0;
        return AccentColorCalculator.HslToRgb(hue / 360.0, 1.0, 0.5);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
