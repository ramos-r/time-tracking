using TimeTracking.Models;
using TimeTracking.Repositories;
using DomainTask = TimeTracking.Models.Task;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Services;

/// <summary>
/// Centraliza as regras do timer (Seção 34). O estado (Running/Paused/Stopped) nunca é
/// persistido diretamente — é sempre derivado das TimeEntry existentes:
///   - Running: existe uma TimeEntry com EndedAt nulo para a tarefa;
///   - Paused: a tarefa tem TimeEntry(s), nenhuma aberta;
///   - Stopped: a tarefa não tem nenhuma TimeEntry.
/// A restrição de "apenas uma tarefa ativa" (Seção 15) é global e também garantida no
/// banco por um índice único (Seção 9) — este serviço é a primeira linha de defesa.
/// </summary>
public class TimerService : ITimerService
{
    private readonly ITimeEntryRepository _timeEntryRepository;
    private readonly ITaskRepository _taskRepository;
    private readonly IClock _clock;

    public TimerService(ITimeEntryRepository timeEntryRepository, ITaskRepository taskRepository, IClock clock)
    {
        _timeEntryRepository = timeEntryRepository;
        _taskRepository = taskRepository;
        _clock = clock;
    }

    public async Task<TimerStatus> GetStatusAsync(int taskId)
    {
        var entries = await _timeEntryRepository.GetAllForTaskAsync(taskId);

        var closedTotal = TimeSpan.Zero;
        DateTime? runningStartedAt = null;

        foreach (var entry in entries)
        {
            if (entry.EndedAt.HasValue)
            {
                closedTotal += entry.EndedAt.Value - entry.StartedAt;
            }
            else
            {
                runningStartedAt = entry.StartedAt;
            }
        }

        return new TimerStatus(runningStartedAt.HasValue, closedTotal, runningStartedAt);
    }

    public async Task<DomainTask?> GetActiveTaskAsync()
    {
        var openEntry = await _timeEntryRepository.GetOpenEntryAsync();
        return openEntry is null ? null : await _taskRepository.GetByIdAsync(openEntry.TaskId);
    }

    public async Task StartAsync(int taskId)
    {
        var openEntry = await _timeEntryRepository.GetOpenEntryAsync();

        if (openEntry is not null)
        {
            if (openEntry.TaskId == taskId)
            {
                return; // já é a tarefa ativa — nada a fazer
            }

            openEntry.EndedAt = _clock.UtcNow;
            await _timeEntryRepository.UpdateAsync(openEntry);
        }

        await _timeEntryRepository.AddAsync(new TimeEntry
        {
            TaskId = taskId,
            StartedAt = _clock.UtcNow,
            EndedAt = null
        });
    }

    public async Task PauseAsync(int taskId)
    {
        var openEntry = await _timeEntryRepository.GetOpenEntryAsync();
        if (openEntry is not null && openEntry.TaskId == taskId)
        {
            openEntry.EndedAt = _clock.UtcNow;
            await _timeEntryRepository.UpdateAsync(openEntry);
        }
    }

    public Task StopAsync(int taskId) => PauseAsync(taskId);
}
