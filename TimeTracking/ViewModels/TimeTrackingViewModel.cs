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

    // Seleção múltipla para exclusão em lote — adicional ao menu "..."/clique direito
    // individual (que continuam intactos e funcionando independente deste modo).
    [ObservableProperty]
    private bool _isSelectionMode;

    [ObservableProperty]
    private bool _isBulkDeleteConfirmOpen;

    private readonly HashSet<int> _selectedTaskIds = new();

    // Grupo (dia) cujo menu de clique direito "Selecionar" foi usado por último — é o
    // escopo do botão "Selecionar todas" do header (Seção 68 — ajuste): nunca todos os
    // dias de uma vez, só o dia que o usuário escolheu.
    private List<TaskListItemViewModel> _activeSelectionGroupTasks = new();

    public bool HasSelection => _selectedTaskIds.Count > 0;

    public string SelectedCountDisplay => _selectedTaskIds.Count switch
    {
        0 => "Nenhuma tarefa selecionada",
        1 => "1 tarefa selecionada",
        _ => $"{_selectedTaskIds.Count} tarefas selecionadas"
    };

    public string BulkDeleteConfirmMessage =>
        $"Tem certeza que deseja excluir {_selectedTaskIds.Count} {(_selectedTaskIds.Count == 1 ? "tarefa" : "tarefas")}? " +
        "Todo o tempo registrado para elas também será removido.";

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
                item.IsSelected = _selectedTaskIds.Contains(task.Id);
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
                StopCommand,
                SelectTaskCommand,
                RequestDeleteCommand,
                SelectGroupCommand,
                SelectSingleTaskCommand,
                IsSelectionMode));
        }

        DayGroups = groups;
        UpdateForcedExpansion();
    }

    // Alternar o modo de seleção não recarrega o banco — só precisa reconstruir os grupos
    // (operação local, barata) para que cada DayGroupViewModel novo nasça com o
    // IsSelectionMode atual (ver comentário na própria propriedade).
    partial void OnIsSelectionModeChanged(bool value) => RebuildDayGroups(_clock.UtcNow);

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
        // No modo de seleção múltipla, clicar no card alterna a seleção em vez de abrir o
        // editor — reaproveita o mesmo comando/gesto já ligado ao clique do TaskCard, sem
        // precisar de nenhuma mudança no XAML.
        if (IsSelectionMode)
        {
            ToggleTaskSelection(item);
            return;
        }

        var editor = _editorFactory();
        AttachEditorHandlers(editor);
        await editor.LoadForEditAsync(item.Id);
        Editor = editor;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void CancelSelectionMode()
    {
        IsSelectionMode = false;
        ClearSelection();
    }

    /// <summary>Menu de clique direito no cabeçalho do grupo, ex.: "Hoje, 02/09". Só revela as
    /// caixinhas de seleção (modo de seleção) e marca este grupo como o alvo do botão
    /// "Selecionar todas" do header — não marca nenhuma tarefa sozinho, o usuário escolhe
    /// manualmente ou usa "Selecionar todas" em seguida.</summary>
    [RelayCommand]
    private void SelectGroup(DayGroupViewModel group)
    {
        IsSelectionMode = true;
        _activeSelectionGroupTasks = group.Tasks.ToList();
    }

    /// <summary>Opção "Selecionar" no menu "..."/clique direito de uma tarefa individual —
    /// ativa o modo de seleção, marca só esta tarefa, e define o grupo (dia) dela como o
    /// alvo do "Selecionar todas" do header, seguindo a mesma regra de escopo por dia já
    /// usada no clique direito do cabeçalho do grupo.</summary>
    [RelayCommand]
    private void SelectSingleTask(TaskListItemViewModel item)
    {
        var group = DayGroups.FirstOrDefault(g => g.Tasks.Contains(item));
        if (group is not null)
        {
            _activeSelectionGroupTasks = group.Tasks.ToList();
        }

        IsSelectionMode = true;
        item.IsSelected = true;
        _selectedTaskIds.Add(item.Id);

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedCountDisplay));
    }

    /// <summary>Botão "Selecionar todas" do header (ao lado de "Excluir selecionadas") —
    /// marca todas as tarefas do grupo escolhido por último via clique direito, nunca de
    /// todos os dias de uma vez. Não afeta seleções já feitas em outros grupos.</summary>
    [RelayCommand]
    private void SelectAllInActiveGroup()
    {
        foreach (var task in _activeSelectionGroupTasks)
        {
            task.IsSelected = true;
            _selectedTaskIds.Add(task.Id);
        }

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedCountDisplay));
    }

    private void ToggleTaskSelection(TaskListItemViewModel item)
    {
        item.IsSelected = !item.IsSelected;
        if (item.IsSelected)
        {
            _selectedTaskIds.Add(item.Id);
        }
        else
        {
            _selectedTaskIds.Remove(item.Id);
        }

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedCountDisplay));
    }

    /// <summary>Botão "Desmarcar todas" do header — só habilitado quando há pelo menos uma
    /// tarefa selecionada (Style="{StaticResource ...}" IsEnabled bound a HasSelection).
    /// Desmarca tudo mas permanece no modo de seleção (diferente de "Cancelar", que sai do
    /// modo) e mantém o grupo ativo de "Selecionar todas" intacto.</summary>
    [RelayCommand]
    private void DeselectAll()
    {
        _selectedTaskIds.Clear();
        foreach (var task in Tasks)
        {
            task.IsSelected = false;
        }

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedCountDisplay));
    }

    private void ClearSelection()
    {
        DeselectAll();
        _activeSelectionGroupTasks = new List<TaskListItemViewModel>();
    }

    [RelayCommand]
    private void RequestBulkDelete()
    {
        if (!HasSelection)
        {
            return;
        }

        OnPropertyChanged(nameof(BulkDeleteConfirmMessage));
        IsBulkDeleteConfirmOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmBulkDeleteAsync()
    {
        var idsToDelete = _selectedTaskIds.ToList();
        IsBulkDeleteConfirmOpen = false;

        try
        {
            foreach (var id in idsToDelete)
            {
                await _taskService.DeleteAsync(id);
            }

            IsSelectionMode = false;
            ClearSelection();
            await LoadTasksAsync();
        }
        catch (Exception)
        {
            ListErrorMessage = "Não foi possível excluir as tarefas selecionadas. Tente novamente.";
        }
    }

    [RelayCommand]
    private void CancelBulkDelete()
    {
        IsBulkDeleteConfirmOpen = false;
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
