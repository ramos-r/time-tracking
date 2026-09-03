using TimeTracking.Repositories;
using DomainTask = TimeTracking.Models.Task;

namespace TimeTracking.Services;

public class TaskService : ITaskService
{
    private readonly ITaskRepository _taskRepository;

    public TaskService(ITaskRepository taskRepository)
    {
        _taskRepository = taskRepository;
    }

    public Task<List<DomainTask>> GetAllAsync() => _taskRepository.GetAllAsync();

    public Task<DomainTask?> GetByIdAsync(int id) => _taskRepository.GetByIdAsync(id);

    public async Task<DomainTask> CreateAsync(string name, string? description, int? tagId)
    {
        ValidateName(name);

        var now = DateTime.UtcNow;
        var task = new DomainTask
        {
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            TagId = tagId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _taskRepository.AddAsync(task);
        return task;
    }

    public async Task UpdateAsync(int id, string name, string? description, int? tagId)
    {
        ValidateName(name);

        var task = await _taskRepository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Tarefa {id} não encontrada.");

        task.Name = name.Trim();
        task.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        task.TagId = tagId;
        task.UpdatedAt = DateTime.UtcNow;

        await _taskRepository.UpdateAsync(task);
    }

    public Task DeleteAsync(int id) => _taskRepository.DeleteAsync(id);

    public event Action? HistoryCleared;

    public async Task ClearHistoryAsync()
    {
        await _taskRepository.DeleteAllAsync();
        HistoryCleared?.Invoke();
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nome da tarefa é obrigatório.", nameof(name));

        if (name.Trim().Length > 200)
            throw new ArgumentException("Nome da tarefa deve ter no máximo 200 caracteres.", nameof(name));
    }
}
