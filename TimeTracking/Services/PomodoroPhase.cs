namespace TimeTracking.Services;

/// <summary>Estados da máquina do Pomodoro (Seção 70). Nomes em inglês, seguindo o resto do
/// código; correspondência com o texto da seção: Idle, Focus (Foco), FocusPaused (FocoPausado),
/// AwaitingBreak (AguardandoPausa), ShortBreak (PausaCurta), LongBreak (PausaLonga),
/// BreakPaused (PausaPausada) e AwaitingFocus (AguardandoFoco).</summary>
public enum PomodoroPhase
{
    Idle,
    Focus,
    FocusPaused,
    AwaitingBreak,
    ShortBreak,
    LongBreak,
    BreakPaused,
    AwaitingFocus
}
