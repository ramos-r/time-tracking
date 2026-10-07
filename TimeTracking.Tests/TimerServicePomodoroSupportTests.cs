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
/// Testes das adições ao TimerService exigidas pelo Pomodoro (Seção 34, v1.5.1): StopAtAsync,
/// evento TimerChanged com origem e tradução da violação do índice único (Seção 9).
/// </summary>
public class TimerServicePomodoroSupportTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly TestClock _clock = new();

    public TimerServicePomodoroSupportTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var context = new AppDbContext(_options);
        context.Database.Migrate();
    }

    public void Dispose() => _connection.Dispose();

    private TimerService CreateTimerService(ITimeEntryRepository? timeEntryRepository = null)
    {
        var factory = new TestDbContextFactory(_options);
        return new TimerService(timeEntryRepository ?? new TimeEntryRepository(factory), new TaskRepository(factory), _clock);
    }

    private async Task<int> CreateTaskAsync(string name)
    {
        var task = new DomainTask { Name = name, CreatedAt = _clock.UtcNow, UpdatedAt = _clock.UtcNow };
        await new TaskRepository(new TestDbContextFactory(_options)).AddAsync(task);
        return task.Id;
    }

    [Fact]
    public async Task StopAtAsync_ClosesOpenEntry_AtGivenTime_NotAtNow()
    {
        var timer = CreateTimerService();
        var taskId = await CreateTaskAsync("A");
        var start = _clock.UtcNow;
        await timer.StartAsync(taskId);

        _clock.UtcNow = start.AddHours(2);
        await timer.StopAtAsync(taskId, start.AddMinutes(25));

        var entries = await timer.GetEntriesForTaskAsync(taskId);
        var entry = Assert.Single(entries);
        Assert.Equal(start.AddMinutes(25), entry.EndedAt);
        Assert.False((await timer.GetStatusAsync(taskId)).IsRunning);
    }

    [Fact]
    public async Task StopAtAsync_BeforeStart_Throws_AndLeavesEntryOpen()
    {
        var timer = CreateTimerService();
        var taskId = await CreateTaskAsync("A");
        await timer.StartAsync(taskId);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(10);

        await Assert.ThrowsAsync<ArgumentException>(() => timer.StopAtAsync(taskId, _clock.UtcNow.AddMinutes(-30)));

        Assert.True((await timer.GetStatusAsync(taskId)).IsRunning);
    }

    [Fact]
    public async Task StopAtAsync_InTheFuture_Throws_AndLeavesEntryOpen()
    {
        var timer = CreateTimerService();
        var taskId = await CreateTaskAsync("A");
        await timer.StartAsync(taskId);

        await Assert.ThrowsAsync<ArgumentException>(() => timer.StopAtAsync(taskId, _clock.UtcNow.AddMinutes(5)));

        Assert.True((await timer.GetStatusAsync(taskId)).IsRunning);
    }

    [Fact]
    public async Task StopAtAsync_WhenTaskHasNoOpenEntry_DoesNothing_AndRaisesNoEvent()
    {
        var timer = CreateTimerService();
        var taskId = await CreateTaskAsync("A");
        var events = new List<TimerChange>();
        timer.TimerChanged += events.Add;

        await timer.StopAtAsync(taskId, _clock.UtcNow);

        Assert.Empty(events);
        Assert.Empty(await timer.GetEntriesForTaskAsync(taskId));
    }

    [Fact]
    public async Task TimerChanged_Start_And_Pause_Report_Kind_Task_And_DefaultOriginUser()
    {
        var timer = CreateTimerService();
        var taskId = await CreateTaskAsync("A");
        var events = new List<TimerChange>();
        timer.TimerChanged += events.Add;

        await timer.StartAsync(taskId);
        await timer.PauseAsync(taskId);

        Assert.Equal(
            new[]
            {
                new TimerChange(TimerChangeKind.Started, taskId, TimerOrigin.User),
                new TimerChange(TimerChangeKind.Ended, taskId, TimerOrigin.User),
            },
            events);
    }

    [Fact]
    public async Task TimerChanged_CarriesGivenOrigin_ForStart_Stop_And_StopAt()
    {
        var timer = CreateTimerService();
        var taskId = await CreateTaskAsync("A");
        var events = new List<TimerChange>();
        timer.TimerChanged += events.Add;

        await timer.StartAsync(taskId, TimerOrigin.Pomodoro);
        await timer.StopAtAsync(taskId, _clock.UtcNow, TimerOrigin.Pomodoro);
        await timer.StartAsync(taskId, TimerOrigin.Pomodoro);
        await timer.StopAsync(taskId, TimerOrigin.Pomodoro);

        Assert.Equal(4, events.Count);
        Assert.All(events, e => Assert.Equal(TimerOrigin.Pomodoro, e.Origin));
    }

    [Fact]
    public async Task TimerChanged_SwitchingTasks_RaisesEndedForPrevious_ThenStartedForNew()
    {
        var timer = CreateTimerService();
        var first = await CreateTaskAsync("A");
        var second = await CreateTaskAsync("B");
        await timer.StartAsync(first);
        var events = new List<TimerChange>();
        timer.TimerChanged += events.Add;

        await timer.StartAsync(second, TimerOrigin.Pomodoro);

        Assert.Equal(
            new[]
            {
                new TimerChange(TimerChangeKind.Ended, first, TimerOrigin.Pomodoro),
                new TimerChange(TimerChangeKind.Started, second, TimerOrigin.Pomodoro),
            },
            events);
    }

    [Fact]
    public async Task TimerChanged_StartingTheAlreadyActiveTask_RaisesNothing()
    {
        var timer = CreateTimerService();
        var taskId = await CreateTaskAsync("A");
        await timer.StartAsync(taskId);
        var events = new List<TimerChange>();
        timer.TimerChanged += events.Add;

        await timer.StartAsync(taskId);

        Assert.Empty(events);
    }

    [Fact]
    public async Task StartAsync_WhenDatabaseRejectsSecondOpenEntry_ThrowsTimerConflictException()
    {
        var factory = new TestDbContextFactory(_options);
        var realRepository = new TimeEntryRepository(factory);
        var first = await CreateTaskAsync("A");
        var second = await CreateTaskAsync("B");
        await CreateTimerService().StartAsync(first); // sessão aberta que o repositório "não enxerga"

        // Simula o início concorrente: a verificação em memória do TimerService não vê a sessão
        // aberta, então só o índice único da Seção 9 impede o segundo registro.
        var timer = CreateTimerService(new BlindToOpenEntryRepository(realRepository));
        var events = new List<TimerChange>();
        timer.TimerChanged += events.Add;

        var ex = await Assert.ThrowsAsync<TimerConflictException>(() => timer.StartAsync(second));

        Assert.IsType<DbUpdateException>(ex.InnerException);
        Assert.Empty(events);
        Assert.Empty(await realRepository.GetAllForTaskAsync(second));
    }

    /// <summary>Repositório que finge não haver sessão aberta — reproduz a janela de corrida
    /// entre a leitura e a gravação em um início concorrente.</summary>
    private sealed class BlindToOpenEntryRepository : ITimeEntryRepository
    {
        private readonly ITimeEntryRepository _inner;

        public BlindToOpenEntryRepository(ITimeEntryRepository inner) => _inner = inner;

        public Task<List<TimeEntry>> GetAllForTaskAsync(int taskId) => _inner.GetAllForTaskAsync(taskId);
        public Task<TimeEntry?> GetOpenEntryAsync() => Task.FromResult<TimeEntry?>(null);
        public Task AddAsync(TimeEntry entry) => _inner.AddAsync(entry);
        public Task UpdateAsync(TimeEntry entry) => _inner.UpdateAsync(entry);
    }
}
