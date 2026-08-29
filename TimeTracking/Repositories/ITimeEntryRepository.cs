using TimeTracking.Models;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Repositories;

public interface ITimeEntryRepository
{
    Task<List<TimeEntry>> GetAllForTaskAsync(int taskId);

    /// <summary>Retorna a TimeEntry aberta (EndedAt nulo), se existir alguma na base inteira.</summary>
    Task<TimeEntry?> GetOpenEntryAsync();

    Task AddAsync(TimeEntry entry);
    Task UpdateAsync(TimeEntry entry);
}
