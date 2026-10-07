using System.Text.Json.Serialization;

namespace TimeTracking.Services;

/// <summary>
/// Estado persistido do ciclo (Seção 70, "Persistência do estado do ciclo") — gravado só em
/// eventos, nunca a cada segundo. O tempo restante exibido NÃO é guardado: é sempre calculado de
/// RemainingAtPhaseStart e PhaseStartedAtUtc (ver PomodoroService.GetRemainingTime).
/// </summary>
public class PomodoroState
{
    // Teto de qualquer tempo restante lido do disco: a maior duração configurável (foco, 120 min).
    private static readonly TimeSpan MaxRemaining = TimeSpan.FromMinutes(PomodoroConfig.MaxFocusMinutes);

    // A fase é gravada como texto e lida com Enum.TryParse (D7): um valor desconhecido (arquivo
    // editado à mão, versão futura) vira Idle em vez de uma JsonException, que faria o
    // AppSettingsStore.Load() descartar o settings.json inteiro (tema e cor de destaque também).
    [JsonPropertyName("Phase")]
    public string PhaseText { get; set; } = nameof(PomodoroPhase.Idle);

    [JsonIgnore]
    public PomodoroPhase Phase
    {
        get => ParsePhase(PhaseText) ?? PomodoroPhase.Idle;
        set => PhaseText = value.ToString();
    }

    /// <summary>Início do trecho atual da fase (UTC).</summary>
    public DateTime PhaseStartedAtUtc { get; set; }

    /// <summary>Tempo restante quando o trecho atual começou.</summary>
    public TimeSpan RemainingAtPhaseStart { get; set; }

    /// <summary>Preenchido apenas nos estados pausados.</summary>
    public TimeSpan? RemainingWhenPaused { get; set; }

    /// <summary>Quantos focos foram concluídos desde o último "Parar ciclo". Serve só para
    /// decidir entre pausa curta e longa — não é estatística (Seção 62).</summary>
    public int CompletedFocusCount { get; set; }

    public int? LinkedTaskId { get; set; }

    /// <summary>Qual pausa foi pausada (ShortBreak ou LongBreak) — só preenchido em BreakPaused.
    /// Campo adicional ao da Seção 70: sem ele não há como saber se retomar volta para a pausa
    /// curta ou para a longa, já que "focosAtéPausaLonga" pode ter mudado nas configurações.</summary>
    [JsonPropertyName("PausedPhase")]
    public string? PausedPhaseText { get; set; }

    [JsonIgnore]
    public PomodoroPhase? PausedPhase
    {
        get => ParsePhase(PausedPhaseText);
        set => PausedPhaseText = value?.ToString();
    }

    public PomodoroState Clone() => (PomodoroState)MemberwiseClone();

    /// <summary>Valida o que veio do disco (D7). Um estado incoerente (fase em andamento sem
    /// horário de início, fase pausada sem tempo restante) é descartado por inteiro — voltar ao
    /// Idle é mais seguro do que inventar um tempo restante.</summary>
    public void Normalize()
    {
        var phase = Phase;
        PhaseText = phase.ToString();

        PhaseStartedAtUtc = PhaseStartedAtUtc.Kind switch
        {
            DateTimeKind.Utc => PhaseStartedAtUtc,
            DateTimeKind.Local => PhaseStartedAtUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(PhaseStartedAtUtc, DateTimeKind.Utc),
        };
        RemainingAtPhaseStart = Clamp(RemainingAtPhaseStart);
        RemainingWhenPaused = RemainingWhenPaused is { } paused ? Clamp(paused) : null;
        CompletedFocusCount = Math.Max(0, CompletedFocusCount);
        LinkedTaskId = LinkedTaskId is > 0 ? LinkedTaskId : null;

        var pausedPhase = PausedPhase;
        if (pausedPhase is not (PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak))
        {
            pausedPhase = null;
        }

        PausedPhase = pausedPhase;

        var isRunning = phase is PomodoroPhase.Focus or PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak;
        var isPaused = phase is PomodoroPhase.FocusPaused or PomodoroPhase.BreakPaused;
        var incoherent =
            (isRunning && PhaseStartedAtUtc.Year < 2000)
            || (isPaused && RemainingWhenPaused is null)
            || (phase == PomodoroPhase.BreakPaused && pausedPhase is null);

        if (incoherent)
        {
            Reset(keepLinkedTask: true);
        }
    }

    /// <summary>Volta ao estado inicial (Idle, contador zerado, sem tempos).</summary>
    public void Reset(bool keepLinkedTask)
    {
        Phase = PomodoroPhase.Idle;
        PhaseStartedAtUtc = default;
        RemainingAtPhaseStart = TimeSpan.Zero;
        RemainingWhenPaused = null;
        CompletedFocusCount = 0;
        PausedPhase = null;
        if (!keepLinkedTask)
        {
            LinkedTaskId = null;
        }
    }

    private static PomodoroPhase? ParsePhase(string? text) =>
        Enum.TryParse<PomodoroPhase>(text, ignoreCase: false, out var phase) && Enum.IsDefined(phase)
            ? phase
            : null;

    private static TimeSpan Clamp(TimeSpan value) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value > MaxRemaining ? MaxRemaining : value;
}
