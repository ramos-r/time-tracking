using Microsoft.EntityFrameworkCore;
using TimeTracking.Data;
using TimeTracking.Models;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Repositories;

public class TimeEntryRepository : ITimeEntryRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public TimeEntryRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<TimeEntry>> GetAllForTaskAsync(int taskId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.TimeEntries
            .AsNoTracking()
            .Where(te => te.TaskId == taskId)
            .OrderBy(te => te.StartedAt)
            .ToListAsync();
    }

    public async Task<TimeEntry?> GetOpenEntryAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.TimeEntries
            .FirstOrDefaultAsync(te => te.EndedAt == null);
    }

    public async Task AddAsync(TimeEntry entry)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.TimeEntries.Add(entry);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(TimeEntry entry)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Mesmo motivo do TaskRepository.UpdateAsync: copiar escalares para uma entidade
        // rastreada nesta conexão, em vez de anexar "entry" (que pode trazer a navegação
        // Task de um contexto já descartado).
        var existing = await context.TimeEntries.FindAsync(entry.Id)
            ?? throw new InvalidOperationException($"TimeEntry {entry.Id} não encontrada.");

        existing.StartedAt = entry.StartedAt;
        existing.EndedAt = entry.EndedAt;

        await context.SaveChangesAsync();
    }
}
