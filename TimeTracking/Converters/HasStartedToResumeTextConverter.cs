using System.Globalization;
using System.Windows.Data;

namespace TimeTracking.Converters;

/// <summary>True (já teve ao menos uma sessão) → "Retomar"; False (nunca foi iniciada) →
/// "Iniciar". Usado no botão de play e no menu de contexto da tarefa (Seção 71, feedback de
/// usuário: uma tarefa recém-criada, nunca iniciada, não pode dizer "Retomar").</summary>
public class HasStartedToResumeTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Retomar" : "Iniciar";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
