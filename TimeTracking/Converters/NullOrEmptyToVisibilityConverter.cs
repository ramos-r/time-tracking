using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TimeTracking.Converters;

/// <summary>Colapsa quando o valor é nulo ou uma string vazia/em branco — usado para
/// esconder elementos opcionais (descrição, tag, mensagem de erro) quando ausentes.</summary>
public class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var isEmpty = value is null || (value is string s && string.IsNullOrWhiteSpace(s));
        return isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
