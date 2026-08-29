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
        }
        catch (Exception)
        {
            ListErrorMessage = "Não foi possível carregar as tarefas.";
        }
    }

    private async Task RefreshTaskStatusAsync(TaskListItemViewModel item)
    {
        var status = await _timerService.GetStatusAsync(item.Id);
        item.ApplyStatus(status, _clock.UtcNow);
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

    private async Task StartAndRefreshAsync(TaskListItemViewModel item)
    {
        await _timerService.StartAsync(item.Id);

        // A tarefa anteriormente ativa (se houver) também precisa atualizar seu estado.
        foreach (var task in Tasks)
        {
            if (task.IsRunning || task.Id == item.Id)
            {
                await RefreshTaskStatusAsync(task);
            }
        }
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
        await RefreshTaskStatusAsync(item);
    }

    [RelayCommand]
    private async Task StopAsync(TaskListItemViewModel item)
    {
        await _timerService.StopAsync(item.Id);
        await RefreshTaskStatusAsync(item);
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
