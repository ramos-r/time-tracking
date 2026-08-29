using DomainTask = TimeTracking.Models.Task;

namespace TimeTracking.Services;

public interface ITaskService
{
    Task<List<DomainTask>> GetAllAsync();
    Task<DomainTask?> GetByIdAsync(int id);
    Task<DomainTask> CreateAsync(string name, string? description, int? tagId);
    Task UpdateAsync(int id, string name, string? description, int? tagId);
    Task DeleteAsync(int id);
}
