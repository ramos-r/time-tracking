using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTracking.Helpers;
using TimeTracking.Services;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.ViewModels;

/// <summary>Opção do seletor de tarefa da tela Pomodoro; Id nulo = "Sem tarefa".</summary>
public record PomodoroTaskOption(int? Id, string Name);

/// <summary>Um ponto do indicador de ciclo ("● ● ○ ○").</summary>
public record CycleDotViewModel(bool IsFilled);

/// <summary>
/// Estado e comandos da tela Pomodoro (Seção 70). Nenhuma regra de tempo mora aqui (Seção 5): o
/// ViewModel só reflete o IPomodoroService e hospeda o tick de 1 segundo, que apenas chama
/// CheckPhaseEndAsync() e atualiza o relógio exibido. É um singleton resolvido já no startup
/// (App.OnStartup) — e não na primeira navegação — para que o tick rode e o fim da fase seja
/// detectado mesmo com o usuário em outra tela.
/// </summary>
public partial class PomodoroViewModel : ObservableObject
{
    private static readonly PomodoroTaskOption NoTask = new(null, "Sem tarefa");

    private readonly IPomodoroService _service;
    private readonly ITaskService _taskService;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _tickTimer;

    private bool _syncingTaskSelection;
    private bool _syncingConfig;
    private bool _ticking;
    private Func<bool, Task<PomodoroActionResult>>? _pendingConflictAction;

    [ObservableProperty]
    private ObservableCollection<PomodoroTaskOption> _taskOptions = new() { NoTask };

    [ObservableProperty]
    private PomodoroTaskOption _selectedTaskOption = NoTask;

    [ObservableProperty]
    private string _phaseLabel = string.Empty;

    [ObservableProperty]
    private string _timeDisplay = "25:00";

    [ObservableProperty]
    private string _cycleText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CycleDotViewModel> _cycleDots = new();

    [ObservableProperty]
    private string _primaryActionText = "Iniciar foco";

    [ObservableProperty]
    private bool _showStopButton;

    [ObservableProperty]
    private bool _canChangeTask = true;

    [ObservableProperty]
    private bool _areSettingsEnabled = true;

    /// <summary>Foco usa a cor de destaque; pausas usam a cor de sucesso; o resto é neutro. O
    /// texto da fase também muda — nunca só a cor (Seção 41).</summary>
    [ObservableProperty]
    private bool _isFocusPhase;

    [ObservableProperty]
    private bool _isBreakPhase;

    /// <summary>Há um ciclo em andamento (qualquer estado fora de Idle) — base do chip da barra
    /// superior quando o usuário está em outra tela (D9).</summary>
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _chipText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    // Conflito de timer (Seção 15) ao iniciar/retomar o foco com a tarefa vinculada: o diálogo
    // é mostrado aqui, nunca no serviço (D3).
    [ObservableProperty]
    private bool _isConflictOpen;

    [ObservableProperty]
    private string _conflictMessage = string.Empty;

    // Configurações (Seção 70) como texto, para o usuário poder digitar livremente; o valor só é
    // validado e aplicado quando o campo perde o foco.
    [ObservableProperty]
    private string _focusMinutesText = string.Empty;

    [ObservableProperty]
    private string _shortBreakMinutesText = string.Empty;

    [ObservableProperty]
    private string _longBreakMinutesText = string.Empty;

    [ObservableProperty]
    private string _focusesUntilLongBreakText = string.Empty;

    [ObservableProperty]
    private string? _configError;

    /// <summary>As configurações ficam recolhidas por padrão; o usuário abre a "aba" clicando
    /// em "Configurações". Não é persistido: a tela sempre abre recolhida.</summary>
    [ObservableProperty]
    private bool _isSettingsOpen;

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    public PomodoroViewModel(IPomodoroService service, ITaskService taskService)
    {
        _service = service;
        _taskService = taskService;
        _dispatcher = Dispatcher.CurrentDispatcher;

        LoadConfigTexts();
        UpdateFromService();

        _service.StateChanged += OnServiceStateChanged;

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += async (_, _) => await TickAsync();
        _tickTimer.Start();

        _ = RefreshTasksAsync();
    }

    // ------------------------------------------------------------------ tick

    /// <summary>Tick de 1 segundo: o serviço detecta o fim da fase (regra de tempo fica lá);
    /// aqui só se atualiza o relógio exibido. Público para os testes chamarem sem esperar 1 s.</summary>
    public async Task TickAsync()
    {
        if (_ticking)
        {
            return;
        }

        _ticking = true;
        try
        {
            await _service.CheckPhaseEndAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Falha no tick do Pomodoro.", ex);
        }
        finally
        {
            _ticking = false;
        }

        RefreshTime();
    }

    private void OnServiceStateChanged()
    {
        if (_dispatcher.CheckAccess())
        {
            UpdateFromService();
        }
        else
        {
            _dispatcher.BeginInvoke(UpdateFromService);
        }
    }

    // ------------------------------------------------------------------ tarefas

    /// <summary>Recarrega a lista do seletor (chamado no Loaded da PomodoroView): o singleton
    /// vive desde o startup, então a lista de tarefas precisa ser renovada a cada visita —
    /// tarefas criadas ou excluídas em outras telas não chegam aqui de outra forma.</summary>
    public async Task RefreshTasksAsync()
    {
        try
        {
            var tasks = await _taskService.GetAllAsync();
            var options = new List<PomodoroTaskOption> { NoTask };
            options.AddRange(tasks.Select(t => new PomodoroTaskOption(t.Id, t.Name)));

            _syncingTaskSelection = true;
            try
            {
                TaskOptions = new ObservableCollection<PomodoroTaskOption>(options);
                SelectedTaskOption = FindOption(_service.State.LinkedTaskId);
            }
            finally
            {
                _syncingTaskSelection = false;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Falha ao carregar as tarefas do Pomodoro.", ex);
            ErrorMessage = "Não foi possível carregar as tarefas.";
        }
    }

    private PomodoroTaskOption FindOption(int? taskId) =>
        TaskOptions.FirstOrDefault(o => o.Id == taskId) ?? NoTask;

    partial void OnSelectedTaskOptionChanged(PomodoroTaskOption value)
    {
        if (_syncingTaskSelection || value is null)
        {
            return;
        }

        _ = ChangeLinkedTaskAsync(value.Id);
    }

    private async Task ChangeLinkedTaskAsync(int? taskId)
    {
        try
        {
            ErrorMessage = null;
            var result = await _service.SetLinkedTaskAsync(taskId);
            if (result.Status == PomodoroActionStatus.Failed)
            {
                ErrorMessage = result.Message;
                await RefreshTasksAsync();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Falha ao vincular a tarefa ao Pomodoro.", ex);
            ErrorMessage = ActionFailedMessage;
        }

        UpdateFromService();
    }

    // ------------------------------------------------------------------ ações

    private const string ActionFailedMessage = "Não foi possível concluir a ação. Tente novamente.";

    [RelayCommand]
    private Task PrimaryActionAsync() => _service.Phase switch
    {
        PomodoroPhase.Idle => RunAsync(replace => _service.StartAsync(SelectedTaskOption.Id, replace)),
        PomodoroPhase.Focus or PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak => RunAsync(_ => _service.PauseAsync()),
        PomodoroPhase.FocusPaused or PomodoroPhase.BreakPaused => RunAsync(replace => _service.ResumeAsync(replace)),
        _ => RunAsync(replace => _service.StartNextPhaseAsync(replace)),
    };

    [RelayCommand]
    private async Task StopCycleAsync()
    {
        try
        {
            ErrorMessage = null;
            await _service.StopCycleAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Falha ao parar o ciclo do Pomodoro.", ex);
            ErrorMessage = ActionFailedMessage;
        }

        UpdateFromService();
    }

    [RelayCommand]
    private async Task ConfirmConflictAsync()
    {
        var action = _pendingConflictAction;
        CancelConflict();
        if (action is not null)
        {
            await RunAsync(action, replaceActive: true);
        }
    }

    [RelayCommand]
    private void CancelConflict()
    {
        _pendingConflictAction = null;
        IsConflictOpen = false;
    }

    private async Task RunAsync(Func<bool, Task<PomodoroActionResult>> action, bool replaceActive = false)
    {
        try
        {
            ErrorMessage = null;
            var result = await action(replaceActive);

            switch (result.Status)
            {
                case PomodoroActionStatus.NeedsConfirmation:
                    _pendingConflictAction = action;
                    ConflictMessage =
                        $"A tarefa \"{result.ActiveTaskName}\" está em execução.\nDeseja pausá-la e iniciar o foco?";
                    IsConflictOpen = true;
                    break;

                case PomodoroActionStatus.Failed:
                    ErrorMessage = result.Message;
                    await RefreshTasksAsync(); // a causa mais comum é a tarefa ter sido excluída
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Falha ao executar uma ação do Pomodoro.", ex);
            ErrorMessage = ActionFailedMessage;
        }

        UpdateFromService();
    }

    // ------------------------------------------------------------------ configurações

    [RelayCommand]
    private void RestoreDefaults()
    {
        ConfigError = null;
        _service.UpdateConfig(PomodoroConfig.Default);
        LoadConfigTexts();
        UpdateFromService();
    }

    partial void OnFocusMinutesTextChanged(string value) => ApplyConfigFromTexts();
    partial void OnShortBreakMinutesTextChanged(string value) => ApplyConfigFromTexts();
    partial void OnLongBreakMinutesTextChanged(string value) => ApplyConfigFromTexts();
    partial void OnFocusesUntilLongBreakTextChanged(string value) => ApplyConfigFromTexts();

    private void LoadConfigTexts()
    {
        var config = _service.Config;
        _syncingConfig = true;
        try
        {
            FocusMinutesText = config.FocusMinutes.ToString();
            ShortBreakMinutesText = config.ShortBreakMinutes.ToString();
            LongBreakMinutesText = config.LongBreakMinutes.ToString();
            FocusesUntilLongBreakText = config.FocusesUntilLongBreak.ToString();
        }
        finally
        {
            _syncingConfig = false;
        }
    }

    /// <summary>Valida os quatro campos contra os limites da Seção 70 e grava. Valor fora do
    /// limite é ajustado ao limite mais próximo; texto que não é número volta ao valor atual —
    /// nos dois casos o usuário vê o motivo em ConfigError. Não afeta a fase em andamento.</summary>
    private void ApplyConfigFromTexts()
    {
        if (_syncingConfig)
        {
            return;
        }

        var config = _service.Config;
        var errors = new List<string>();

        config.FocusMinutes = Parse(FocusMinutesText, config.FocusMinutes, PomodoroConfig.MinFocusMinutes, PomodoroConfig.MaxFocusMinutes, "Foco", errors);
        config.ShortBreakMinutes = Parse(ShortBreakMinutesText, config.ShortBreakMinutes, PomodoroConfig.MinShortBreakMinutes, PomodoroConfig.MaxShortBreakMinutes, "Pausa curta", errors);
        config.LongBreakMinutes = Parse(LongBreakMinutesText, config.LongBreakMinutes, PomodoroConfig.MinLongBreakMinutes, PomodoroConfig.MaxLongBreakMinutes, "Pausa longa", errors);
        config.FocusesUntilLongBreak = Parse(FocusesUntilLongBreakText, config.FocusesUntilLongBreak, PomodoroConfig.MinFocusesUntilLongBreak, PomodoroConfig.MaxFocusesUntilLongBreak, "Focos até a pausa longa", errors);

        _service.UpdateConfig(config);
        ConfigError = errors.Count == 0 ? null : string.Join(" ", errors);
        LoadConfigTexts();
        UpdateFromService();
    }

    private static int Parse(string text, int current, int min, int max, string label, List<string> errors)
    {
        if (!int.TryParse(text?.Trim(), out var value))
        {
            errors.Add($"{label}: informe um número entre {min} e {max}.");
            return current;
        }

        if (value < min || value > max)
        {
            errors.Add($"{label}: o valor deve ficar entre {min} e {max}.");
            return Math.Clamp(value, min, max);
        }

        return value;
    }

    // ------------------------------------------------------------------ reflexo do serviço

    private void UpdateFromService()
    {
        var state = _service.State;
        var config = _service.Config;
        var phase = state.Phase;

        PhaseLabel = phase switch
        {
            PomodoroPhase.Idle => "Pronto para focar",
            PomodoroPhase.Focus => "FOCO",
            PomodoroPhase.FocusPaused => "FOCO PAUSADO",
            PomodoroPhase.AwaitingBreak => "Foco concluído — iniciar pausa?",
            PomodoroPhase.ShortBreak => "PAUSA CURTA",
            PomodoroPhase.LongBreak => "PAUSA LONGA",
            PomodoroPhase.BreakPaused => state.PausedPhase == PomodoroPhase.LongBreak ? "PAUSA LONGA PAUSADA" : "PAUSA CURTA PAUSADA",
            PomodoroPhase.AwaitingFocus => "Pausa concluída — iniciar foco?",
            _ => string.Empty,
        };

        PrimaryActionText = phase switch
        {
            PomodoroPhase.Idle or PomodoroPhase.AwaitingFocus => "Iniciar foco",
            PomodoroPhase.Focus or PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak => "Pausar",
            PomodoroPhase.FocusPaused or PomodoroPhase.BreakPaused => "Retomar",
            PomodoroPhase.AwaitingBreak => "Iniciar pausa",
            _ => string.Empty,
        };

        IsFocusPhase = phase is PomodoroPhase.Focus or PomodoroPhase.FocusPaused;
        IsBreakPhase = phase is PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak or PomodoroPhase.BreakPaused;
        IsActive = phase != PomodoroPhase.Idle;
        ShowStopButton = phase != PomodoroPhase.Idle;

        // A tarefa só pode ser escolhida ou trocada com o foco parado (Seção 70). As
        // configurações ficam desabilitadas enquanto uma fase está rodando.
        CanChangeTask = phase != PomodoroPhase.Focus;
        AreSettingsEnabled = phase is not (PomodoroPhase.Focus or PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak);

        CycleText = $"Ciclo {_service.CycleNumber} de {config.FocusesUntilLongBreak}";
        CycleDots = new ObservableCollection<CycleDotViewModel>(BuildDots(state, config));

        // O vínculo pode ter sido removido pelo serviço (tarefa excluída, histórico limpo).
        var linked = FindOption(state.LinkedTaskId);
        if (!Equals(linked, SelectedTaskOption))
        {
            _syncingTaskSelection = true;
            try
            {
                SelectedTaskOption = linked;
            }
            finally
            {
                _syncingTaskSelection = false;
            }
        }

        RefreshTime();
    }

    private IEnumerable<CycleDotViewModel> BuildDots(PomodoroState state, PomodoroConfig config)
    {
        var total = config.FocusesUntilLongBreak;
        var completedInCycle = state.CompletedFocusCount % total;
        var afterFocus = state.Phase is PomodoroPhase.AwaitingBreak or PomodoroPhase.ShortBreak
            or PomodoroPhase.LongBreak or PomodoroPhase.BreakPaused;

        // Depois do último foco do ciclo (pausa longa) todos os pontos ficam cheios.
        var filled = afterFocus && state.CompletedFocusCount > 0 && completedInCycle == 0 ? total : completedInCycle;
        return Enumerable.Range(0, total).Select(i => new CycleDotViewModel(i < filled));
    }

    private void RefreshTime()
    {
        var state = _service.State;
        var phase = state.Phase;
        var remaining = phase == PomodoroPhase.Idle ? _service.Config.FocusDuration : _service.GetRemainingTime();
        var text = FormatTime(remaining);

        TimeDisplay = phase is PomodoroPhase.AwaitingBreak or PomodoroPhase.AwaitingFocus ? "00:00" : text;

        ChipText = phase switch
        {
            PomodoroPhase.Focus => $"Foco {text}",
            PomodoroPhase.FocusPaused => $"Foco pausado {text}",
            PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak => $"Pausa {text}",
            PomodoroPhase.BreakPaused => $"Pausa pausada {text}",
            PomodoroPhase.AwaitingBreak => "Foco concluído",
            PomodoroPhase.AwaitingFocus => "Pausa concluída",
            _ => string.Empty,
        };
    }

    /// <summary>mm:ss arredondado para cima (a contagem mostra 25:00 no início e só chega a 00:00
    /// no fim); os minutos passam de 59 sem virar horas — o foco vai até 120 min.</summary>
    public static string FormatTime(TimeSpan time)
    {
        var total = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, time.TotalSeconds)));
        return $"{(int)total.TotalMinutes:00}:{total.Seconds:00}";
    }
}
