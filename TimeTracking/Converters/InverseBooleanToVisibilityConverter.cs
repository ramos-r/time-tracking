using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TimeTracking.Converters;

/// <summary>Visible quando false, Collapsed quando true — usado para alternar Play vs. Pause/Stop.</summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
