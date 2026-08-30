using TimeTracking.Models;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Services;

public interface ITagService
{
    Task<List<Tag>> GetAllAsync();
    Task<Tag?> GetByIdAsync(int id);
    Task<Tag> CreateAsync(string name, string? description, string color);
    Task UpdateAsync(int id, string name, string? description, string color);

    /// <summary>Exclui a tag. As Task associadas são preservadas — o banco (SetNull,
    /// Seção 9) zera Task.TagId automaticamente, sem excluir nenhuma tarefa.</summary>
    Task DeleteAsync(int id);
}
