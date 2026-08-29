using DomainTask = TimeTracking.Models.Task;

namespace TimeTracking.Repositories;

public interface ITaskRepository
{
    Task<List<DomainTask>> GetAllAsync();
    Task<DomainTask?> GetByIdAsync(int id);
    Task AddAsync(DomainTask task);
    Task UpdateAsync(DomainTask task);
    Task DeleteAsync(int id);
}
