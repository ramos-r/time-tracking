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
/// Testes do PomodoroService (Seção 70 e Seção 47, v1.5.1). Usam SQLite :memory: real, o
/// TestClock (nenhum sleep real) e um settings.json temporário. "Fechar e reabrir o app" é
/// simulado criando uma segunda instância do serviço sobre o mesmo settings.json e o mesmo banco.
///
/// As reações a mudanças externas são fire-and-forget; como o semáforo do serviço é FIFO, uma
/// chamada a CheckPhaseEndAsync logo depois (Flush) só retorna depois delas — sem esperas reais.
/// </summary>
public class PomodoroServiceTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 8, 29, 14, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly TestClock _clock = new() { UtcNow = Start };
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"tt_pomodoro_test_{Guid.NewGuid():N}.json");
    private readonly TaskService _taskService;
    private readonly TimerService _timer;

    public PomodoroServiceTests()
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

    private PomodoroService CreateService(ITimerService? timer = null) =>
        new(timer ?? _timer, _taskService, new AppSettingsStore(_settingsPath), _clock);

    private async Task<int> CreateTaskAsync(string name = "Desenvolver API") =>
        (await _taskService.CreateAsync(name, null, null)).Id;

    private void Advance(TimeSpan by) => _clock.UtcNow += by;
    private void AdvanceMinutes(int minutes) => Advance(TimeSpan.FromMinutes(minutes));

    private static Task Flush(PomodoroService service) => service.CheckPhaseEndAsync();

    private static async Task<PomodoroActionResult> StartOk(PomodoroService service, int? taskId)
    {
        var result = await service.StartAsync(taskId);
        Assert.Equal(PomodoroActionStatus.Success, result.Status);
        return result;
    }

    // ------------------------------------------------------------------ ciclo completo

    [Fact]
    public async Task Focus_ShortBreak_Focus_WithLinkedTask_CreatesExactlyTwoEntries_WithConfiguredDuration()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();

        await StartOk(service, taskId);
        AdvanceMinutes(25);
        await service.CheckPhaseEndAsync();
        Assert.Equal(PomodoroPhase.AwaitingBreak, service.Phase);

        await service.StartNextPhaseAsync();
        Assert.Equal(PomodoroPhase.ShortBreak, service.Phase);
        AdvanceMinutes(5);
        await service.CheckPhaseEndAsync();
        Assert.Equal(PomodoroPhase.AwaitingFocus, service.Phase);

        await service.StartNextPhaseAsync();
        Assert.Equal(PomodoroPhase.Focus, service.Phase);
        AdvanceMinutes(25);
        await service.CheckPhaseEndAsync();

        var entries = await _timer.GetEntriesForTaskAsync(taskId);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(TimeSpan.FromMinutes(25), e.EndedAt!.Value - e.StartedAt));
        Assert.Equal(Start, entries[0].StartedAt);
        Assert.Equal(Start.AddMinutes(30), entries[1].StartedAt);
    }

    [Fact]
    public async Task AfterConfiguredNumberOfFocuses_TheNextBreakIsLong()
    {
        var service = CreateService();

        for (var focus = 1; focus <= 4; focus++)
        {
            if (focus == 1)
            {
                await StartOk(service, null);
            }
            else
            {
                await service.StartNextPhaseAsync();
            }

            AdvanceMinutes(25);
            await service.CheckPhaseEndAsync();
            Assert.Equal(focus, service.State.CompletedFocusCount);

            await service.StartNextPhaseAsync();
            if (focus < 4)
            {
                Assert.Equal(PomodoroPhase.ShortBreak, service.Phase);
                AdvanceMinutes(5);
            }
            else
            {
                Assert.Equal(PomodoroPhase.LongBreak, service.Phase);
                Assert.Equal(TimeSpan.FromMinutes(15), service.GetRemainingTime());
                AdvanceMinutes(15);
            }

            await service.CheckPhaseEndAsync();
            Assert.Equal(PomodoroPhase.AwaitingFocus, service.Phase);
        }

        // O contador não zera depois da pausa longa; o 5º foco volta a ser o "1 de 4".
        Assert.Equal(4, service.State.CompletedFocusCount);
        Assert.Equal(1, service.CycleNumber);
    }

    [Fact]
    public async Task CycleNumber_FollowsTheD10Rules()
    {
        var service = CreateService();
        Assert.Equal(1, service.CycleNumber); // Idle

        var expected = new (int focus, int awaitingBreak, int breakPhase, int awaitingFocus)[]
        {
            (1, 1, 1, 2), (2, 2, 2, 3), (3, 3, 3, 4), (4, 4, 4, 1),
        };

        for (var i = 0; i < expected.Length; i++)
        {
            if (i == 0)
            {
                await StartOk(service, null);
            }
            else
            {
                await service.StartNextPhaseAsync();
            }

            Assert.Equal(expected[i].focus, service.CycleNumber);
            AdvanceMinutes(25);
            await service.CheckPhaseEndAsync();
            Assert.Equal(expected[i].awaitingBreak, service.CycleNumber);

            await service.StartNextPhaseAsync();
            Assert.Equal(expected[i].breakPhase, service.CycleNumber);
            AdvanceMinutes(i == 3 ? 15 : 5);
            await service.CheckPhaseEndAsync();
            Assert.Equal(expected[i].awaitingFocus, service.CycleNumber);
        }
    }

    // ------------------------------------------------------------------ regra crítica

    [Fact]
    public async Task ReopeningAfterFocusEnded_ClosesEntryAtTheoreticalEnd_AndGoesToAwaitingBreak_WithoutNotification()
    {
        var taskId = await CreateTaskAsync();
        var firstRun = CreateService();
        await StartOk(firstRun, taskId);

        // App fechado às 14:10 e reaberto às 16:00.
        AdvanceMinutes(120);
        var reopened = CreateService();
        var notifications = new List<PomodoroPhase>();
        reopened.PhaseEnded += notifications.Add;
        await reopened.RecoverOnStartupAsync();

        var entry = Assert.Single(await _timer.GetEntriesForTaskAsync(taskId));
        Assert.Equal(Start, entry.StartedAt);
        Assert.Equal(Start.AddMinutes(25), entry.EndedAt); // 25 min — e não 2h
        Assert.Equal(PomodoroPhase.AwaitingBreak, reopened.Phase);
        Assert.Equal(1, reopened.State.CompletedFocusCount);
        Assert.Empty(notifications);
        Assert.Null(await _timer.GetActiveTaskAsync());
    }

    [Fact]
    public async Task ReopeningAfterBreakEnded_GoesToAwaitingFocus_WithoutNotification()
    {
        var firstRun = CreateService();
        await StartOk(firstRun, null);
        AdvanceMinutes(25);
        await firstRun.CheckPhaseEndAsync();
        await firstRun.StartNextPhaseAsync(); // pausa curta às 14:25

        AdvanceMinutes(240);
        var reopened = CreateService();
        var notifications = new List<PomodoroPhase>();
        reopened.PhaseEnded += notifications.Add;
        await reopened.RecoverOnStartupAsync();

        Assert.Equal(PomodoroPhase.AwaitingFocus, reopened.Phase);
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task ReopeningBeforeFocusEnds_ResumesWithCorrectRemainingTime_AndKeepsEntryOpen()
    {
        var taskId = await CreateTaskAsync();
        var firstRun = CreateService();
        await StartOk(firstRun, taskId);

        AdvanceMinutes(10);
        var reopened = CreateService();
        await reopened.RecoverOnStartupAsync();

        Assert.Equal(PomodoroPhase.Focus, reopened.Phase);
        Assert.Equal(TimeSpan.FromMinutes(15), reopened.GetRemainingTime());
        Assert.True((await _timer.GetStatusAsync(taskId)).IsRunning);

        AdvanceMinutes(15);
        await reopened.CheckPhaseEndAsync();
        var entry = Assert.Single(await _timer.GetEntriesForTaskAsync(taskId));
        Assert.Equal(Start.AddMinutes(25), entry.EndedAt);
    }

    [Fact]
    public async Task Suspension_ClockJumpsPastEnd_ClosesEntryAtTheoreticalEnd_AndNotifiesOnce()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        var notifications = new List<PomodoroPhase>();
        service.PhaseEnded += notifications.Add;
        await StartOk(service, taskId);

        AdvanceMinutes(180); // PC suspenso; o primeiro tick só acontece depois de acordar
        await service.CheckPhaseEndAsync();
        await service.CheckPhaseEndAsync(); // o tick seguinte não repete o fim

        var entry = Assert.Single(await _timer.GetEntriesForTaskAsync(taskId));
        Assert.Equal(Start.AddMinutes(25), entry.EndedAt);
        Assert.Equal(new[] { PomodoroPhase.Focus }, notifications);
        Assert.Equal(1, service.State.CompletedFocusCount);
    }

    [Fact]
    public async Task EntryStartEditedAfterTheoreticalEnd_ClosesAtItsStart_InsteadOfFailing()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        await StartOk(service, taskId);
        var open = (await _timer.GetEntriesForTaskAsync(taskId)).Single();
        await _timer.UpdateEntryTimestampsAsync(open.Id, Start.AddMinutes(40), null); // Seção 17

        AdvanceMinutes(50);
        await service.CheckPhaseEndAsync();

        var entry = Assert.Single(await _timer.GetEntriesForTaskAsync(taskId));
        Assert.Equal(Start.AddMinutes(40), entry.EndedAt); // max(fim teórico 14:25, início 14:40)
        Assert.Equal(PomodoroPhase.AwaitingBreak, service.Phase);
    }

    [Fact]
    public async Task PauseAfterTheFocusAlreadyEnded_IsTreatedAsTheEnd_NotAsAPause()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        await StartOk(service, taskId);

        AdvanceMinutes(30);
        var result = await service.PauseAsync();

        Assert.Equal(PomodoroActionStatus.InvalidState, result.Status);
        Assert.Equal(PomodoroPhase.AwaitingBreak, service.Phase);
        var entry = Assert.Single(await _timer.GetEntriesForTaskAsync(taskId));
        Assert.Equal(Start.AddMinutes(25), entry.EndedAt);
    }

    [Fact]
    public async Task RemainingTime_IsClamped_BetweenZeroAndPhaseDuration()
    {
        var service = CreateService();
        await StartOk(service, null);

        _clock.UtcNow = Start.AddMinutes(-30); // relógio do Windows ajustado para trás
        Assert.Equal(TimeSpan.FromMinutes(25), service.GetRemainingTime());

        _clock.UtcNow = Start.AddHours(10);
        Assert.Equal(TimeSpan.Zero, service.GetRemainingTime());
    }

    // ------------------------------------------------------------------ pausar / retomar

    [Fact]
    public async Task PauseAndResume_CreatesTwoEntries_SummingToTheFocusDuration()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        await StartOk(service, taskId);

        AdvanceMinutes(10);
        Assert.Equal(PomodoroActionStatus.Success, (await service.PauseAsync()).Status);
        Assert.Equal(PomodoroPhase.FocusPaused, service.Phase);
        Assert.Equal(TimeSpan.FromMinutes(15), service.GetRemainingTime());

        AdvanceMinutes(30); // tempo parado não conta nem para o foco nem para a TimeEntry
        Assert.Equal(TimeSpan.FromMinutes(15), service.GetRemainingTime());
        Assert.Equal(PomodoroActionStatus.Success, (await service.ResumeAsync()).Status);
        Assert.Equal(PomodoroPhase.Focus, service.Phase);

        AdvanceMinutes(15);
        await service.CheckPhaseEndAsync();

        var entries = await _timer.GetEntriesForTaskAsync(taskId);
        Assert.Equal(2, entries.Count);
        var total = entries.Aggregate(TimeSpan.Zero, (sum, e) => sum + (e.EndedAt!.Value - e.StartedAt));
        Assert.Equal(TimeSpan.FromMinutes(25), total);
    }

    [Fact]
    public async Task PausingABreak_DoesNotTouchEntries_AndResumeRestoresTheSameBreak()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        var notifications = new List<PomodoroPhase>();
        service.PhaseEnded += notifications.Add;
        await StartOk(service, taskId);
        AdvanceMinutes(25);
        await service.CheckPhaseEndAsync();
        await service.StartNextPhaseAsync();

        AdvanceMinutes(2);
        await service.PauseAsync();
        Assert.Equal(PomodoroPhase.BreakPaused, service.Phase);
        Assert.Equal(PomodoroPhase.ShortBreak, service.State.PausedPhase);
        Assert.Equal(TimeSpan.FromMinutes(3), service.GetRemainingTime());

        AdvanceMinutes(60);
        Assert.Equal(TimeSpan.FromMinutes(3), service.GetRemainingTime());
        await service.ResumeAsync();
        Assert.Equal(PomodoroPhase.ShortBreak, service.Phase);

        AdvanceMinutes(3);
        await service.CheckPhaseEndAsync();

        Assert.Equal(PomodoroPhase.AwaitingFocus, service.Phase);
        Assert.Equal(new[] { PomodoroPhase.Focus, PomodoroPhase.ShortBreak }, notifications);
        Assert.Single(await _timer.GetEntriesForTaskAsync(taskId)); // só o foco gerou TimeEntry
    }

    [Fact]
    public async Task PausedState_SurvivesReopeningTheApp()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        await StartOk(service, taskId);
        AdvanceMinutes(10);
        await service.PauseAsync();

        AdvanceMinutes(300);
        var reopened = CreateService();
        await reopened.RecoverOnStartupAsync();

        Assert.Equal(PomodoroPhase.FocusPaused, reopened.Phase);
        Assert.Equal(TimeSpan.FromMinutes(15), reopened.GetRemainingTime());
        Assert.Equal(taskId, reopened.State.LinkedTaskId);
    }

    // ------------------------------------------------------------------ ações externas

    [Fact]
    public async Task StoppingTheLinkedTaskOnTimeTrackingScreen_PausesTheFocus_PreservingRemainingTime()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        await StartOk(service, taskId);

        AdvanceMinutes(10);
        await _timer.StopAsync(taskId); // origin User: a tela Time Tracking
        await Flush(service);

        Assert.Equal(PomodoroPhase.FocusPaused, service.Phase);
        Assert.Equal(TimeSpan.FromMinutes(15), service.GetRemainingTime());

        // O Pomodoro não retoma sozinho, mesmo se o usuário der Play de novo na tarefa.
        AdvanceMinutes(5);
        await _timer.StartAsync(taskId);
        await Flush(service);
        Assert.Equal(PomodoroPhase.FocusPaused, service.Phase);

        // Retomar reaproveita a sessão aberta: nenhuma TimeEntry duplicada (índice único da Seção 9).
        Assert.Equal(PomodoroActionStatus.Success, (await service.ResumeAsync()).Status);
        Assert.Equal(2, (await _timer.GetEntriesForTaskAsync(taskId)).Count);
    }

    [Fact]
    public async Task StartingAnotherTask_DuringFocus_PausesTheFocus()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        var other = await CreateTaskAsync("Outra");
        await StartOk(service, taskId);

        AdvanceMinutes(8);
        await _timer.StartAsync(other); // confirmou o diálogo da Seção 15
        await Flush(service);

        Assert.Equal(PomodoroPhase.FocusPaused, service.Phase);
        Assert.Equal(TimeSpan.FromMinutes(17), service.GetRemainingTime());
        var entry = Assert.Single(await _timer.GetEntriesForTaskAsync(taskId));
        Assert.Equal(Start.AddMinutes(8), entry.EndedAt);
    }

    [Fact]
    public async Task ExternalChanges_AreIgnored_WhenThereIsNoFocusOrNoLinkedTask()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();

        // Sem foco rodando.
        await _timer.StartAsync(taskId);
        await Flush(service);
        Assert.Equal(PomodoroPhase.Idle, service.Phase);
        await _timer.StopAsync(taskId);

        // Foco sem tarefa vinculada.
        await StartOk(service, null);
        await _timer.StartAsync(taskId);
        await _timer.StopAsync(taskId);
        await Flush(service);
        Assert.Equal(PomodoroPhase.Focus, service.Phase);
    }

    [Fact]
    public async Task ActionsOriginatedByThePomodoro_AreIgnoredByItself()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        var other = await CreateTaskAsync("Outra");
        await _timer.StartAsync(other);

        // Trocar a tarefa ativa pelo Pomodoro gera Ended(other) e Started(taskId) com origin Pomodoro.
        var result = await service.StartAsync(taskId, replaceActive: true);
        await Flush(service);

        Assert.Equal(PomodoroActionStatus.Success, result.Status);
        Assert.Equal(PomodoroPhase.Focus, service.Phase);
    }

    // ------------------------------------------------------------------ início do foco (D3)

    [Fact]
    public async Task StartingFocus_WhileAnotherTaskRuns_AsksForConfirmation_AndCancelingStartsNothing()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        var other = await CreateTaskAsync("Outra tarefa");
        await _timer.StartAsync(other);

        var result = await service.StartAsync(taskId);

        Assert.Equal(PomodoroActionStatus.NeedsConfirmation, result.Status);
        Assert.Equal("Outra tarefa", result.ActiveTaskName);
        Assert.Equal(PomodoroPhase.Idle, service.Phase);
        Assert.Empty(await _timer.GetEntriesForTaskAsync(taskId));
        Assert.True((await _timer.GetStatusAsync(other)).IsRunning);
        Assert.Null(service.State.LinkedTaskId); // o vínculo só vale depois de o foco começar
    }

    [Fact]
    public async Task StartingFocus_ConfirmedWithReplaceActive_SwitchesTheRunningTask()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        var other = await CreateTaskAsync("Outra tarefa");
        await _timer.StartAsync(other);
        AdvanceMinutes(3);

        var result = await service.StartAsync(taskId, replaceActive: true);

        Assert.Equal(PomodoroActionStatus.Success, result.Status);
        Assert.False((await _timer.GetStatusAsync(other)).IsRunning);
        Assert.True((await _timer.GetStatusAsync(taskId)).IsRunning);
        Assert.Equal(taskId, service.State.LinkedTaskId);
    }

    [Fact]
    public async Task StartingFocus_WithLinkedTaskAlreadyRunning_ReusesTheOpenEntry()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        await _timer.StartAsync(taskId); // Play pela tela Time Tracking às 14:00
        AdvanceMinutes(10);

        await StartOk(service, taskId);
        Assert.Single(await _timer.GetEntriesForTaskAsync(taskId));

        AdvanceMinutes(25);
        await service.CheckPhaseEndAsync();

        var entry = Assert.Single(await _timer.GetEntriesForTaskAsync(taskId));
        Assert.Equal(Start, entry.StartedAt);
        Assert.Equal(Start.AddMinutes(35), entry.EndedAt); // 10 min antes + 25 min de foco
    }

    [Fact]
    public async Task StartingFocus_WhenDatabaseRejectsTheEntry_ReturnsFriendlyFailure_AndDoesNotStart()
    {
        var gated = new GatedTimerService(_timer) { ThrowConflictOnStart = true };
        var service = CreateService(gated);
        var taskId = await CreateTaskAsync();

        var result = await service.StartAsync(taskId);

        Assert.Equal(PomodoroActionStatus.Failed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Equal(PomodoroPhase.Idle, service.Phase);
    }

    [Fact]
    public async Task StartingFocus_WithMissingTask_Fails()
    {
        var service = CreateService();

        var result = await service.StartAsync(9999);

        Assert.Equal(PomodoroActionStatus.Failed, result.Status);
        Assert.Equal(PomodoroPhase.Idle, service.Phase);
    }

    [Fact]
    public async Task StartAsync_OutsideIdle_IsRejected()
    {
        var service = CreateService();
        await StartOk(service, null);

        Assert.Equal(PomodoroActionStatus.InvalidState, (await service.StartAsync(null)).Status);
    }

    // ------------------------------------------------------------------ sem tarefa / tarefa excluída

    [Fact]
    public async Task CycleWithoutLinkedTask_CreatesNoTimeEntry()
    {
        var service = CreateService();
        var bystander = await CreateTaskAsync("Não vinculada");

        await StartOk(service, null);
        Assert.Null(await _timer.GetActiveTaskAsync());
        AdvanceMinutes(25);
        await service.CheckPhaseEndAsync();
        await service.StartNextPhaseAsync();
        AdvanceMinutes(5);
        await service.CheckPhaseEndAsync();

        Assert.Empty(await _timer.GetEntriesForTaskAsync(bystander));
        Assert.Null(await _timer.GetActiveTaskAsync());
        Assert.Equal(PomodoroPhase.AwaitingFocus, service.Phase);
    }

    [Fact]
    public async Task DeletingTheLinkedTask_DuringTheCycle_RemovesTheLink_AndTheCycleContinues()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        await StartOk(service, taskId);

        AdvanceMinutes(5);
        await _taskService.DeleteAsync(taskId);
        await Flush(service);

        Assert.Null(service.State.LinkedTaskId);
        Assert.Equal(PomodoroPhase.Focus, service.Phase);

        AdvanceMinutes(20);
        await service.CheckPhaseEndAsync(); // não pode lançar: não há mais sessão para fechar
        Assert.Equal(PomodoroPhase.AwaitingBreak, service.Phase);
    }

    [Fact]
    public async Task DeletingAnotherTask_KeepsTheLink()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        var other = await CreateTaskAsync("Outra");
        await StartOk(service, taskId);

        await _taskService.DeleteAsync(other);
        await Flush(service);

        Assert.Equal(taskId, service.State.LinkedTaskId);
    }

    [Fact]
    public async Task ClearingHistory_RemovesTheLink()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        await StartOk(service, taskId);

        await _taskService.ClearHistoryAsync();
        await Flush(service);

        Assert.Null(service.State.LinkedTaskId);
        Assert.Equal(PomodoroPhase.Focus, service.Phase);
    }

    [Fact]
    public async Task Recovery_DropsLinkToATaskDeletedWhileTheAppWasClosed()
    {
        var firstRun = CreateService();
        var taskId = await CreateTaskAsync();
        await firstRun.SetLinkedTaskAsync(taskId);
        // Exclusão feita direto no repositório: o app "estava fechado", ninguém ouviu o evento.
        await new TaskRepository(new TestDbContextFactory(_options)).DeleteAsync(taskId);

        var reopened = CreateService();
        await reopened.RecoverOnStartupAsync();

        Assert.Null(reopened.State.LinkedTaskId);
    }

    [Fact]
    public async Task Recovery_FocusWithinTimeButLinkedSessionNotOpen_BecomesPausedFocus()
    {
        var firstRun = CreateService();
        var taskId = await CreateTaskAsync();
        await StartOk(firstRun, taskId);
        AdvanceMinutes(5);
        // O app caiu entre fechar a TimeEntry e gravar o estado.
        await _timer.PauseAsync(taskId, TimerOrigin.Pomodoro);

        AdvanceMinutes(5);
        var reopened = CreateService();
        await reopened.RecoverOnStartupAsync();

        Assert.Equal(PomodoroPhase.FocusPaused, reopened.Phase);
        Assert.Equal(TimeSpan.FromMinutes(15), reopened.GetRemainingTime());
    }

    // ------------------------------------------------------------------ vínculo, configuração, parar

    [Fact]
    public async Task LinkedTask_CannotChangeDuringFocus_ButCanWhilePaused()
    {
        var service = CreateService();
        var first = await CreateTaskAsync("A");
        var second = await CreateTaskAsync("B");
        await StartOk(service, first);

        Assert.Equal(PomodoroActionStatus.InvalidState, (await service.SetLinkedTaskAsync(second)).Status);
        Assert.Equal(first, service.State.LinkedTaskId);

        AdvanceMinutes(5);
        await service.PauseAsync();
        Assert.Equal(PomodoroActionStatus.Success, (await service.SetLinkedTaskAsync(second)).Status);
        await service.ResumeAsync();

        Assert.True((await _timer.GetStatusAsync(second)).IsRunning);
        Assert.Equal(second, service.State.LinkedTaskId);
    }

    [Fact]
    public async Task ChangingConfig_DuringAPhase_KeepsCurrentDuration_AndAppliesToTheNext()
    {
        var service = CreateService();
        await StartOk(service, null);

        service.UpdateConfig(new PomodoroConfig { FocusMinutes = 10, ShortBreakMinutes = 2, LongBreakMinutes = 4, FocusesUntilLongBreak = 3 });
        AdvanceMinutes(5);
        Assert.Equal(TimeSpan.FromMinutes(20), service.GetRemainingTime()); // ainda 25, não 10

        AdvanceMinutes(20);
        await service.CheckPhaseEndAsync();
        await service.StartNextPhaseAsync();
        Assert.Equal(TimeSpan.FromMinutes(2), service.GetRemainingTime());
        AdvanceMinutes(2);
        await service.CheckPhaseEndAsync();
        await service.StartNextPhaseAsync();
        Assert.Equal(TimeSpan.FromMinutes(10), service.GetRemainingTime());
    }

    [Fact]
    public void UpdateConfig_ClampsToAllowedLimits_AndPersists()
    {
        var service = CreateService();

        service.UpdateConfig(new PomodoroConfig { FocusMinutes = 500, ShortBreakMinutes = 0, LongBreakMinutes = 90, FocusesUntilLongBreak = 99 });

        var reopened = CreateService();
        Assert.Equal(120, reopened.Config.FocusMinutes);
        Assert.Equal(1, reopened.Config.ShortBreakMinutes);
        Assert.Equal(60, reopened.Config.LongBreakMinutes);
        Assert.Equal(10, reopened.Config.FocusesUntilLongBreak);
    }

    [Fact]
    public async Task StopCycle_ClosesEntryNow_ResetsCounter_AndReturnsToIdle()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        await StartOk(service, null);
        AdvanceMinutes(25);
        await service.CheckPhaseEndAsync();
        await service.StartNextPhaseAsync();
        AdvanceMinutes(5);
        await service.CheckPhaseEndAsync();
        await service.SetLinkedTaskAsync(taskId);
        await service.StartNextPhaseAsync();
        AdvanceMinutes(7);

        await service.StopCycleAsync();

        var entry = Assert.Single(await _timer.GetEntriesForTaskAsync(taskId));
        Assert.Equal(Start.AddMinutes(37), entry.EndedAt);
        Assert.Equal(PomodoroPhase.Idle, service.Phase);
        Assert.Equal(0, service.State.CompletedFocusCount);
        Assert.Equal(taskId, service.State.LinkedTaskId); // a escolha da tarefa é mantida
        Assert.Equal(1, service.CycleNumber);
        Assert.False((await _timer.GetStatusAsync(taskId)).IsRunning);
    }

    [Fact]
    public async Task StopCycle_AfterFocusAlreadyEnded_ClosesEntryAtTheoreticalEnd()
    {
        var service = CreateService();
        var taskId = await CreateTaskAsync();
        await StartOk(service, taskId);

        AdvanceMinutes(40);
        await service.StopCycleAsync();

        var entry = Assert.Single(await _timer.GetEntriesForTaskAsync(taskId));
        Assert.Equal(Start.AddMinutes(25), entry.EndedAt);
        Assert.Equal(PomodoroPhase.Idle, service.Phase);
    }

    [Fact]
    public async Task StateChanged_IsRaised_AfterActions()
    {
        var service = CreateService();
        var raised = 0;
        service.StateChanged += () => raised++;

        await StartOk(service, null);
        Assert.Equal(1, raised);

        await service.PauseAsync();
        Assert.Equal(2, raised);

        await service.PauseAsync(); // inválido: não muda nada
        Assert.Equal(2, raised);
    }

    // ------------------------------------------------------------------ concorrência (D5)

    [Fact]
    public async Task TwoPhaseEndChecksAtTheSameInstant_CountTheFocusOnlyOnce()
    {
        var gated = new GatedTimerService(_timer);
        var service = CreateService(gated);
        var taskId = await CreateTaskAsync();
        var notifications = new List<PomodoroPhase>();
        service.PhaseEnded += notifications.Add;
        await StartOk(service, taskId);
        AdvanceMinutes(25);

        // O 1º tick fica parado dentro do StopAtAsync; o 2º chega enquanto o 1º ainda não
        // gravou o novo estado — sem o semáforo, ele também veria "Focus" e contaria de novo.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gated.StopAtGate = release;
        var firstTick = service.CheckPhaseEndAsync();
        var secondTick = service.CheckPhaseEndAsync();
        Assert.False(firstTick.IsCompleted);

        release.SetResult();
        await Task.WhenAll(firstTick, secondTick);

        Assert.Equal(1, gated.StopAtCalls);
        Assert.Equal(1, service.State.CompletedFocusCount);
        Assert.Equal(new[] { PomodoroPhase.Focus }, notifications);
        Assert.Equal(PomodoroPhase.AwaitingBreak, service.Phase);
    }

    /// <summary>Decorador que repassa tudo ao TimerService real, mas pode segurar o StopAtAsync
    /// até um sinal do teste ou simular a violação do índice único ao iniciar.</summary>
    private sealed class GatedTimerService : ITimerService
    {
        private readonly ITimerService _inner;

        public GatedTimerService(ITimerService inner) => _inner = inner;

        public TaskCompletionSource? StopAtGate { get; set; }
        public int StopAtCalls { get; private set; }
        public bool ThrowConflictOnStart { get; set; }

        public event Action<TimerChange>? TimerChanged
        {
            add => _inner.TimerChanged += value;
            remove => _inner.TimerChanged -= value;
        }

        public Task<TimerStatus> GetStatusAsync(int taskId) => _inner.GetStatusAsync(taskId);
        public Task<DomainTask?> GetActiveTaskAsync() => _inner.GetActiveTaskAsync();
        public Task PauseAsync(int taskId, TimerOrigin origin = TimerOrigin.User) => _inner.PauseAsync(taskId, origin);
        public Task StopAsync(int taskId, TimerOrigin origin = TimerOrigin.User) => _inner.StopAsync(taskId, origin);
        public Task<List<TimeEntry>> GetEntriesForTaskAsync(int taskId) => _inner.GetEntriesForTaskAsync(taskId);
        public Task UpdateEntryTimestampsAsync(int entryId, DateTime startedAt, DateTime? endedAt) =>
            _inner.UpdateEntryTimestampsAsync(entryId, startedAt, endedAt);
        public Task AddManualEntryAsync(int taskId, DateTime startedAtUtc, DateTime endedAtUtc) =>
            _inner.AddManualEntryAsync(taskId, startedAtUtc, endedAtUtc);

        public Task StartAsync(int taskId, TimerOrigin origin = TimerOrigin.User) =>
            ThrowConflictOnStart
                ? throw new TimerConflictException(new InvalidOperationException("simulado"))
                : _inner.StartAsync(taskId, origin);

        public async Task StopAtAsync(int taskId, DateTime endedAtUtc, TimerOrigin origin = TimerOrigin.User)
        {
            StopAtCalls++;
            if (StopAtGate is not null)
            {
                await StopAtGate.Task;
            }

            await _inner.StopAtAsync(taskId, endedAtUtc, origin);
        }
    }
}
