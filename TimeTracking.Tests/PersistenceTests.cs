using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Data;
using TimeTracking.Models;
using TimeTracking.Repositories;
using DomainTask = TimeTracking.Models.Task;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Tests;

/// <summary>
/// Testes de persistência (Seção 47). Usa SQLite com Data Source=:memory: mantendo a conexão
/// aberta durante o teste, e aplica as migrations reais — não o provider InMemory do EF Core,
/// que não valida integridade relacional (decisão registrada na nota de revisão, item 12).
/// </summary>
public class PersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public PersistenceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new AppDbContext(_options);
        context.Database.Migrate();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    [Fact]
    public async Task Migrations_Create_Expected_Tables()
    {
        await using var context = CreateContext();

        Assert.True(await context.Database.CanConnectAsync());
        Assert.Empty(await context.Tags.ToListAsync());
        Assert.Empty(await context.Tasks.ToListAsync());
        Assert.Empty(await context.TimeEntries.ToListAsync());
    }

    [Fact]
    public async Task Can_Create_Tag_And_Task_Associated_To_It()
    {
        int tagId;
        await using (var context = CreateContext())
        {
            var tag = new Tag
            {
                Name = "Desenvolvimento",
                Color = "#C89B6D",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Tags.Add(tag);
            await context.SaveChangesAsync();
            tagId = tag.Id;

            context.Tasks.Add(new DomainTask
            {
                Name = "Desenvolver API",
                TagId = tagId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var task = await context.Tasks.Include(t => t.Tag).SingleAsync();
            Assert.Equal("Desenvolver API", task.Name);
            Assert.NotNull(task.Tag);
            Assert.Equal(tagId, task.Tag!.Id);
        }
    }

    [Fact]
    public async Task Deleting_Task_Cascades_Its_TimeEntries()
    {
        int taskId;
        await using (var context = CreateContext())
        {
            var task = new DomainTask { Name = "Estudos", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.Tasks.Add(task);
            await context.SaveChangesAsync();
            taskId = task.Id;

            context.TimeEntries.Add(new TimeEntry
            {
                TaskId = taskId,
                StartedAt = DateTime.UtcNow.AddHours(-1),
                EndedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var task = await context.Tasks.FindAsync(taskId);
            context.Tasks.Remove(task!);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            Assert.False(await context.TimeEntries.AnyAsync(te => te.TaskId == taskId));
        }
    }

    [Fact]
    public async Task Deleting_Tag_Sets_Task_TagId_Null_But_Keeps_Task()
    {
        int tagId, taskId;
        await using (var context = CreateContext())
        {
            var tag = new Tag { Name = "Trabalho", Color = "#7FAE82", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.Tags.Add(tag);
            await context.SaveChangesAsync();
            tagId = tag.Id;

            var task = new DomainTask { Name = "Reunião", TagId = tagId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.Tasks.Add(task);
            await context.SaveChangesAsync();
            taskId = task.Id;
        }

        await using (var context = CreateContext())
        {
            var tag = await context.Tags.FindAsync(tagId);
            context.Tags.Remove(tag!);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var task = await context.Tasks.FindAsync(taskId);
            Assert.NotNull(task);
            Assert.Null(task!.TagId);
        }
    }

    [Fact]
    public async Task Cannot_Persist_Two_Open_TimeEntries_Simultaneously()
    {
        int taskAId, taskBId;
        await using (var context = CreateContext())
        {
            var taskA = new DomainTask { Name = "Tarefa A", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            var taskB = new DomainTask { Name = "Tarefa B", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.Tasks.AddRange(taskA, taskB);
            await context.SaveChangesAsync();
            taskAId = taskA.Id;
            taskBId = taskB.Id;
        }

        await using (var context = CreateContext())
        {
            context.TimeEntries.Add(new TimeEntry { TaskId = taskAId, StartedAt = DateTime.UtcNow });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            context.TimeEntries.Add(new TimeEntry { TaskId = taskBId, StartedAt = DateTime.UtcNow });

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Sessions_Total_Time_Is_Sum_Of_Individual_Sessions()
    {
        await using var context = CreateContext();

        var task = new DomainTask { Name = "Desenvolver API", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        var baseDate = new DateTime(2026, 8, 29, 0, 0, 0, DateTimeKind.Utc);
        context.TimeEntries.AddRange(
            new TimeEntry { TaskId = task.Id, StartedAt = baseDate.AddHours(10), EndedAt = baseDate.AddHours(11) },
            new TimeEntry { TaskId = task.Id, StartedAt = baseDate.AddHours(14), EndedAt = baseDate.AddHours(15) });
        await context.SaveChangesAsync();

        var entries = await context.TimeEntries.Where(te => te.TaskId == task.Id).ToListAsync();
        var total = entries.Sum(te => (te.EndedAt! - te.StartedAt)!.Value.TotalMinutes);

        Assert.Equal(120, total);
    }

    /// <summary>
    /// Exercita TaskRepository/TagRepository.UpdateAsync de verdade (não o DbContext cru),
    /// cobrindo o bug corrigido na Fase 4: atualizar uma Task carregada com a navegação Tag
    /// via context.Tasks.Update(task) direto faria o EF Core tentar reanexar/atualizar
    /// também a Tag relacionada. O fix busca uma entidade rastreada e copia só os escalares.
    /// </summary>
    [Fact]
    public async Task Updating_Task_Through_Repository_Does_Not_Corrupt_Related_Tag()
    {
        var factory = new TestDbContextFactory(_options);
        var taskRepository = new TaskRepository(factory);
        var tagRepository = new TagRepository(factory);

        var tagA = new Tag { Name = "Desenvolvimento", Color = "#C89B6D", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var tagB = new Tag { Name = "Estudos", Color = "#7FAE82", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await tagRepository.AddAsync(tagA);
        await tagRepository.AddAsync(tagB);

        var task = new DomainTask { Name = "Rascunho", TagId = tagA.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await taskRepository.AddAsync(task);

        var loaded = await taskRepository.GetByIdAsync(task.Id);
        loaded!.Name = "Desenvolver API";
        loaded.TagId = tagB.Id;
        loaded.UpdatedAt = DateTime.UtcNow;
        await taskRepository.UpdateAsync(loaded);

        var reloaded = await taskRepository.GetByIdAsync(task.Id);
        Assert.Equal("Desenvolver API", reloaded!.Name);
        Assert.Equal(tagB.Id, reloaded.TagId);

        var tagAReloaded = await tagRepository.GetByIdAsync(tagA.Id);
        var tagBReloaded = await tagRepository.GetByIdAsync(tagB.Id);
        Assert.Equal("Desenvolvimento", tagAReloaded!.Name);
        Assert.Equal("Estudos", tagBReloaded!.Name);
    }
}
