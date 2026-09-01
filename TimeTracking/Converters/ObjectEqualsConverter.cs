using System.Globalization;
using System.Windows.Data;

namespace TimeTracking.Converters;

/// <summary>Compara dois valores quaisquer por igualdade — usado para destacar a página
/// ativa na sidebar (compara o CommandParameter de cada botão com a página atual).</summary>
public class ObjectEqualsConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2 && Equals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
