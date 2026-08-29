using Microsoft.EntityFrameworkCore;
using TimeTracking.Data;
using DomainTask = TimeTracking.Models.Task;

namespace TimeTracking.Repositories;

public class TaskRepository : ITaskRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public TaskRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<DomainTask>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tasks
            .AsNoTracking()
            .Include(t => t.Tag)
            .Include(t => t.TimeEntries)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<DomainTask?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tasks
            .Include(t => t.Tag)
            .Include(t => t.TimeEntries)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task AddAsync(DomainTask task)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Tasks.Add(task);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(DomainTask task)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Copia apenas os escalares para uma entidade rastreada nesta conexão — evitar
        // context.Tasks.Update(task) diretamente, pois "task" pode carregar navegações
        // (Tag, TimeEntries) de um contexto já descartado, o que faria o EF Core tentar
        // anexar/atualizar também essas entidades relacionadas sem necessidade.
        var existing = await context.Tasks.FindAsync(task.Id)
            ?? throw new InvalidOperationException($"Task {task.Id} não encontrada.");

        existing.Name = task.Name;
        existing.Description = task.Description;
        existing.TagId = task.TagId;
        existing.UpdatedAt = task.UpdatedAt;

        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Tasks.FindAsync(id);
        if (entity is not null)
        {
            context.Tasks.Remove(entity);
            await context.SaveChangesAsync();
        }
    }
}
