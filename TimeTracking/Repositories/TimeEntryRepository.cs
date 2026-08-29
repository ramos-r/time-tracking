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
        context.TimeEntries.Update(entry);
        await context.SaveChangesAsync();
    }
}
