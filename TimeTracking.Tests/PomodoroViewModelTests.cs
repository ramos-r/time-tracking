using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Data;
using TimeTracking.Repositories;
using TimeTracking.Services;
using TimeTracking.ViewModels;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Tests;

/// <summary>
/// Testes da camada de apresentação do Pomodoro (Fase 12B): o PomodoroViewModel só reflete o
/// serviço e hospeda o tick — aqui se confere o que a tela mostra e o que cada botão faz, com
/// o serviço real sobre SQLite :memory: e o TestClock (nenhum sleep, nenhum timer real).
/// </summary>
public class PomodoroViewModelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly TestClock _clock = new();
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"tt_pomodoro_vm_test_{Guid.NewGuid():N}.json");
    private readonly TaskService _taskService;
    private readonly TimerService _timer;
    private readonly PomodoroService _service;
    private readonly PomodoroViewModel _viewModel;

    public PomodoroViewModelTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var context = new AppDbContext(_options);
        context.Database.Migrate();

        var factory = new TestDbContextFactory(_options);
        _taskService = new TaskService(new TaskRepository(factory));
        _timer = new TimerService(new TimeEntryRepository(factory), new TaskRepository(factory), _clock);
        _service = new PomodoroService(_timer, _taskService, new AppSettingsStore(_settingsPath), _clock);
        _viewModel = new PomodoroViewModel(_service, _taskService);
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

    private Task PrimaryAction() => ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)_viewModel.PrimaryActionCommand).ExecuteAsync(null);
    private Task StopCycle() => ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)_viewModel.StopCycleCommand).ExecuteAsync(null);
    private Task ConfirmConflict() => ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)_viewModel.ConfirmConflictCommand).ExecuteAsync(null);

    private void AdvanceMinutes(double minutes) => _clock.UtcNow += TimeSpan.FromMinutes(minutes);

    private async Task<int> CreateTaskAsync(string name) => (await _taskService.CreateAsync(name, null, null)).Id;

    // ------------------------------------------------------------------ estado inicial e fases

    [Fact]
    public void InitialState_IsIdle_WithConfiguredFocusTime_AndCycleOneOfFour()
    {
        Assert.Equal("Pronto para focar", _viewModel.PhaseLabel);
        Assert.Equal("25:00", _viewModel.TimeDisplay);
        Assert.Equal("Iniciar foco", _viewModel.PrimaryActionText);
        Assert.False(_viewModel.ShowStopButton);
        Assert.False(_viewModel.IsActive);
        Assert.Equal("Ciclo 1 de 4", _viewModel.CycleText);
        Assert.Equal(4, _viewModel.CycleDots.Count);
        Assert.All(_viewModel.CycleDots, dot => Assert.False(dot.IsFilled));
        Assert.True(_viewModel.CanChangeTask);
        Assert.True(_viewModel.AreSettingsEnabled);
        Assert.Equal("Sem tarefa", _viewModel.SelectedTaskOption.Name);
    }

    [Fact]
    public async Task StartingFocus_ShowsFocusState_AndLocksTaskAndSettings()
    {
        await PrimaryAction();

        Assert.Equal("FOCO", _viewModel.PhaseLabel);
        Assert.Equal("Pausar", _viewModel.PrimaryActionText);
        Assert.True(_viewModel.IsFocusPhase);
        Assert.False(_viewModel.IsBreakPhase);
        Assert.True(_viewModel.IsActive);
        Assert.True(_viewModel.ShowStopButton);
        Assert.False(_viewModel.CanChangeTask);
        Assert.False(_viewModel.AreSettingsEnabled);
        Assert.Equal("Foco 25:00", _viewModel.ChipText);
    }

    [Fact]
    public async Task Tick_UpdatesTheCountdown_FromTimestamps()
    {
        await PrimaryAction();

        _clock.UtcNow += TimeSpan.FromSeconds(90);
        await _viewModel.TickAsync();

        Assert.Equal("23:30", _viewModel.TimeDisplay);
        Assert.Equal("Foco 23:30", _viewModel.ChipText);
    }

    [Fact]
    public async Task Tick_WhenFocusEnds_ShowsAwaitingBreak_WithFirstDotFilled()
    {
        await PrimaryAction();

        AdvanceMinutes(25);
        await _viewModel.TickAsync();

        Assert.Equal("Foco concluído — iniciar pausa?", _viewModel.PhaseLabel);
        Assert.Equal("Iniciar pausa", _viewModel.PrimaryActionText);
        Assert.Equal("00:00", _viewModel.TimeDisplay);
        Assert.Equal("Ciclo 1 de 4", _viewModel.CycleText);
        Assert.Equal(new[] { true, false, false, false }, _viewModel.CycleDots.Select(d => d.IsFilled));
        Assert.Equal("Foco concluído", _viewModel.ChipText);
    }

    [Fact]
    public async Task PauseAndResume_ChangeLabelsAndButtons()
    {
        await PrimaryAction(); // iniciar
        AdvanceMinutes(10);

        await PrimaryAction(); // pausar
        Assert.Equal("FOCO PAUSADO", _viewModel.PhaseLabel);
        Assert.Equal("Retomar", _viewModel.PrimaryActionText);
        Assert.Equal("15:00", _viewModel.TimeDisplay);
        Assert.True(_viewModel.CanChangeTask);
        Assert.Equal("Foco pausado 15:00", _viewModel.ChipText);

        await PrimaryAction(); // retomar
        Assert.Equal("FOCO", _viewModel.PhaseLabel);
    }

    [Fact]
    public async Task Break_UsesBreakColorFlag_AndTextOfTheBreak()
    {
        await PrimaryAction();
        AdvanceMinutes(25);
        await _viewModel.TickAsync();

        await PrimaryAction(); // iniciar pausa

        Assert.Equal("PAUSA CURTA", _viewModel.PhaseLabel);
        Assert.True(_viewModel.IsBreakPhase);
        Assert.False(_viewModel.IsFocusPhase);
        Assert.Equal("05:00", _viewModel.TimeDisplay);
        Assert.Equal("Pausa 05:00", _viewModel.ChipText);

        AdvanceMinutes(5);
        await _viewModel.TickAsync();
        Assert.Equal("Pausa concluída — iniciar foco?", _viewModel.PhaseLabel);
        Assert.Equal("Iniciar foco", _viewModel.PrimaryActionText);
        Assert.Equal("Ciclo 2 de 4", _viewModel.CycleText);
    }

    [Fact]
    public async Task LongBreak_ShowsAllDotsFilled_AndCycleNOfN()
    {
        _viewModel.FocusesUntilLongBreakText = "2";

        for (var i = 0; i < 2; i++)
        {
            await PrimaryAction(); // iniciar foco (1ª vez do Idle, 2ª de AguardandoFoco)
            AdvanceMinutes(25);
            await _viewModel.TickAsync();
            await PrimaryAction(); // iniciar pausa
            if (i == 0)
            {
                AdvanceMinutes(5);
                await _viewModel.TickAsync();
            }
        }

        Assert.Equal("PAUSA LONGA", _viewModel.PhaseLabel);
        Assert.Equal("Ciclo 2 de 2", _viewModel.CycleText);
        Assert.Equal(new[] { true, true }, _viewModel.CycleDots.Select(d => d.IsFilled));
        Assert.Equal("15:00", _viewModel.TimeDisplay);
    }

    [Fact]
    public async Task StopCycle_ReturnsToIdle()
    {
        await PrimaryAction();
        AdvanceMinutes(3);

        await StopCycle();

        Assert.Equal("Pronto para focar", _viewModel.PhaseLabel);
        Assert.False(_viewModel.IsActive);
        Assert.False(_viewModel.ShowStopButton);
        Assert.True(_viewModel.AreSettingsEnabled);
    }

    // ------------------------------------------------------------------ tarefa vinculada e conflito

    [Fact]
    public async Task RefreshTasks_ListsTasksAfterNoTaskOption()
    {
        await CreateTaskAsync("A");
        await CreateTaskAsync("B");

        await _viewModel.RefreshTasksAsync();

        // Mesma ordem da tela Time Tracking (a mais recente primeiro).
        Assert.Equal(new[] { "Sem tarefa", "B", "A" }, _viewModel.TaskOptions.Select(o => o.Name));
        Assert.Equal("Sem tarefa", _viewModel.SelectedTaskOption.Name);
    }

    [Fact]
    public async Task SelectingATask_LinksItInTheService_AndSelectingNoTaskUnlinks()
    {
        var taskId = await CreateTaskAsync("A");
        await _viewModel.RefreshTasksAsync();

        _viewModel.SelectedTaskOption = _viewModel.TaskOptions.Single(o => o.Id == taskId);
        Assert.Equal(taskId, _service.State.LinkedTaskId);

        _viewModel.SelectedTaskOption = _viewModel.TaskOptions.Single(o => o.Id is null);
        Assert.Null(_service.State.LinkedTaskId);
    }

    [Fact]
    public async Task FocusWithLinkedTask_RecordsTimeEntry_ThatShowsInTheTimerService()
    {
        var taskId = await CreateTaskAsync("A");
        await _viewModel.RefreshTasksAsync();
        _viewModel.SelectedTaskOption = _viewModel.TaskOptions.Single(o => o.Id == taskId);

        await PrimaryAction();
        AdvanceMinutes(25);
        await _viewModel.TickAsync();

        var entry = Assert.Single(await _timer.GetEntriesForTaskAsync(taskId));
        Assert.Equal(TimeSpan.FromMinutes(25), entry.EndedAt!.Value - entry.StartedAt);
    }

    [Fact]
    public async Task StartingFocus_WhileAnotherTaskRuns_OpensConflictDialog_AndConfirmingSwitches()
    {
        var taskId = await CreateTaskAsync("Tarefa do foco");
        var other = await CreateTaskAsync("Outra tarefa");
        await _viewModel.RefreshTasksAsync();
        _viewModel.SelectedTaskOption = _viewModel.TaskOptions.Single(o => o.Id == taskId);
        await _timer.StartAsync(other);

        await PrimaryAction();

        Assert.True(_viewModel.IsConflictOpen);
        Assert.Contains("Outra tarefa", _viewModel.ConflictMessage);
        Assert.Equal("Pronto para focar", _viewModel.PhaseLabel); // nada começou ainda

        await ConfirmConflict();

        Assert.False(_viewModel.IsConflictOpen);
        Assert.Equal("FOCO", _viewModel.PhaseLabel);
        Assert.False((await _timer.GetStatusAsync(other)).IsRunning);
        Assert.True((await _timer.GetStatusAsync(taskId)).IsRunning);
    }

    [Fact]
    public async Task CancelingTheConflictDialog_StartsNothing()
    {
        var taskId = await CreateTaskAsync("Tarefa do foco");
        var other = await CreateTaskAsync("Outra tarefa");
        await _viewModel.RefreshTasksAsync();
        _viewModel.SelectedTaskOption = _viewModel.TaskOptions.Single(o => o.Id == taskId);
        await _timer.StartAsync(other);
        await PrimaryAction();

        _viewModel.CancelConflictCommand.Execute(null);

        Assert.False(_viewModel.IsConflictOpen);
        Assert.Equal("Pronto para focar", _viewModel.PhaseLabel);
        Assert.Empty(await _timer.GetEntriesForTaskAsync(taskId));
        Assert.True((await _timer.GetStatusAsync(other)).IsRunning);
    }

    [Fact]
    public async Task DeletingTheLinkedTask_ResetsTheSelectorToNoTask()
    {
        var taskId = await CreateTaskAsync("A");
        await _viewModel.RefreshTasksAsync();
        _viewModel.SelectedTaskOption = _viewModel.TaskOptions.Single(o => o.Id == taskId);

        await _taskService.DeleteAsync(taskId);
        await _service.CheckPhaseEndAsync(); // sincroniza a reação assíncrona do serviço

        Assert.Equal("Sem tarefa", _viewModel.SelectedTaskOption.Name);
    }

    [Fact]
    public async Task StartingFocus_WithATaskDeletedMeanwhile_ShowsFriendlyError()
    {
        var taskId = await CreateTaskAsync("A");
        await _viewModel.RefreshTasksAsync();
        _viewModel.SelectedTaskOption = _viewModel.TaskOptions.Single(o => o.Id == taskId);
        // Exclusão direto no repositório: a lista do seletor ficou desatualizada.
        await new TaskRepository(new TestDbContextFactory(_options)).DeleteAsync(taskId);

        await PrimaryAction();

        Assert.Equal("Pronto para focar", _viewModel.PhaseLabel);
        Assert.False(string.IsNullOrWhiteSpace(_viewModel.ErrorMessage));
        Assert.DoesNotContain(_viewModel.TaskOptions, o => o.Id == taskId); // lista recarregada
    }

    // ------------------------------------------------------------------ configurações

    [Fact]
    public void ChangingASetting_AppliesToServiceAndIdleDisplay()
    {
        _viewModel.FocusMinutesText = "50";

        Assert.Equal(50, _service.Config.FocusMinutes);
        Assert.Equal("50:00", _viewModel.TimeDisplay);
        Assert.Null(_viewModel.ConfigError);
    }

    [Fact]
    public void NonNumericSetting_IsRevertedWithMessage()
    {
        _viewModel.ShortBreakMinutesText = "abc";

        Assert.Equal(5, _service.Config.ShortBreakMinutes);
        Assert.Equal("5", _viewModel.ShortBreakMinutesText);
        Assert.False(string.IsNullOrWhiteSpace(_viewModel.ConfigError));
    }

    [Fact]
    public void OutOfRangeSetting_IsClampedWithMessage()
    {
        _viewModel.FocusMinutesText = "500";

        Assert.Equal(120, _service.Config.FocusMinutes);
        Assert.Equal("120", _viewModel.FocusMinutesText);
        Assert.Contains("entre 1 e 120", _viewModel.ConfigError);

        _viewModel.FocusesUntilLongBreakText = "1";
        Assert.Equal(2, _service.Config.FocusesUntilLongBreak);
    }

    [Fact]
    public void RestoreDefaults_Brings25_5_15_4_Back_AndClearsTheError()
    {
        _viewModel.FocusMinutesText = "50";
        _viewModel.ShortBreakMinutesText = "abc";
        _viewModel.LongBreakMinutesText = "30";
        _viewModel.FocusesUntilLongBreakText = "6";

        _viewModel.RestoreDefaultsCommand.Execute(null);

        var config = _service.Config;
        Assert.Equal((25, 5, 15, 4), (config.FocusMinutes, config.ShortBreakMinutes, config.LongBreakMinutes, config.FocusesUntilLongBreak));
        Assert.Equal("25", _viewModel.FocusMinutesText);
        Assert.Null(_viewModel.ConfigError);
    }

    [Fact]
    public async Task SettingChangedDuringFocus_DoesNotChangeTheRunningPhase()
    {
        await PrimaryAction();
        Assert.False(_viewModel.AreSettingsEnabled);

        // A tela desabilita os campos; mesmo assim, se o valor mudar, a fase atual não muda.
        _viewModel.FocusMinutesText = "10";
        AdvanceMinutes(5);
        await _viewModel.TickAsync();

        Assert.Equal("20:00", _viewModel.TimeDisplay);
    }

    [Fact]
    public void SettingsTab_StartsCollapsed_AndTogglesOnCommand()
    {
        Assert.False(_viewModel.IsSettingsOpen);

        _viewModel.ToggleSettingsCommand.Execute(null);
        Assert.True(_viewModel.IsSettingsOpen);

        _viewModel.ToggleSettingsCommand.Execute(null);
        Assert.False(_viewModel.IsSettingsOpen);
    }

    // ------------------------------------------------------------------ formato do relógio

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(1500, "25:00")]
    [InlineData(1499.2, "25:00")] // arredonda para cima: 25:00 no início, 00:00 só no fim
    [InlineData(61, "01:01")]
    [InlineData(7200, "120:00")]
    [InlineData(-5, "00:00")]
    public void FormatTime_RoundsUp_AndDoesNotWrapIntoHours(double seconds, string expected)
    {
        Assert.Equal(expected, PomodoroViewModel.FormatTime(TimeSpan.FromSeconds(seconds)));
    }

    // ------------------------------------------------------------------ chip da barra superior (D9)

    [Fact]
    public async Task Chip_ShowsOnlyOutsidePomodoroScreen_WhileACycleIsActive()
    {
        var main = new MainViewModel(new StubNavigationService(), _viewModel);
        var changes = new List<string?>();
        main.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.False(main.ShowPomodoroChip); // Idle

        await PrimaryAction();
        Assert.True(main.ShowPomodoroChip); // ciclo ativo, em Time Tracking
        Assert.Contains(nameof(MainViewModel.ShowPomodoroChip), changes);

        main.NavigateToCommand.Execute(AppPage.Pomodoro);
        Assert.False(main.ShowPomodoroChip); // na própria tela Pomodoro o chip some

        main.NavigateToCommand.Execute(AppPage.Settings);
        Assert.True(main.ShowPomodoroChip);

        await StopCycle();
        Assert.False(main.ShowPomodoroChip);
    }

    private sealed class StubNavigationService : INavigationService
    {
        public object ResolveViewModel(AppPage page) => new object();
    }
}
