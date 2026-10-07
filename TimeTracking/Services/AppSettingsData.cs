namespace TimeTracking.Services;

/// <summary>
/// Modelo do arquivo settings.json (Seção 26/57). Compartilhado entre ThemeService,
/// AccentColorService e PomodoroService — ver AppSettingsStore para o porquê de um modelo único.
/// </summary>
public class AppSettingsData
{
    public string Theme { get; set; } = nameof(AppTheme.System);

    /// <summary>Hex (#RRGGBB) da cor de destaque escolhida pelo usuário (Seção 69). Null =
    /// nenhuma preferência salva ainda — AccentColorService aplica o padrão de fábrica.</summary>
    public string? AccentColorHex { get; set; }

    /// <summary>Durações do Pomodoro (Seção 70, "Configurações").</summary>
    public PomodoroConfig Pomodoro { get; set; } = new();

    /// <summary>Estado do ciclo do Pomodoro (Seção 70, "Persistência do estado do ciclo").</summary>
    public PomodoroState PomodoroState { get; set; } = new();

    /// <summary>Corrige o que veio do disco (D7): objetos ausentes/nulos viram o padrão e os
    /// valores do Pomodoro voltam aos limites permitidos.</summary>
    public void Normalize()
    {
        Pomodoro = (Pomodoro ?? new PomodoroConfig()).Normalized();
        PomodoroState ??= new PomodoroState();
        PomodoroState.Normalize();
    }
}
