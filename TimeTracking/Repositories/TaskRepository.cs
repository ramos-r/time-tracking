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
        context.Tasks.Update(task);
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
