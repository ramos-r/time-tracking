using DomainTask = TimeTracking.Models.Task;

namespace TimeTracking.Repositories;

public interface ITaskRepository
{
    Task<List<DomainTask>> GetAllAsync();
    Task<DomainTask?> GetByIdAsync(int id);
    Task AddAsync(DomainTask task);
    Task UpdateAsync(DomainTask task);
    Task DeleteAsync(int id);

    /// <summary>Remove todas as Task (Seção 27 — Limpar histórico). As TimeEntry são
    /// removidas junto via cascade delete no banco; as Tag não são afetadas.</summary>
    Task DeleteAllAsync();
}
