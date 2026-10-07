namespace TimeTracking.Services;

/// <summary>Os quatro valores ajustáveis do Pomodoro (Seção 70, "Configurações"), persistidos no
/// settings.json. Alterá-los não afeta a fase em andamento: o tempo restante de cada fase já é
/// gravado no PomodoroState quando ela começa.</summary>
public class PomodoroConfig
{
    public const int MinFocusMinutes = 1;
    public const int MaxFocusMinutes = 120;
    public const int MinShortBreakMinutes = 1;
    public const int MaxShortBreakMinutes = 60;
    public const int MinLongBreakMinutes = 1;
    public const int MaxLongBreakMinutes = 60;
    public const int MinFocusesUntilLongBreak = 2;
    public const int MaxFocusesUntilLongBreak = 10;

    public int FocusMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int FocusesUntilLongBreak { get; set; } = 4;

    public TimeSpan FocusDuration => TimeSpan.FromMinutes(FocusMinutes);
    public TimeSpan ShortBreakDuration => TimeSpan.FromMinutes(ShortBreakMinutes);
    public TimeSpan LongBreakDuration => TimeSpan.FromMinutes(LongBreakMinutes);

    public static PomodoroConfig Default => new();

    public PomodoroConfig Clone() => (PomodoroConfig)MemberwiseClone();

    /// <summary>Corrige para os limites da tabela da Seção 70 (valores lidos de um arquivo
    /// editado à mão ou corrompido não podem produzir durações absurdas).</summary>
    public PomodoroConfig Normalized() => new()
    {
        FocusMinutes = Math.Clamp(FocusMinutes, MinFocusMinutes, MaxFocusMinutes),
        ShortBreakMinutes = Math.Clamp(ShortBreakMinutes, MinShortBreakMinutes, MaxShortBreakMinutes),
        LongBreakMinutes = Math.Clamp(LongBreakMinutes, MinLongBreakMinutes, MaxLongBreakMinutes),
        FocusesUntilLongBreak = Math.Clamp(FocusesUntilLongBreak, MinFocusesUntilLongBreak, MaxFocusesUntilLongBreak),
    };
}
