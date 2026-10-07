using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Data;
using TimeTracking.Repositories;
using TimeTracking.Services;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Tests;

/// <summary>Eventos do TaskService (v1.5.1): TaskDeleted, simétrico ao HistoryCleared, de que o
/// ícone da barra de tarefas e o PomodoroService dependem.</summary>
public class TaskServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TaskService _taskService;

    public TaskServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var context = new AppDbContext(options);
        context.Database.Migrate();

        _taskService = new TaskService(new TaskRepository(new TestDbContextFactory(options)));
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task DeleteAsync_RaisesTaskDeleted_WithTheDeletedId()
    {
        var task = await _taskService.CreateAsync("Tarefa", null, null);
        var deleted = new List<int>();
        _taskService.TaskDeleted += deleted.Add;

        await _taskService.DeleteAsync(task.Id);

        Assert.Equal(new[] { task.Id }, deleted);
        Assert.Null(await _taskService.GetByIdAsync(task.Id));
    }

    [Fact]
    public async Task DeleteAsync_InBulk_RaisesTaskDeleted_OncePerTask()
    {
        var first = await _taskService.CreateAsync("A", null, null);
        var second = await _taskService.CreateAsync("B", null, null);
        var deleted = new List<int>();
        _taskService.TaskDeleted += deleted.Add;

        await _taskService.DeleteAsync(first.Id);
        await _taskService.DeleteAsync(second.Id);

        Assert.Equal(new[] { first.Id, second.Id }, deleted);
    }
}
