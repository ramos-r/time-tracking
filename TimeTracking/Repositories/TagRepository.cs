using Microsoft.EntityFrameworkCore;
using TimeTracking.Data;
using TimeTracking.Models;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Repositories;

public class TagRepository : ITagRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public TagRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<Tag>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tags
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<Tag?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tags.FindAsync(id);
    }

    public async Task AddAsync(Tag tag)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Tag tag)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Tags.Update(tag);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Tags.FindAsync(id);
        if (entity is not null)
        {
            context.Tags.Remove(entity);
            await context.SaveChangesAsync();
        }
    }
}
