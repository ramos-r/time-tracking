namespace TimeTracking.Services;

public enum PomodoroActionStatus
{
    /// <summary>A ação foi executada.</summary>
    Success,

    /// <summary>Há outra tarefa em execução (Seção 15): a UI deve confirmar com o usuário e repetir
    /// a chamada com replaceActive: true. O diálogo é da UI, nunca do serviço (D3).</summary>
    NeedsConfirmation,

    /// <summary>A ação não vale no estado atual (ex.: Pausar fora do foco, ou uma fase que acabou
    /// de terminar). Não é erro: a UI apenas reflete o estado atual.</summary>
    InvalidState,

    /// <summary>A ação não pôde ser feita; Message traz o texto amigável (Seção 39).</summary>
    Failed
}

/// <param name="ActiveTaskName">Só em NeedsConfirmation: nome da tarefa que está em execução.</param>
/// <param name="Message">Só em Failed: mensagem amigável para o usuário.</param>
public record PomodoroActionResult(PomodoroActionStatus Status, string? ActiveTaskName = null, string? Message = null)
{
    public static PomodoroActionResult Success { get; } = new(PomodoroActionStatus.Success);
    public static PomodoroActionResult InvalidState { get; } = new(PomodoroActionStatus.InvalidState);
    public static PomodoroActionResult NeedsConfirmation(string activeTaskName) =>
        new(PomodoroActionStatus.NeedsConfirmation, ActiveTaskName: activeTaskName);
    public static PomodoroActionResult Failed(string message) =>
        new(PomodoroActionStatus.Failed, Message: message);
}

/// <summary>
/// Máquina de estados do Pomodoro (Seção 70). Não é um segundo timer: usa o ITimerService para
/// abrir e fechar a TimeEntry da tarefa vinculada, e nunca toca em repositórios. Também não
/// conhece WPF — quem hospeda o tick de 1 segundo e o aviso de fim de fase são o ViewModel e um
/// componente à parte (Fases 12B/12C).
/// </summary>
public interface IPomodoroService
{
    /// <summary>Cópia do estado persistido do ciclo.</summary>
    PomodoroState State { get; }

    PomodoroPhase Phase { get; }

    /// <summary>Cópia das durações configuradas.</summary>
    PomodoroConfig Config { get; }

    /// <summary>Tempo restante da fase, sempre calculado por timestamps e limitado a
    /// [0, duração do trecho]. Zero fora das fases de contagem e das pausadas.</summary>
    TimeSpan GetRemainingTime();

    /// <summary>"Ciclo N de M" (Seção 70, D10): no foco, (concluídos % M) + 1; nas pausas e em
    /// AguardandoPausa, o foco que acabou de ser concluído (M de M antes da pausa longa);
    /// em AguardandoFoco, o próximo foco.</summary>
    int CycleNumber { get; }

    /// <summary>Disparado depois de qualquer mudança no estado ou no vínculo.</summary>
    event Action? StateChanged;

    /// <summary>Disparado quando o fim de uma fase (Focus, ShortBreak ou LongBreak) é detectado
    /// com o app aberto — base do aviso da Fase 12C. Não dispara na recuperação ao abrir o app
    /// (Seção 70: sem som ao reabrir depois do fim).</summary>
    event Action<PomodoroPhase>? PhaseEnded;

    /// <summary>Inicia o primeiro foco a partir de Idle, com tarefa vinculada opcional.</summary>
    Task<PomodoroActionResult> StartAsync(int? taskId, bool replaceActive = false);

    /// <summary>Pausa o foco (encerra a TimeEntry "agora") ou uma pausa (sem efeito em TimeEntry).</summary>
    Task<PomodoroActionResult> PauseAsync();

    Task<PomodoroActionResult> ResumeAsync(bool replaceActive = false);

    /// <summary>De AguardandoPausa inicia a pausa curta/longa; de AguardandoFoco inicia o foco.</summary>
    Task<PomodoroActionResult> StartNextPhaseAsync(bool replaceActive = false);

    /// <summary>Parar ciclo: encerra a TimeEntry aberta (se houver), zera o contador e volta a Idle.</summary>
    Task StopCycleAsync();

    /// <summary>Escolhe/troca a tarefa vinculada; só vale quando não há foco rodando.</summary>
    Task<PomodoroActionResult> SetLinkedTaskAsync(int? taskId);

    /// <summary>Chamado pelo tick de 1 segundo: detecta o fim da fase e aplica a regra crítica
    /// (a TimeEntry termina no horário teórico, nunca em UtcNow).</summary>
    Task CheckPhaseEndAsync();

    /// <summary>Reconcilia o estado salvo com o relógio e com as sessões reais; deve rodar depois
    /// da migração do banco (Seção 16).</summary>
    Task RecoverOnStartupAsync();

    /// <summary>Grava novas durações; não afeta a fase em andamento.</summary>
    void UpdateConfig(PomodoroConfig config);
}
