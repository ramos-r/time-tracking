using CommunityToolkit.Mvvm.ComponentModel;
using TimeTracking.Models;
using TimeTracking.Services;
using DomainTask = TimeTracking.Models.Task;

namespace TimeTracking.ViewModels;

/// <summary>
/// Envolve uma Task de domínio para exibição na lista, agregando o estado do timer
/// (Seção 10) calculado pelo TimerService. O tick de 1s (Seção 43) atualiza apenas o
/// item em execução, recalculando em memória — sem consultar o banco a cada tick.
/// </summary>
public partial class TaskListItemViewModel : ObservableObject
{
    public DomainTask Task { get; }

    public int Id => Task.Id;
    public string Name => Task.Name;
    public string? Description => Task.Description;
    public Tag? Tag => Task.Tag;

    [ObservableProperty]
    private bool _isRunning;

    // Distingue "nunca foi iniciada" (0 TimeEntry) de "pausada" (já teve pelo menos uma
    // sessão) — usado para trocar o texto do botão/menu entre "Iniciar" e "Retomar"
    // (Seção 71, feedback de usuário: tarefa recém-criada não pode dizer "Retomar").
    [ObservableProperty]
    private bool _hasStarted;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private TimeSpan _elapsed;

    public string ElapsedDisplay =>
        $"{(int)Elapsed.TotalHours:D2}:{Elapsed.Minutes:D2}:{Elapsed.Seconds:D2}";

    public TimerStatus Status { get; private set; } = new(false, TimeSpan.Zero, null);

    public TaskListItemViewModel(DomainTask task)
    {
        Task = task;
    }

    public void ApplyStatus(TimerStatus status, DateTime now)
    {
        Status = status;
        IsRunning = status.IsRunning;
        HasStarted = status.HasEntries;
        Elapsed = status.GetElapsed(now);
    }

    public void Tick(DateTime now)
    {
        if (IsRunning)
        {
            Elapsed = Status.GetElapsed(now);
        }
    }

    partial void OnElapsedChanged(TimeSpan value) => OnPropertyChanged(nameof(ElapsedDisplay));
}
