using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTracking.Services;
using DomainTask = TimeTracking.Models.Task;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.ViewModels;

public partial class TimeTrackingViewModel : ObservableObject
{
    private readonly ITaskService _taskService;
    private readonly ITimerService _timerService;
    private readonly IClock _clock;
    private readonly Func<TaskEditorViewModel> _editorFactory;
    private readonly DispatcherTimer _tickTimer;

    [ObservableProperty]
    private ObservableCollection<TaskListItemViewModel> _tasks = new();

    [ObservableProperty]
    private ObservableCollection<DayGroupViewModel> _dayGroups = new();

    [ObservableProperty]
    private bool _isEditorOpen;

    [ObservableProperty]
    private TaskEditorViewModel? _editor;

    [ObservableProperty]
    private DomainTask? _pendingDelete;

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [ObservableProperty]
    private string? _listErrorMessage;

    // Conflito de timer (Seção 15): tentativa de iniciar uma tarefa enquanto outra roda.
    [ObservableProperty]
    private bool _isPlayConflictOpen;

    [ObservableProperty]
    private string? _conflictActiveTaskName;

    private TaskListItemViewModel? _pendingPlayTarget;

    public string DeleteConfirmMessage =>
        $"Tem certeza que deseja excluir \"{PendingDelete?.Name}\"? Todo o tempo registrado para ela também será removido.";

    public string PlayConflictMessage =>
        $"A tarefa \"{ConflictActiveTaskName}\" está em execução.\nDeseja pausá-la e iniciar \"{_pendingPlayTarget?.Name}\"?";

    public TimeTrackingViewModel(ITaskService taskService, ITimerService timerService, IClock clock, Func<TaskEditorViewModel> editorFactory)
    {
        _taskService = taskService;
        _timerService = timerService;
        _clock = clock;
        _editorFactory = editorFactory;

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) => OnTick();
        _tickTimer.Start();

        _ = LoadTasksAsync();
    }

    private void OnTick()
    {
        var now = _clock.UtcNow;
        foreach (var item in Tasks)
        {
            item.Tick(now);
        }

        // Seção 68: o total do dia com timer ativo precisa acompanhar o tick, sempre
        // recalculado em memória (Seção 43) — nunca lido do banco a cada segundo.
        var runningGroup = DayGroups.FirstOrDefault(g => g.HasRunningTask);
        if (runningGroup is not null)
        {
            var domainTasks = Tasks.Select(t => t.Task).ToList();
            runningGroup.TotalDuration = TaskDayGroupBuilder.SumEntriesForDate(domainTasks, runningGroup.Date, now);
        }
    }

    [RelayCommand]
    private async Task LoadTasksAsync()
    {
        try
        {
            ListErrorMessage = null;
            var tasks = await _taskService.GetAllAsync();
            var now = _clock.UtcNow;

            var items = new List<TaskListItemViewModel>();
            foreach (var task in tasks)
            {
                var item = new TaskListItemViewModel(task);
                var status = await _timerService.GetStatusAsync(task.Id);
                item.ApplyStatus(status, now);
                items.Add(item);
            }

            Tasks = new ObservableCollection<TaskListItemViewModel>(items);
            RebuildDayGroups(now);
        }
        catch (Exception)
        {
            ListErrorMessage = "Não foi possível carregar as tarefas.";
        }
    }

    /// <summary>Reconstrói os grupos de data (Seção 68) preservando o estado expandido/
    /// recolhido de cada data já existente — só o padrão (Hoje aberto, demais fechados)
    /// se aplica a datas novas que ainda não haviam aparecido nesta sessão.</summary>
    private void RebuildDayGroups(DateTime nowUtc)
    {
        var previousExpanded = DayGroups.ToDictionary(g => g.Date, g => g.IsExpanded);

        var domainTasks = Tasks.Select(t => t.Task).ToList();
        var groupData = TaskDayGroupBuilder.Build(domainTasks, nowUtc);
        var itemsById = Tasks.ToDictionary(t => t.Id);

        var groups = new ObservableCollection<DayGroupViewModel>();
        foreach (var data in groupData)
        {
            var groupItems = data.Tasks.Select(t => itemsById[t.Id]);
            var isExpanded = previousExpanded.TryGetValue(data.Date, out var prior) ? prior : data.IsToday;

            groups.Add(new DayGroupViewModel(
                data.Date,
                data.IsToday,
                groupItems,
                isExpanded,
                data.TotalDuration,
                PlayCommand,
                PauseCommand,
                StopCommand,
                SelectTaskCommand,
                RequestDeleteCommand));
        }

        DayGroups = groups;
        UpdateForcedExpansion();
    }

    /// <summary>Atualiza HasRunningTask de cada grupo — o próprio DayGroupViewModel força
    /// IsExpanded quando ele passa a ter uma tarefa em execução (Seção 68, item 7).</summary>
    private void UpdateForcedExpansion()
    {
        foreach (var group in DayGroups)
        {
            group.HasRunningTask = group.Tasks.Any(t => t.IsRunning);
        }
    }

    [RelayCommand]
    private async Task PlayAsync(TaskListItemViewModel item)
    {
        var activeTask = await _timerService.GetActiveTaskAsync();

        if (activeTask is not null && activeTask.Id != item.Id)
        {
            _pendingPlayTarget = item;
            ConflictActiveTaskName = activeTask.Name;
            OnPropertyChanged(nameof(PlayConflictMessage));
            IsPlayConflictOpen = true;
            return;
        }

        await StartAndRefreshAsync(item);
    }

    /// <summary>Recarrega tudo do banco após qualquer ação do timer (Start/Pause/Stop).
    /// Necessário porque os DomainTask em memória (Tasks) são snapshots desconectados
    /// (AsNoTracking, Seção 65) — só um reload garante que TimeEntries reflita a sessão
    /// recém aberta/encerrada, o que por sua vez é a fonte do total do dia (Seção 68).
    /// Sem isso, o total do dia ficava travado no valor de antes da ação até a próxima
    /// tarefa ser criada (o que forçava um LoadTasksAsync por outro caminho).</summary>
    private async Task StartAndRefreshAsync(TaskListItemViewModel item)
    {
        await _timerService.StartAsync(item.Id);
        await LoadTasksAsync();
    }

    [RelayCommand]
    private async Task ConfirmPlayConflictAsync()
    {
        if (_pendingPlayTarget is not null)
        {
            var target = _pendingPlayTarget;
            CancelPlayConflict();
            await StartAndRefreshAsync(target);
        }
    }

    [RelayCommand]
    private void CancelPlayConflict()
    {
        _pendingPlayTarget = null;
        ConflictActiveTaskName = null;
        IsPlayConflictOpen = false;
    }

    [RelayCommand]
    private async Task PauseAsync(TaskListItemViewModel item)
    {
        await _timerService.PauseAsync(item.Id);
        await LoadTasksAsync();
    }

    [RelayCommand]
    private async Task StopAsync(TaskListItemViewModel item)
    {
        await _timerService.StopAsync(item.Id);
        await LoadTasksAsync();
    }

    /// <summary>Cria uma tarefa sem exigir nenhum campo (nome padrão "Nova tarefa") e já
    /// inicia o timer nela. StartAsync pausa qualquer outra tarefa ativa automaticamente
    /// (Seção 15) — sem diálogo de confirmação aqui, pois o próprio clique já é a intenção
    /// explícita do usuário. Não abre o editor: o usuário cria rápido e edita depois.</summary>
    [RelayCommand]
    private async Task StartQuickTaskAsync()
    {
        try
        {
            ListErrorMessage = null;
            var newTask = await _taskService.CreateAsync("Nova tarefa", null, null);
            await _timerService.StartAsync(newTask.Id);
            await LoadTasksAsync();
        }
        catch (Exception)
        {
            ListErrorMessage = "Não foi possível iniciar a tarefa.";
        }
    }

    [RelayCommand]
    private async Task OpenNewTaskAsync()
    {
        var editor = _editorFactory();
        AttachEditorHandlers(editor);
        await editor.LoadForNewAsync();
        Editor = editor;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private async Task SelectTaskAsync(TaskListItemViewModel item)
    {
        var editor = _editorFactory();
        AttachEditorHandlers(editor);
        await editor.LoadForEditAsync(item.Id);
        Editor = editor;
        IsEditorOpen = true;
    }

    private void AttachEditorHandlers(TaskEditorViewModel editor)
    {
        editor.Saved += OnEditorSaved;
        editor.CloseRequested += OnEditorClosed;
    }

    private void DetachEditorHandlers(TaskEditorViewModel editor)
    {
        editor.Saved -= OnEditorSaved;
        editor.CloseRequested -= OnEditorClosed;
    }

    private async void OnEditorSaved()
    {
        CloseEditor();
        await LoadTasksAsync();
    }

    private void OnEditorClosed() => CloseEditor();

    [RelayCommand]
    private void CloseEditor()
    {
        if (Editor is not null)
        {
            DetachEditorHandlers(Editor);
        }

        IsEditorOpen = false;
        Editor = null;
    }

    [RelayCommand]
    private void RequestDelete(TaskListItemViewModel item)
    {
        PendingDelete = item.Task;
        OnPropertyChanged(nameof(DeleteConfirmMessage));
        IsDeleteConfirmOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (PendingDelete is not null)
        {
            try
            {
                await _taskService.DeleteAsync(PendingDelete.Id);
                await LoadTasksAsync();
            }
            catch (Exception)
            {
                ListErrorMessage = "Não foi possível excluir a tarefa. Tente novamente.";
            }
        }

        CancelDelete();
    }

    [RelayCommand]
    private void CancelDelete()
    {
        PendingDelete = null;
        IsDeleteConfirmOpen = false;
    }
}
