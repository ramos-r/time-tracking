using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Data;
using TimeTracking.Repositories;
using TimeTracking.Services;
using TimeTracking.ViewModels;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Tests;

/// <summary>A tela Time Tracking recarrega a lista quando o Pomodoro abre/fecha sessões pelo
/// TimerService (item 4.4 da análise da Fase 12.0) — mas não duplica o recarregamento das
/// ações do próprio usuário, que já recarregam sozinhas.</summary>
public class TimeTrackingViewModelPomodoroSyncTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestClock _clock = new();
    private readonly TaskService _taskService;
    private readonly TimerService _timer;
    private readonly TimeTrackingViewModel _viewModel;

    public TimeTrackingViewModelPomodoroSyncTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var context = new AppDbContext(options);
        context.Database.Migrate();

        var factory = new TestDbContextFactory(options);
        _taskService = new TaskService(new TaskRepository(factory));
        _timer = new TimerService(new TimeEntryRepository(factory), new TaskRepository(factory), _clock);
        _viewModel = new TimeTrackingViewModel(
            _taskService, _timer, _clock,
            () => throw new InvalidOperationException("O editor não é usado nestes testes."));
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task SessionOpenedByThePomodoro_ReloadsTheList()
    {
        var task = await _taskService.CreateAsync("Tarefa", null, null);
        Assert.Empty(_viewModel.Tasks); // a lista só foi carregada antes de a tarefa existir

        await _timer.StartAsync(task.Id, TimerOrigin.Pomodoro);

        var item = Assert.Single(_viewModel.Tasks);
        Assert.True(item.IsRunning);
    }

    [Fact]
    public async Task SessionClosedByThePomodoro_ReloadsTheList()
    {
        var task = await _taskService.CreateAsync("Tarefa", null, null);
        await _timer.StartAsync(task.Id, TimerOrigin.Pomodoro);

        _clock.UtcNow += TimeSpan.FromMinutes(25);
        await _timer.StopAtAsync(task.Id, _clock.UtcNow, TimerOrigin.Pomodoro);

        var item = Assert.Single(_viewModel.Tasks);
        Assert.False(item.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25), item.Elapsed);
    }

    [Fact]
    public async Task SwitchingTasksByThePomodoro_LeavesTheListConsistent()
    {
        var first = await _taskService.CreateAsync("A", null, null);
        var second = await _taskService.CreateAsync("B", null, null);
        await _timer.StartAsync(first.Id, TimerOrigin.Pomodoro);

        await _timer.StartAsync(second.Id, TimerOrigin.Pomodoro); // Ended(A) + Started(B)

        Assert.False(_viewModel.Tasks.Single(t => t.Id == first.Id).IsRunning);
        Assert.True(_viewModel.Tasks.Single(t => t.Id == second.Id).IsRunning);
    }

    [Fact]
    public async Task UserOriginatedChanges_DoNotTriggerTheExtraReload()
    {
        var task = await _taskService.CreateAsync("Tarefa", null, null);

        await _timer.StartAsync(task.Id); // origin User: quem chama é que recarrega

        Assert.Empty(_viewModel.Tasks);
    }
}
