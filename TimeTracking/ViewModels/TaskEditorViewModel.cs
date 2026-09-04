using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTracking.Models;
using TimeTracking.Services;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.ViewModels;

/// <summary>
/// ViewModel do painel direito de edição/criação de tarefa (Seção 20): Nome/Descrição/Tag
/// e, quando aplicável, data/hora de início e término (Seção 17).
///
/// Regra da Seção 17 para os campos de data/hora:
///   - 0 TimeEntry (tarefa nova, nunca iniciada): campos ocultos;
///   - exatamente 1 TimeEntry: editáveis diretamente (o término só é editável se a sessão
///     já estiver encerrada — não faz sentido definir um fim para uma sessão em andamento
///     por aqui; isso é papel do Pause/Stop do timer, Fase 5);
///   - mais de 1 TimeEntry: somente leitura, agregando início da primeira e término da
///     última sessão, com indicador visual de "múltiplas sessões" e tempo total.
/// </summary>
public partial class TaskEditorViewModel : ObservableObject
{
    private readonly ITaskService _taskService;
    private readonly ITagService _tagService;
    private readonly ITimerService _timerService;
    private readonly IClock _clock;
    private readonly Func<TagEditorViewModel> _tagEditorFactory;
    private int? _editingTaskId;
    private int? _singleEntryId;

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

    [ObservableProperty]
    private int _timeEntryCount;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private DateTime? _startDate;

    [ObservableProperty]
    private string _startTimeText = string.Empty;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private string _endTimeText = string.Empty;

    [ObservableProperty]
    private string? _dateTimeError;

    [ObservableProperty]
    private string _totalElapsedDisplay = "00:00:00";

    // Popup "Nova tag" (Seção 71, feedback de usuário): permite criar uma tag sem fechar o
    // editor de tarefa e ir até a tela Tags — reaproveita o mesmo TagEditorViewModel/formulário
    // usado lá, só que exibido como um popup centralizado em vez do painel lateral inteiro.
    [ObservableProperty]
    private bool _isTagEditorOpen;

    [ObservableProperty]
    private TagEditorViewModel? _tagEditor;

    public string Title => IsNew ? "Nova tarefa" : "Editar tarefa";

    public bool HasNoEntries => TimeEntryCount == 0;
    public bool HasSingleEntry => TimeEntryCount == 1;
    public bool HasMultipleEntries => TimeEntryCount > 1;
    public bool CanEditDates => HasSingleEntry || IsNew;
    public bool CanEditEndDate => CanEditDates && !IsRunning;

    /// <summary>Campos de data/hora aparecem tanto para tarefa nova (para permitir lançar
    /// uma sessão retroativa direto na criação) quanto para tarefa existente com sessão(ões)
    /// registrada(s) — só ficam ocultos para uma tarefa existente que nunca foi iniciada.</summary>
    public bool ShowDateFields => IsNew || !HasNoEntries;

    public event Action? Saved;
    public event Action? CloseRequested;

    public TaskEditorViewModel(ITaskService taskService, ITagService tagService, ITimerService timerService, IClock clock, Func<TagEditorViewModel> tagEditorFactory)
    {
        _taskService = taskService;
        _tagService = tagService;
        _timerService = timerService;
        _clock = clock;
        _tagEditorFactory = tagEditorFactory;
    }

    public async Task LoadForNewAsync()
    {
        _editingTaskId = null;
        _singleEntryId = null;
        IsNew = true;
        Name = string.Empty;
        Description = null;
        SelectedTag = null;
        NameError = null;
        ErrorMessage = null;
        DateTimeError = null;
        SetEntryState(count: 0, isRunning: false);
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
        DateTimeError = null;
        await LoadTagsAsync();
        SelectedTag = AvailableTags.FirstOrDefault(t => t.Id == task.TagId);

        var entries = await _timerService.GetEntriesForTaskAsync(taskId);
        var status = await _timerService.GetStatusAsync(taskId);
        SetEntryState(entries.Count, status.IsRunning);
        TotalElapsedDisplay = FormatElapsed(status.GetElapsed(_clock.UtcNow));

        if (entries.Count == 1)
        {
            _singleEntryId = entries[0].Id;
            SetDateFields(entries[0].StartedAt, entries[0].EndedAt);
        }
        else if (entries.Count > 1)
        {
            _singleEntryId = null;
            SetDateFields(entries[0].StartedAt, entries[^1].EndedAt);
        }
        else
        {
            _singleEntryId = null;
        }
    }

    private void SetEntryState(int count, bool isRunning)
    {
        TimeEntryCount = count;
        IsRunning = isRunning;
    }

    private void SetDateFields(DateTime startedAtUtc, DateTime? endedAtUtc)
    {
        var startLocal = DateTime.SpecifyKind(startedAtUtc, DateTimeKind.Utc).ToLocalTime();
        StartDate = startLocal.Date;
        StartTimeText = startLocal.ToString("HH:mm", CultureInfo.InvariantCulture);

        if (endedAtUtc.HasValue)
        {
            var endLocal = DateTime.SpecifyKind(endedAtUtc.Value, DateTimeKind.Utc).ToLocalTime();
            EndDate = endLocal.Date;
            EndTimeText = endLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        else
        {
            EndDate = null;
            EndTimeText = string.Empty;
        }
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

    private async Task LoadTagsAsync()
    {
        var tags = await _tagService.GetAllAsync();
        AvailableTags = new ObservableCollection<Tag>(tags);
    }

    partial void OnNameChanged(string value) => Validate();

    partial void OnIsNewChanged(bool value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanEditDates));
        OnPropertyChanged(nameof(CanEditEndDate));
        OnPropertyChanged(nameof(ShowDateFields));
    }

    partial void OnTimeEntryCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasNoEntries));
        OnPropertyChanged(nameof(HasSingleEntry));
        OnPropertyChanged(nameof(HasMultipleEntries));
        OnPropertyChanged(nameof(CanEditDates));
        OnPropertyChanged(nameof(CanEditEndDate));
        OnPropertyChanged(nameof(ShowDateFields));
    }

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(CanEditEndDate));

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

    /// <summary>Combina data (local) + texto "HH:mm" (local) em um DateTime local; retorna
    /// null se qualquer uma das partes for inválida/ausente.</summary>
    private static DateTime? CombineLocalDateAndTime(DateTime? date, string timeText)
    {
        if (!date.HasValue)
        {
            return null;
        }

        if (!TimeSpan.TryParse(timeText, CultureInfo.InvariantCulture, out var time))
        {
            return null;
        }

        return date.Value.Date + time;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!Validate())
        {
            return;
        }

        DateTimeError = null;
        DateTime? newStartedAtUtc = null;
        DateTime? newEndedAtUtc = null;

        if (IsNew)
        {
            // Data/hora são opcionais na criação: em branco = tarefa sem sessão, como hoje.
            // Só o início preenchido = sessão registrada mas pausada (duração zero), sem
            // iniciar o timer. Início + término = sessão retroativa já encerrada.
            var startTouched = StartDate.HasValue || !string.IsNullOrWhiteSpace(StartTimeText);
            var startFilled = StartDate.HasValue && !string.IsNullOrWhiteSpace(StartTimeText);
            var endTouched = EndDate.HasValue || !string.IsNullOrWhiteSpace(EndTimeText);
            var endFilled = EndDate.HasValue && !string.IsNullOrWhiteSpace(EndTimeText);

            if (startTouched && !startFilled)
            {
                DateTimeError = "Preencha a data e o horário de início juntos, ou deixe os dois em branco.";
                return;
            }

            if (!startFilled && endTouched)
            {
                DateTimeError = "Para definir um término, preencha primeiro a data/hora de início.";
                return;
            }

            if (startFilled)
            {
                var startLocal = CombineLocalDateAndTime(StartDate, StartTimeText)!.Value;
                newStartedAtUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime();

                if (endTouched && !endFilled)
                {
                    DateTimeError = "Preencha a data e o horário de término juntos, ou deixe os dois em branco.";
                    return;
                }

                if (endFilled)
                {
                    var endLocal = CombineLocalDateAndTime(EndDate, EndTimeText)!.Value;
                    newEndedAtUtc = DateTime.SpecifyKind(endLocal, DateTimeKind.Local).ToUniversalTime();

                    if (newEndedAtUtc < newStartedAtUtc)
                    {
                        DateTimeError = "O término não pode ser anterior ao início.";
                        return;
                    }
                }
                else
                {
                    newEndedAtUtc = newStartedAtUtc;
                }
            }
        }
        else if (CanEditDates)
        {
            var startLocal = CombineLocalDateAndTime(StartDate, StartTimeText);
            if (startLocal is null)
            {
                DateTimeError = "Data/hora de início inválida.";
                return;
            }

            newStartedAtUtc = DateTime.SpecifyKind(startLocal.Value, DateTimeKind.Local).ToUniversalTime();

            if (CanEditEndDate && EndDate.HasValue)
            {
                var endLocal = CombineLocalDateAndTime(EndDate, EndTimeText);
                if (endLocal is null)
                {
                    DateTimeError = "Data/hora de término inválida.";
                    return;
                }

                newEndedAtUtc = DateTime.SpecifyKind(endLocal.Value, DateTimeKind.Local).ToUniversalTime();

                if (newEndedAtUtc < newStartedAtUtc)
                {
                    DateTimeError = "O término não pode ser anterior ao início.";
                    return;
                }
            }
        }

        try
        {
            ErrorMessage = null;

            if (IsNew)
            {
                var newTask = await _taskService.CreateAsync(Name, Description, SelectedTag?.Id);

                if (newStartedAtUtc.HasValue)
                {
                    await _timerService.AddManualEntryAsync(newTask.Id, newStartedAtUtc.Value, newEndedAtUtc!.Value);
                }
            }
            else
            {
                await _taskService.UpdateAsync(_editingTaskId!.Value, Name, Description, SelectedTag?.Id);

                if (CanEditDates && _singleEntryId.HasValue && newStartedAtUtc.HasValue)
                {
                    await _timerService.UpdateEntryTimestampsAsync(_singleEntryId.Value, newStartedAtUtc.Value, newEndedAtUtc);
                }
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

    [RelayCommand]
    private async Task OpenNewTagAsync()
    {
        var editor = _tagEditorFactory();
        editor.Saved += OnTagEditorSaved;
        editor.CloseRequested += OnTagEditorClosed;
        await editor.LoadForNewAsync();
        TagEditor = editor;
        IsTagEditorOpen = true;
    }

    private async void OnTagEditorSaved()
    {
        var createdTag = TagEditor?.CreatedTag;
        CloseTagEditor();
        await LoadTagsAsync();

        if (createdTag is not null)
        {
            SelectedTag = AvailableTags.FirstOrDefault(t => t.Id == createdTag.Id);
        }
    }

    private void OnTagEditorClosed() => CloseTagEditor();

    [RelayCommand]
    private void CloseTagEditor()
    {
        if (TagEditor is not null)
        {
            TagEditor.Saved -= OnTagEditorSaved;
            TagEditor.CloseRequested -= OnTagEditorClosed;
        }

        IsTagEditorOpen = false;
        TagEditor = null;
    }
}
