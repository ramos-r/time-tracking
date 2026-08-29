using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTracking.Services;
using DomainTask = TimeTracking.Models.Task;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.ViewModels;

public partial class TimeTrackingViewModel : ObservableObject
{
    private readonly ITaskService _taskService;
    private readonly Func<TaskEditorViewModel> _editorFactory;

    [ObservableProperty]
    private ObservableCollection<DomainTask> _tasks = new();

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

    public string DeleteConfirmMessage =>
        $"Tem certeza que deseja excluir \"{PendingDelete?.Name}\"? Todo o tempo registrado para ela também será removido.";

    public TimeTrackingViewModel(ITaskService taskService, Func<TaskEditorViewModel> editorFactory)
    {
        _taskService = taskService;
        _editorFactory = editorFactory;
        _ = LoadTasksAsync();
    }

    [RelayCommand]
    private async Task LoadTasksAsync()
    {
        try
        {
            ListErrorMessage = null;
            var tasks = await _taskService.GetAllAsync();
            Tasks = new ObservableCollection<DomainTask>(tasks);
        }
        catch (Exception)
        {
            ListErrorMessage = "Não foi possível carregar as tarefas.";
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
    private async Task SelectTaskAsync(DomainTask task)
    {
        var editor = _editorFactory();
        AttachEditorHandlers(editor);
        await editor.LoadForEditAsync(task.Id);
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
    private void RequestDelete(DomainTask task)
    {
        PendingDelete = task;
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
