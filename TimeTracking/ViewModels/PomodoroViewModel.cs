using CommunityToolkit.Mvvm.ComponentModel;

namespace TimeTracking.ViewModels;

/// <summary>
/// Placeholder de navegação (Seção 36). Pomodoro não faz parte do MVP —
/// nenhuma funcionalidade deve ser implementada aqui além do aviso "Em breve".
/// </summary>
public partial class PomodoroViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Pomodoro";

    [ObservableProperty]
    private string _message = "Em breve";
}
