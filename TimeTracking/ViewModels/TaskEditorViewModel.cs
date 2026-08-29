using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTracking.Models;
using TimeTracking.Services;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.ViewModels;

/// <summary>
/// ViewModel do painel direito de edição/criação de tarefa (Seção 20).
/// Nesta fase (Fase 4) cobre apenas Nome/Descrição/Tag — os campos de data/hora de
/// início e término (Seção 17) dependem de TimeEntry, que só existe a partir da Fase 5
/// (Timer), e serão adicionados na Fase 6 (Painel de Edição).
/// </summary>
public partial class TaskEditorViewModel : ObservableObject
{
    private readonly ITaskService _taskService;
    private readonly ITagService _tagService;
    private int? _editingTaskId;

    [ObservableProperty]
    private bool _isNew;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _nameError;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private ObservableCollection<Tag> _availableTags = new();

    [ObservableProperty]
    private Tag? _selectedTag;

    [ObservableProperty]
    private string? _errorMessage;

    public string Title => IsNew ? "Nova tarefa" : "Editar tarefa";

    public event Action? Saved;
    public event Action? CloseRequested;

    public TaskEditorViewModel(ITaskService taskService, ITagService tagService)
    {
        _taskService = taskService;
        _tagService = tagService;
    }

    public async Task LoadForNewAsync()
    {
        _editingTaskId = null;
        IsNew = true;
        Name = string.Empty;
        Description = null;
        SelectedTag = null;
        NameError = null;
        ErrorMessage = null;
        await LoadTagsAsync();
    }

    public async Task LoadForEditAsync(int taskId)
    {
        var task = await _taskService.GetByIdAsync(taskId)
            ?? throw new InvalidOperationException($"Tarefa {taskId} não encontrada.");

        _editingTaskId = taskId;
        IsNew = false;
        Name = task.Name;
        Description = task.Description;
        NameError = null;
        ErrorMessage = null;
        await LoadTagsAsync();
        SelectedTag = AvailableTags.FirstOrDefault(t => t.Id == task.TagId);
    }

    private async Task LoadTagsAsync()
    {
        var tags = await _tagService.GetAllAsync();
        AvailableTags = new ObservableCollection<Tag>(tags);
    }

    partial void OnNameChanged(string value) => Validate();

    partial void OnIsNewChanged(bool value) => OnPropertyChanged(nameof(Title));

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            NameError = "Nome é obrigatório.";
        }
        else if (Name.Trim().Length > 200)
        {
            NameError = "Nome deve ter no máximo 200 caracteres.";
        }
        else
        {
            NameError = null;
        }

        SaveCommand.NotifyCanExecuteChanged();
        return NameError is null;
    }

    private bool CanSave() => string.IsNullOrEmpty(NameError);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!Validate())
        {
            return;
        }

        try
        {
            ErrorMessage = null;

            if (IsNew)
            {
                await _taskService.CreateAsync(Name, Description, SelectedTag?.Id);
            }
            else
            {
                await _taskService.UpdateAsync(_editingTaskId!.Value, Name, Description, SelectedTag?.Id);
            }

            Saved?.Invoke();
        }
        catch (Exception)
        {
            ErrorMessage = "Não foi possível salvar a tarefa. Verifique os dados e tente novamente.";
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    [RelayCommand]
    private void ClearTag() => SelectedTag = null;
}
