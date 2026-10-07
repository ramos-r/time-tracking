using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Data;
using TimeTracking.Helpers;
using TimeTracking.Repositories;
using TimeTracking.Services;

namespace TimeTracking.Tests;

/// <summary>
/// Aviso de fim de fase (Seção 70, Fase 12C): quando avisar e quando NÃO avisar. O som e o
/// piscar da barra de tarefas são substituídos por um registrador (FakeAlerts) — o que se testa
/// é a regra, com o PomodoroService real sobre SQLite :memory: e o TestClock.
/// </summary>
public class PhaseEndNotifierTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 8, 29, 14, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly TestClock _clock = new() { UtcNow = Start };
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"tt_notifier_test_{Guid.NewGuid():N}.json");
    private readonly TaskService _taskService;
    private readonly TimerService _timer;
    private readonly FakeAlerts _alerts = new();

    public PhaseEndNotifierTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var context = new AppDbContext(_options);
        context.Database.Migrate();

        var factory = new TestDbContextFactory(_options);
        _taskService = new TaskService(new TaskRepository(factory));
        _timer = new TimerService(new TimeEntryRepository(factory), new TaskRepository(factory), _clock);
    }

    public void Dispose()
    {
        _connection.Dispose();
        foreach (var path in new[] { _settingsPath, _settingsPath + ".tmp" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private PomodoroService CreateService(INativeAlerts? alerts = null)
    {
        var service = new PomodoroService(_timer, _taskService, new AppSettingsStore(_settingsPath), _clock);
        _ = new PhaseEndNotifier(service, alerts ?? _alerts);
        return service;
    }

    private void AdvanceMinutes(int minutes) => _clock.UtcNow += TimeSpan.FromMinutes(minutes);

    [Fact]
    public async Task FocusEndDetectedWithAppOpen_PlaysFocusSound_AndFlashesTaskbar_Once()
    {
        var service = CreateService();
        await service.StartAsync(null);

        AdvanceMinutes(25);
        await service.CheckPhaseEndAsync();
        await service.CheckPhaseEndAsync(); // o tick seguinte não repete o aviso

        Assert.Equal(new[] { true }, _alerts.Sounds);
        Assert.Equal(1, _alerts.Flashes);
    }

    [Fact]
    public async Task BreakEnd_PlaysBreakSound_AndFlashesTaskbar()
    {
        var service = CreateService();
        await service.StartAsync(null);
        AdvanceMinutes(25);
        await service.CheckPhaseEndAsync();
        await service.StartNextPhaseAsync();

        AdvanceMinutes(5);
        await service.CheckPhaseEndAsync();

        Assert.Equal(new[] { true, false }, _alerts.Sounds); // fim do foco, depois fim da pausa
        Assert.Equal(2, _alerts.Flashes);
    }

    [Fact]
    public async Task Suspension_ClockJumpsPastEnd_AlertsOnTheFirstTickAfterWaking()
    {
        var service = CreateService();
        await service.StartAsync(null);

        AdvanceMinutes(180); // PC suspenso; o app continua aberto
        await service.CheckPhaseEndAsync();

        Assert.Equal(new[] { true }, _alerts.Sounds);
        Assert.Equal(1, _alerts.Flashes);
    }

    [Fact]
    public async Task ReopeningTheAppAfterTheEnd_DoesNotAlert()
    {
        var firstRun = CreateService(new FakeAlerts()); // a instância "anterior" não conta aqui
        await firstRun.StartAsync(null);
        AdvanceMinutes(120);

        var reopened = CreateService();
        await reopened.RecoverOnStartupAsync();

        Assert.Equal(PomodoroPhase.AwaitingBreak, reopened.Phase);
        Assert.Empty(_alerts.Sounds);
        Assert.Equal(0, _alerts.Flashes);
    }

    [Fact]
    public async Task PausingAndStopping_NeverAlert()
    {
        var service = CreateService();
        await service.StartAsync(null);
        AdvanceMinutes(10);
        await service.PauseAsync();
        await service.ResumeAsync();
        await service.StopCycleAsync();

        Assert.Empty(_alerts.Sounds);
        Assert.Equal(0, _alerts.Flashes);
    }

    [Fact]
    public async Task StoppingTheCycleAfterTheFocusAlreadyEnded_DoesNotAlert()
    {
        var service = CreateService();
        await service.StartAsync(null);

        AdvanceMinutes(40); // o fim passou sem nenhum tick; o usuário clica em Parar ciclo
        await service.StopCycleAsync();

        Assert.Empty(_alerts.Sounds);
        Assert.Equal(0, _alerts.Flashes);
    }

    [Fact]
    public async Task FailingSound_DoesNotBlockTheTaskbarFlash_NorBreakTheService()
    {
        var alerts = new FakeAlerts { ThrowOnSound = true };
        var service = CreateService(alerts);
        await service.StartAsync(null);

        AdvanceMinutes(25);
        await service.CheckPhaseEndAsync(); // não pode lançar

        Assert.Equal(1, alerts.Flashes);
        Assert.Equal(PomodoroPhase.AwaitingBreak, service.Phase);
    }

    [Fact]
    public async Task FailingFlash_DoesNotBreakTheService()
    {
        var alerts = new FakeAlerts { ThrowOnFlash = true };
        var service = CreateService(alerts);
        await service.StartAsync(null);

        AdvanceMinutes(25);
        await service.CheckPhaseEndAsync();

        Assert.Equal(new[] { true }, alerts.Sounds);
        Assert.Equal(PomodoroPhase.AwaitingBreak, service.Phase);
    }

    private sealed class FakeAlerts : INativeAlerts
    {
        public List<bool> Sounds { get; } = new();
        public int Flashes { get; private set; }
        public bool ThrowOnSound { get; init; }
        public bool ThrowOnFlash { get; init; }

        public void PlaySound(bool isFocusEnd)
        {
            if (ThrowOnSound)
            {
                throw new InvalidOperationException("sem dispositivo de áudio");
            }

            Sounds.Add(isFocusEnd);
        }

        public void FlashTaskbar()
        {
            Flashes++;
            if (ThrowOnFlash)
            {
                throw new InvalidOperationException("janela indisponível");
            }
        }
    }
}
