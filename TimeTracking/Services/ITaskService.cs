using DomainTask = TimeTracking.Models.Task;

namespace TimeTracking.Services;

public interface ITaskService
{
    Task<List<DomainTask>> GetAllAsync();
    Task<DomainTask?> GetByIdAsync(int id);
    Task<DomainTask> CreateAsync(string name, string? description, int? tagId);
    Task UpdateAsync(int id, string name, string? description, int? tagId);
    Task DeleteAsync(int id);

    /// <summary>Limpar histórico (Seção 27): remove todas as Task e TimeEntry, preservando as Tag.</summary>
    Task ClearHistoryAsync();

    /// <summary>Disparado após o histórico ser limpo. Existe porque "Limpar histórico" fica em
    /// Settings, uma tela/ViewModel diferente de Time Tracking — sem esse evento, a lista de
    /// tarefas já carregada em memória lá (Seção 65, AsNoTracking) ficaria desatualizada até o
    /// usuário reiniciar o app ou trocar de tela e voltar.</summary>
    event Action? HistoryCleared;
}
