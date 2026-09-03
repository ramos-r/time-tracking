using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTracking.Services;

namespace TimeTracking.ViewModels;

/// <summary>
/// Grupo retrátil de tarefas por data (Seção 68). Os comandos de timer/edição/exclusão são
/// repassados da TimeTrackingViewModel (não implementados aqui) para que components:TaskCard
/// continue funcionando sem alteração — ele resolve esses comandos a partir do DataContext do
/// ItemsControl mais próximo, que agora é este grupo, não mais a ViewModel da tela inteira.
/// </summary>
public partial class DayGroupViewModel : ObservableObject
{
    public DateTime Date { get; }
    public bool IsToday { get; }
    public string DisplayLabel { get; }

    public ObservableCollection<TaskListItemViewModel> Tasks { get; }

    public ICommand PlayCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SelectTaskCommand { get; }
    public ICommand RequestDeleteCommand { get; }

    /// <summary>Seleciona todas as tarefas deste grupo (menu de clique direito no
    /// cabeçalho) — ver comentário na implementação, TimeTrackingViewModel.SelectGroup.</summary>
    public ICommand SelectGroupCommand { get; }

    /// <summary>Repassado da TimeTrackingViewModel (seleção múltipla para exclusão em lote).
    /// Não precisa de notificação própria: alternar o modo de seleção reconstrói os grupos
    /// (RebuildDayGroups), então cada DayGroupViewModel novo já nasce com o valor atual.</summary>
    public bool IsSelectionMode { get; }

    [ObservableProperty]
    private TimeSpan _totalDuration;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _hasRunningTask;

    public string TotalDurationDisplay =>
        $"{(int)TotalDuration.TotalHours:D2}:{TotalDuration.Minutes:D2}:{TotalDuration.Seconds:D2}";

    public DayGroupViewModel(
        DateTime date,
        bool isToday,
        IEnumerable<TaskListItemViewModel> tasks,
        bool isExpanded,
        TimeSpan totalDuration,
        ICommand playCommand,
        ICommand stopCommand,
        ICommand selectTaskCommand,
        ICommand requestDeleteCommand,
        ICommand selectGroupCommand,
        bool isSelectionMode)
    {
        Date = date;
        IsToday = isToday;
        DisplayLabel = RelativeDateFormatter.Format(date, DateTime.Now);
        Tasks = new ObservableCollection<TaskListItemViewModel>(tasks);
        _isExpanded = isExpanded;
        _totalDuration = totalDuration;
        PlayCommand = playCommand;
        StopCommand = stopCommand;
        SelectTaskCommand = selectTaskCommand;
        RequestDeleteCommand = requestDeleteCommand;
        SelectGroupCommand = selectGroupCommand;
        IsSelectionMode = isSelectionMode;
    }

    partial void OnTotalDurationChanged(TimeSpan value) => OnPropertyChanged(nameof(TotalDurationDisplay));

    // Seção 68, item 7: assim que uma tarefa do grupo passa a rodar, o grupo se expande
    // sozinho — encapsulado aqui (não na ViewModel da tela) para que continue valendo
    // não importa de onde HasRunningTask seja atualizado.
    partial void OnHasRunningTaskChanged(bool value)
    {
        if (value)
        {
            IsExpanded = true;
        }
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        // Seção 68, item 7: com timer rodando em uma tarefa do grupo, o recolhimento manual
        // é ignorado para não esconder acidentalmente a tarefa em andamento.
        if (HasRunningTask)
        {
            return;
        }

        IsExpanded = !IsExpanded;
    }
}
