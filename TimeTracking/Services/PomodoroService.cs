using TimeTracking.Helpers;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Services;

/// <summary>
/// Implementação da máquina de estados da Seção 70. Pontos que sustentam o resto:
///
/// - O tempo restante nunca é um contador que decresce: é sempre RemainingAtPhaseStart menos o
///   tempo decorrido desde PhaseStartedAtUtc, o que torna a suspensão do PC e a reabertura do app
///   casos naturais (Seção 16).
/// - Regra crítica: o fim de um foco encerra a TimeEntry em PhaseStartedAtUtc + RemainingAtPhaseStart
///   (o fim teórico), nunca em UtcNow — ver ProcessDueEndAsync.
/// - Todas as operações passam por um SemaphoreSlim (D5): sem isso, um tick no meio de um
///   "Pausar" assíncrono poderia processar o fim da fase duas vezes. Os eventos públicos são
///   disparados depois de soltar o semáforo, para que um handler nunca fique preso nele.
/// - Só a tarefa vinculada gera TimeEntry, e sempre através do ITimerService com
///   origin = Pomodoro (as mudanças com essa origem são ignoradas pelo próprio serviço).
/// </summary>
public class PomodoroService : IPomodoroService
{
    private readonly ITimerService _timerService;
    private readonly ITaskService _taskService;
    private readonly AppSettingsStore _settingsStore;
    private readonly IClock _clock;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _stateChangedPending;
    private readonly List<PomodoroPhase> _pendingPhaseEnds = new();

    // Substituídos por inteiro (copy-on-write) a cada mudança: quem lê fora do semáforo (o tick
    // da UI) sempre enxerga um objeto consistente.
    private PomodoroState _state;
    private PomodoroConfig _config;

    public PomodoroService(ITimerService timerService, ITaskService taskService, AppSettingsStore settingsStore, IClock clock)
    {
        _timerService = timerService;
        _taskService = taskService;
        _settingsStore = settingsStore;
        _clock = clock;

        var data = _settingsStore.Load();
        _state = data.PomodoroState;
        _config = data.Pomodoro;

        _timerService.TimerChanged += OnTimerChanged;
        _taskService.TaskDeleted += OnTaskDeleted;
        _taskService.HistoryCleared += OnHistoryCleared;
    }

    public event Action? StateChanged;
    public event Action<PomodoroPhase>? PhaseEnded;

    public PomodoroState State => _state.Clone();
    public PomodoroPhase Phase => _state.Phase;
    public PomodoroConfig Config => _config.Clone();

    public TimeSpan GetRemainingTime() => RemainingAt(_state, _clock.UtcNow);

    public int CycleNumber
    {
        get
        {
            var state = _state;
            var total = _config.FocusesUntilLongBreak;
            var completed = state.CompletedFocusCount;

            return state.Phase switch
            {
                PomodoroPhase.AwaitingBreak or PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak or PomodoroPhase.BreakPaused
                    => completed % total == 0 ? total : completed % total,
                _ => (completed % total) + 1,
            };
        }
    }

    public Task<PomodoroActionResult> StartAsync(int? taskId, bool replaceActive = false) =>
        RunLockedAsync(async () =>
        {
            if (_state.Phase != PomodoroPhase.Idle)
            {
                return PomodoroActionResult.InvalidState;
            }

            if (taskId is int id && await _taskService.GetByIdAsync(id) is null)
            {
                return PomodoroActionResult.Failed("A tarefa escolhida não existe mais.");
            }

            return await BeginFocusAsync(taskId, _config.FocusDuration, replaceActive);
        });

    public Task<PomodoroActionResult> PauseAsync() =>
        RunLockedAsync(async () =>
        {
            // Se o fim da fase já passou, quem trata é o fim — não uma pausa com tempo zerado.
            if (await ProcessDueEndAsync(notify: true))
            {
                return PomodoroActionResult.InvalidState;
            }

            var state = _state;
            var now = _clock.UtcNow;

            switch (state.Phase)
            {
                case PomodoroPhase.Focus:
                    var remaining = RemainingAt(state, now);
                    if (state.LinkedTaskId is int linked)
                    {
                        await _timerService.PauseAsync(linked, TimerOrigin.Pomodoro);
                    }

                    Commit(next =>
                    {
                        next.Phase = PomodoroPhase.FocusPaused;
                        next.PhaseStartedAtUtc = now;
                        next.RemainingAtPhaseStart = remaining;
                        next.RemainingWhenPaused = remaining;
                    });
                    return PomodoroActionResult.Success;

                case PomodoroPhase.ShortBreak:
                case PomodoroPhase.LongBreak:
                    var breakRemaining = RemainingAt(state, now);
                    Commit(next =>
                    {
                        next.PausedPhase = state.Phase;
                        next.Phase = PomodoroPhase.BreakPaused;
                        next.PhaseStartedAtUtc = now;
                        next.RemainingAtPhaseStart = breakRemaining;
                        next.RemainingWhenPaused = breakRemaining;
                    });
                    return PomodoroActionResult.Success;

                default:
                    return PomodoroActionResult.InvalidState;
            }
        });

    public Task<PomodoroActionResult> ResumeAsync(bool replaceActive = false) =>
        RunLockedAsync(async () =>
        {
            var state = _state;
            switch (state.Phase)
            {
                case PomodoroPhase.FocusPaused:
                    return await BeginFocusAsync(state.LinkedTaskId, state.RemainingWhenPaused ?? TimeSpan.Zero, replaceActive);

                case PomodoroPhase.BreakPaused:
                    var now = _clock.UtcNow;
                    Commit(next =>
                    {
                        next.Phase = state.PausedPhase ?? PomodoroPhase.ShortBreak;
                        next.PausedPhase = null;
                        next.PhaseStartedAtUtc = now;
                        next.RemainingAtPhaseStart = state.RemainingWhenPaused ?? TimeSpan.Zero;
                        next.RemainingWhenPaused = null;
                    });
                    return PomodoroActionResult.Success;

                default:
                    return PomodoroActionResult.InvalidState;
            }
        });

    public Task<PomodoroActionResult> StartNextPhaseAsync(bool replaceActive = false) =>
        RunLockedAsync(async () =>
        {
            var state = _state;
            switch (state.Phase)
            {
                case PomodoroPhase.AwaitingBreak:
                    // A pausa longa entra quando focosConcluídos % focosAtéPausaLonga == 0 (Seção 70).
                    var isLong = state.CompletedFocusCount % _config.FocusesUntilLongBreak == 0;
                    var now = _clock.UtcNow;
                    Commit(next =>
                    {
                        next.Phase = isLong ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
                        next.PhaseStartedAtUtc = now;
                        next.RemainingAtPhaseStart = isLong ? _config.LongBreakDuration : _config.ShortBreakDuration;
                        next.RemainingWhenPaused = null;
                    });
                    return PomodoroActionResult.Success;

                case PomodoroPhase.AwaitingFocus:
                    return await BeginFocusAsync(state.LinkedTaskId, _config.FocusDuration, replaceActive);

                default:
                    return PomodoroActionResult.InvalidState;
            }
        });

    public Task StopCycleAsync() =>
        RunLockedAsync(async () =>
        {
            // Se o foco já tinha terminado, a TimeEntry fecha no fim teórico (não em "agora").
            // Sem aviso: o usuário está encerrando o ciclo de qualquer forma.
            await ProcessDueEndAsync(notify: false);

            var state = _state;
            if (state.Phase == PomodoroPhase.Focus && state.LinkedTaskId is int linked)
            {
                await _timerService.PauseAsync(linked, TimerOrigin.Pomodoro);
            }

            // O vínculo com a tarefa é mantido: parar o ciclo zera o contador, não a escolha da tarefa.
            Commit(next => next.Reset(keepLinkedTask: true));
            return true;
        });

    public Task<PomodoroActionResult> SetLinkedTaskAsync(int? taskId) =>
        RunLockedAsync(async () =>
        {
            // Se o foco que estava rodando acabou de terminar, a troca já vale (não está mais em foco).
            await ProcessDueEndAsync(notify: true);

            if (_state.Phase == PomodoroPhase.Focus)
            {
                return PomodoroActionResult.InvalidState;
            }

            if (taskId is int id && await _taskService.GetByIdAsync(id) is null)
            {
                return PomodoroActionResult.Failed("A tarefa escolhida não existe mais.");
            }

            if (_state.LinkedTaskId != taskId)
            {
                Commit(next => next.LinkedTaskId = taskId);
            }

            return PomodoroActionResult.Success;
        });

    public Task CheckPhaseEndAsync() =>
        RunLockedAsync(() => ProcessDueEndAsync(notify: true));

    public Task RecoverOnStartupAsync() =>
        RunLockedAsync(async () =>
        {
            var state = _state;

            if (state.LinkedTaskId is int linkedId && await _taskService.GetByIdAsync(linkedId) is null)
            {
                Commit(next => next.LinkedTaskId = null);
                state = _state;
            }

            switch (state.Phase)
            {
                case PomodoroPhase.Focus:
                case PomodoroPhase.ShortBreak:
                case PomodoroPhase.LongBreak:
                    // Reabrir depois do fim: encerra no fim teórico e vai para Aguardando*, sem
                    // aviso sonoro (Seção 70). Antes do fim, nada a fazer — o tempo restante é
                    // calculado dos timestamps.
                    if (await ProcessDueEndAsync(notify: false))
                    {
                        break;
                    }

                    // Foco ainda no prazo, mas a sessão da tarefa não está aberta (ex.: o app caiu
                    // entre fechar a TimeEntry e gravar o estado): em vez de contar tempo que não
                    // está sendo registrado, o foco fica pausado.
                    if (state.Phase == PomodoroPhase.Focus && state.LinkedTaskId is int linked
                        && !(await _timerService.GetStatusAsync(linked)).IsRunning)
                    {
                        var now = _clock.UtcNow;
                        var remaining = RemainingAt(state, now);
                        Commit(next =>
                        {
                            next.Phase = PomodoroPhase.FocusPaused;
                            next.PhaseStartedAtUtc = now;
                            next.RemainingAtPhaseStart = remaining;
                            next.RemainingWhenPaused = remaining;
                        });
                    }

                    break;
            }

            // O estado normalizado ao ler (D7) pode ter diferido do arquivo; grava e avisa a UI.
            Commit(_ => { });
            return true;
        });

    public void UpdateConfig(PomodoroConfig config)
    {
        var normalized = config.Normalized();
        _config = normalized;
        _settingsStore.Save(data => data.Pomodoro = normalized.Clone());
    }

    // ---------------------------------------------------------------------------------------
    // Núcleo (sempre chamado com o semáforo tomado)
    // ---------------------------------------------------------------------------------------

    /// <summary>Começa (ou retoma) um trecho de foco com o tempo restante dado. Com tarefa
    /// vinculada, abre a TimeEntry pelo TimerService — ou reaproveita a sessão aberta se a própria
    /// tarefa já estiver rodando (Seção 70).</summary>
    private async Task<PomodoroActionResult> BeginFocusAsync(int? linkedTaskId, TimeSpan remaining, bool replaceActive)
    {
        if (linkedTaskId is int id && await _taskService.GetByIdAsync(id) is null)
        {
            linkedTaskId = null; // a tarefa foi excluída: o ciclo segue sem gravar TimeEntry
        }

        if (linkedTaskId is int taskId)
        {
            var active = await _timerService.GetActiveTaskAsync();
            if (active is not null && active.Id != taskId && !replaceActive)
            {
                return PomodoroActionResult.NeedsConfirmation(active.Name);
            }

            try
            {
                await _timerService.StartAsync(taskId, TimerOrigin.Pomodoro);
            }
            catch (TimerConflictException ex)
            {
                return PomodoroActionResult.Failed(ex.Message);
            }
        }

        // O início do trecho é marcado depois de abrir a sessão: assim a TimeEntry nunca começa
        // depois do início do foco, e o fim teórico nunca fica antes do início da TimeEntry.
        var now = _clock.UtcNow;
        Commit(next =>
        {
            next.Phase = PomodoroPhase.Focus;
            next.LinkedTaskId = linkedTaskId;
            next.PhaseStartedAtUtc = now;
            next.RemainingAtPhaseStart = remaining;
            next.RemainingWhenPaused = null;
            next.PausedPhase = null;
        });
        return PomodoroActionResult.Success;
    }

    /// <summary>Se a fase em contagem já passou do fim, aplica o fim. Regra crítica (Seção 70): a
    /// TimeEntry do foco é encerrada em PhaseStartedAtUtc + RemainingAtPhaseStart, nunca em
    /// UtcNow — seja com o app aberto, após suspensão ou ao reabrir. Retorna true se aplicou.</summary>
    private async Task<bool> ProcessDueEndAsync(bool notify)
    {
        var state = _state;
        if (state.Phase is not (PomodoroPhase.Focus or PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak))
        {
            return false;
        }

        var theoreticalEnd = state.PhaseStartedAtUtc + state.RemainingAtPhaseStart;
        if (_clock.UtcNow < theoreticalEnd)
        {
            return false;
        }

        if (state.Phase == PomodoroPhase.Focus)
        {
            await CloseLinkedSessionAtAsync(state.LinkedTaskId, theoreticalEnd);
            Commit(next =>
            {
                next.Phase = PomodoroPhase.AwaitingBreak;
                next.PhaseStartedAtUtc = theoreticalEnd;
                next.RemainingAtPhaseStart = TimeSpan.Zero;
                next.RemainingWhenPaused = null;
                next.CompletedFocusCount++;
            });
        }
        else
        {
            Commit(next =>
            {
                next.Phase = PomodoroPhase.AwaitingFocus;
                next.PhaseStartedAtUtc = theoreticalEnd;
                next.RemainingAtPhaseStart = TimeSpan.Zero;
                next.RemainingWhenPaused = null;
            });
        }

        if (notify)
        {
            _pendingPhaseEnds.Add(state.Phase);
        }

        return true;
    }

    private async Task CloseLinkedSessionAtAsync(int? linkedTaskId, DateTime theoreticalEnd)
    {
        if (linkedTaskId is not int taskId)
        {
            return;
        }

        var status = await _timerService.GetStatusAsync(taskId);
        if (!status.IsRunning || status.RunningStartedAt is not { } startedAt)
        {
            return; // a sessão já foi encerrada por outro caminho (ex.: o usuário parou a tarefa)
        }

        // Início editado (Seção 17) para depois do fim teórico: respeita a validação do StopAtAsync.
        var endedAt = theoreticalEnd < startedAt ? startedAt : theoreticalEnd;
        try
        {
            await _timerService.StopAtAsync(taskId, endedAt, TimerOrigin.Pomodoro);
        }
        catch (ArgumentException ex)
        {
            // Horário rejeitado (ex.: início da sessão no futuro): o estado do ciclo segue em
            // frente; a sessão fica aberta para o usuário resolver, e o motivo vai para o log.
            AppLog.Error($"Não foi possível encerrar a sessão da tarefa {taskId} em {endedAt:O}.", ex);
        }
    }

    private static TimeSpan RemainingAt(PomodoroState state, DateTime now)
    {
        switch (state.Phase)
        {
            case PomodoroPhase.Focus:
            case PomodoroPhase.ShortBreak:
            case PomodoroPhase.LongBreak:
                // [0, duração do trecho]: o limite superior cobre o relógio do Windows ajustado
                // para trás (tempo decorrido negativo).
                var remaining = state.RemainingAtPhaseStart - (now - state.PhaseStartedAtUtc);
                if (remaining < TimeSpan.Zero)
                {
                    return TimeSpan.Zero;
                }

                return remaining > state.RemainingAtPhaseStart ? state.RemainingAtPhaseStart : remaining;

            case PomodoroPhase.FocusPaused:
            case PomodoroPhase.BreakPaused:
                return state.RemainingWhenPaused ?? TimeSpan.Zero;

            default:
                return TimeSpan.Zero;
        }
    }

    /// <summary>Aplica uma mudança ao estado (copy-on-write), grava no settings.json e agenda o
    /// StateChanged — gravar só em eventos, nunca a cada segundo (Seção 43).</summary>
    private void Commit(Action<PomodoroState> change)
    {
        var next = _state.Clone();
        change(next);
        _state = next;

        var snapshot = next.Clone();
        _settingsStore.Save(data => data.PomodoroState = snapshot);
        _stateChangedPending = true;
    }

    // ---------------------------------------------------------------------------------------
    // Reações a mudanças externas (Seção 70, "Interação com a tela Time Tracking")
    // ---------------------------------------------------------------------------------------

    private void OnTimerChanged(TimerChange change)
    {
        // Verificado de forma síncrona, antes de disputar o semáforo: uma ação do próprio
        // Pomodoro dispara este evento enquanto ele ainda está segurando o semáforo.
        if (change.Origin == TimerOrigin.Pomodoro)
        {
            return;
        }

        _ = ReactAsync(() => HandleExternalTimerChangeAsync(change), "mudança externa do timer");
    }

    private async Task<bool> HandleExternalTimerChangeAsync(TimerChange change)
    {
        var state = _state;
        if (state.Phase != PomodoroPhase.Focus || state.LinkedTaskId is not int linked)
        {
            return false;
        }

        // Parou/pausou a tarefa vinculada, ou iniciou outra: em ambos os casos a sessão do foco
        // deixou de estar aberta.
        var affectsFocus = change.Kind == TimerChangeKind.Ended
            ? change.TaskId == linked
            : change.TaskId != linked;
        if (!affectsFocus)
        {
            return false;
        }

        // O evento é processado de forma assíncrona; o fim da fase pode ter passado nesse meio-tempo.
        if (await ProcessDueEndAsync(notify: true))
        {
            return true;
        }

        var now = _clock.UtcNow;
        var remaining = RemainingAt(state, now);
        Commit(next =>
        {
            next.Phase = PomodoroPhase.FocusPaused;
            next.PhaseStartedAtUtc = now;
            next.RemainingAtPhaseStart = remaining;
            next.RemainingWhenPaused = remaining;
        });
        return true;
    }

    private void OnTaskDeleted(int taskId) =>
        _ = ReactAsync(() => UnlinkAsync(taskId), "exclusão de tarefa");

    private void OnHistoryCleared() =>
        _ = ReactAsync(() => UnlinkAsync(null), "limpeza do histórico");

    /// <summary>Remove o vínculo (taskId nulo = qualquer tarefa). O ciclo continua; só deixa de
    /// gravar TimeEntry.</summary>
    private Task<bool> UnlinkAsync(int? taskId)
    {
        var state = _state;
        if (state.LinkedTaskId is int linked && (taskId is null || taskId == linked))
        {
            Commit(next => next.LinkedTaskId = null);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private async Task ReactAsync(Func<Task<bool>> reaction, string description)
    {
        try
        {
            await RunLockedAsync(reaction);
        }
        catch (Exception ex)
        {
            // Fire-and-forget: sem este catch, uma falha viraria exceção sem dono na thread de UI.
            AppLog.Error($"Falha ao reagir a {description} no Pomodoro.", ex);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Serialização (D5)
    // ---------------------------------------------------------------------------------------

    private async Task<T> RunLockedAsync<T>(Func<Task<T>> body)
    {
        var raiseStateChanged = false;
        var phaseEnds = Array.Empty<PomodoroPhase>();

        await _gate.WaitAsync();
        try
        {
            return await body();
        }
        finally
        {
            raiseStateChanged = _stateChangedPending;
            _stateChangedPending = false;
            phaseEnds = _pendingPhaseEnds.ToArray();
            _pendingPhaseEnds.Clear();
            _gate.Release();

            // Fora do semáforo (já solto acima): um handler que chame de volta o serviço não
            // fica preso nele. Disparado também se o corpo lançar — o estado pode já ter mudado.
            if (raiseStateChanged)
            {
                StateChanged?.Invoke();
            }

            foreach (var phase in phaseEnds)
            {
                PhaseEnded?.Invoke(phase);
            }
        }
    }
}
