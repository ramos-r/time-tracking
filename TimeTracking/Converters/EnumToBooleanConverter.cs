using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TimeTracking.Converters;

/// <summary>Liga um RadioButton.IsChecked a um valor específico de uma propriedade enum —
/// usado na seleção de tema (Seção 26).</summary>
public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true && parameter is not null ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}
