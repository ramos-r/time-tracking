using TimeTracking.Models;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Repositories;

public interface ITagRepository
{
    Task<List<Tag>> GetAllAsync();
    Task<Tag?> GetByIdAsync(int id);
    Task AddAsync(Tag tag);
    Task UpdateAsync(Tag tag);
    Task DeleteAsync(int id);
}
