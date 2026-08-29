using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Data;
using TimeTracking.Models;
using TimeTracking.Repositories;
using TimeTracking.Services;
using DomainTask = TimeTracking.Models.Task;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Tests;

/// <summary>
/// Testes do timer (Seção 47/54) — a regra crítica do MVP. Usa um IClock controlável
/// para validar a matemática de forma determinística (sem sleeps reais), e SQLite
/// :memory: com conexão aberta para exercitar a persistência real (mesma estratégia
/// da Fase 2 — não o provider InMemory do EF Core).
/// </summary>
public class TimerServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly TestClock _clock;

    public TimerServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new AppDbContext(_options);
        context.Database.Migrate();

        _clock = new TestClock();
    }

    public void Dispose() => _connection.Dispose();

    private TimerService CreateTimerService()
    {
        var factory = new TestDbContextFactory(_options);
        return new TimerService(new TimeEntryRepository(factory), new TaskRepository(factory), _clock);
    }

    private async Task<int> CreateTaskAsync(string name)
    {
        var factory = new TestDbContextFactory(_options);
        var taskRepository = new TaskRepository(factory);
        var task = new DomainTask { Name = name, CreatedAt = _clock.UtcNow, UpdatedAt = _clock.UtcNow };
        await taskRepository.AddAsync(task);
        return task.Id;
    }

    [Fact]
    public async Task StartAsync_Creates_Open_Entry_And_Marks_Task_As_Running()
    {
        var timer = CreateTimerService();
        var taskId = await CreateTaskAsync("Desenvolver API");

        await timer.StartAsync(taskId);

        var status = await timer.GetStatusAsync(taskId);
        Assert.True(status.IsRunning);
        Assert.Equal(_clock.UtcNow, status.RunningStartedAt);
        Assert.Equal(TimeSpan.Zero, status.ClosedEntriesTotal);

        var active = await timer.GetActiveTaskAsync();
        Assert.NotNull(active);
        Assert.Equal(taskId, active!.Id);
    }

    [Fact]
    public async Task GetActiveTaskAsync_Returns_Null_When_Nothing_Running()
    {
        var timer = CreateTimerService();
        await CreateTaskAsync("Tarefa parada");

        Assert.Null(await timer.GetActiveTaskAsync());
    }

    [Fact]
    public async Task StartAsync_On_Different_Task_Pauses_Previous_And_Starts_New()
    {
        var timer = CreateTimerService();
        var taskAId = await CreateTaskAsync("Tarefa A");
        var taskBId = await CreateTaskAsync("Tarefa B");

        await timer.StartAsync(taskAId);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(30);

        await timer.StartAsync(taskBId);

        var statusA = await timer.GetStatusAsync(taskAId);
        var statusB = await timer.GetStatusAsync(taskBId);

        Assert.False(statusA.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(30), statusA.ClosedEntriesTotal);

        Assert.True(statusB.IsRunning);
        Assert.Equal(TimeSpan.Zero, statusB.ClosedEntriesTotal);

        var active = await timer.GetActiveTaskAsync();
        Assert.Equal(taskBId, active!.Id);
    }

    [Fact]
    public async Task Only_One_Open_TimeEntry_Exists_Globally_After_Switching_Tasks()
    {
        var timer = CreateTimerService();
        var taskAId = await CreateTaskAsync("Tarefa A");
        var taskBId = await CreateTaskAsync("Tarefa B");

        await timer.StartAsync(taskAId);
        await timer.StartAsync(taskBId);

        await using var context = new AppDbContext(_options);
        var openCount = await context.TimeEntries.CountAsync(te => te.EndedAt == null);
        Assert.Equal(1, openCount);
    }

    [Fact]
    public async Task Play_Pause_Play_Stop_Produces_Mathematically_Correct_Total()
    {
        // Cenário explícito da Seção 54: Play -> Pause -> Play -> Stop.
        var timer = CreateTimerService();
        var taskId = await CreateTaskAsync("Desenvolver API");

        await timer.StartAsync(taskId); // 10:00
        _clock.UtcNow = _clock.UtcNow.AddHours(1); // 11:00
        await timer.PauseAsync(taskId);

        _clock.UtcNow = _clock.UtcNow.AddHours(3); // 14:00
        await timer.StartAsync(taskId);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(30); // 14:30
        await timer.StopAsync(taskId);

        var status = await timer.GetStatusAsync(taskId);
        Assert.False(status.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(90), status.ClosedEntriesTotal); // 1h + 30min = 1h30 (Seção 12)
    }

    [Fact]
    public async Task GetStatusAsync_Sums_Closed_Sessions_Plus_Live_Running_Delta()
    {
        var timer = CreateTimerService();
        var taskId = await CreateTaskAsync("Desenvolver API");

        var factory = new TestDbContextFactory(_options);
        var timeEntryRepository = new TimeEntryRepository(factory);

        var baseDate = _clock.UtcNow;
        await timeEntryRepository.AddAsync(new TimeEntry { TaskId = taskId, StartedAt = baseDate, EndedAt = baseDate.AddHours(1) });
        await timeEntryRepository.AddAsync(new TimeEntry { TaskId = taskId, StartedAt = baseDate.AddHours(4), EndedAt = baseDate.AddHours(4.5) });

        // Sessão aberta: começou 20 minutos atrás, ainda rodando.
        var runningStart = baseDate.AddHours(6);
        await timeEntryRepository.AddAsync(new TimeEntry { TaskId = taskId, StartedAt = runningStart, EndedAt = null });
        _clock.UtcNow = runningStart.AddMinutes(20);

        var status = await timer.GetStatusAsync(taskId);

        Assert.True(status.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(90), status.ClosedEntriesTotal);
        Assert.Equal(TimeSpan.FromMinutes(110), status.GetElapsed(_clock.UtcNow)); // 90min fechados + 20min ao vivo
    }

    [Fact]
    public async Task Recovery_After_Restart_Reconstructs_Running_State_From_Persisted_Timestamps()
    {
        // Simula "Play -> fechar app -> abrir app" (Seção 16): uma segunda instância de
        // TimerService, sem nenhum estado em memória compartilhado, deve reconstruir a
        // sessão ativa apenas a partir dos timestamps persistidos.
        var firstRun = CreateTimerService();
        var taskId = await CreateTaskAsync("Desenvolver API");
        await firstRun.StartAsync(taskId);

        _clock.UtcNow = _clock.UtcNow.AddMinutes(20); // tempo passa com o "app fechado"

        var secondRun = CreateTimerService(); // nova instância = "app reaberto"
        var active = await secondRun.GetActiveTaskAsync();
        Assert.NotNull(active);
        Assert.Equal(taskId, active!.Id);

        var status = await secondRun.GetStatusAsync(taskId);
        Assert.True(status.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(20), status.GetElapsed(_clock.UtcNow));
    }
}
