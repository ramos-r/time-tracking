using System.IO;
using TimeTracking.Services;

namespace TimeTracking.Tests;

/// <summary>
/// Regressão do problema que motivou o AppSettingsStore (Seção 69): antes, ThemeService e
/// AccentColorService salvariam o settings.json cada um sobrescrevendo o arquivo inteiro com
/// um objeto contendo só o próprio campo — o segundo a gravar apagaria o valor do primeiro.
/// Estes testes usam um arquivo temporário isolado (nunca o settings.json real do usuário).
/// </summary>
public class AppSettingsStoreTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"tt_settings_test_{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var store = new AppSettingsStore(_tempPath);

        var data = store.Load();

        Assert.Equal(nameof(AppTheme.System), data.Theme);
        Assert.Null(data.AccentColorHex);
    }

    [Fact]
    public void Save_ThenSave_DifferentField_PreservesBothValues()
    {
        var store = new AppSettingsStore(_tempPath);

        // Simula ThemeService.ApplyTheme seguido de AccentColorService.ApplyAccentColor —
        // cada um mexendo só no próprio campo, como faria em uso real.
        store.Save(data => data.Theme = nameof(AppTheme.Dark));
        store.Save(data => data.AccentColorHex = "#7129D3");

        var result = store.Load();

        Assert.Equal(nameof(AppTheme.Dark), result.Theme);
        Assert.Equal("#7129D3", result.AccentColorHex);
    }

    [Fact]
    public void Save_ThenSave_SameField_OverwritesOnlyThatField()
    {
        var store = new AppSettingsStore(_tempPath);

        store.Save(data => data.Theme = nameof(AppTheme.Dark));
        store.Save(data => data.AccentColorHex = "#7129D3");
        store.Save(data => data.Theme = nameof(AppTheme.Light));

        var result = store.Load();

        Assert.Equal(nameof(AppTheme.Light), result.Theme);
        Assert.Equal("#7129D3", result.AccentColorHex);
    }

    [Fact]
    public void Save_IsAtomic_LeavesNoTemporaryFileBehind()
    {
        var store = new AppSettingsStore(_tempPath);

        store.Save(data => data.Theme = nameof(AppTheme.Dark));
        store.Save(data => data.Theme = nameof(AppTheme.Light)); // 2ª vez: caminho File.Replace

        Assert.True(File.Exists(_tempPath));
        Assert.False(File.Exists(_tempPath + ".tmp"));
        Assert.Equal(nameof(AppTheme.Light), store.Load().Theme);
    }

    [Fact]
    public void Save_WhenWriteFails_DoesNotThrow_AndLogsTheFailure()
    {
        var missingFolder = Path.Combine(Path.GetTempPath(), $"tt_missing_{Guid.NewGuid():N}");
        var unwritablePath = Path.Combine(missingFolder, "settings.json");
        var store = new AppSettingsStore(unwritablePath);

        store.Save(data => data.Theme = nameof(AppTheme.Dark)); // não deve lançar

        Assert.Contains(unwritablePath, ReadLog());
    }

    [Fact]
    public void Pomodoro_Defaults_WhenFileDoesNotExist()
    {
        var data = new AppSettingsStore(_tempPath).Load();

        Assert.Equal(25, data.Pomodoro.FocusMinutes);
        Assert.Equal(5, data.Pomodoro.ShortBreakMinutes);
        Assert.Equal(15, data.Pomodoro.LongBreakMinutes);
        Assert.Equal(4, data.Pomodoro.FocusesUntilLongBreak);
        Assert.Equal(PomodoroPhase.Idle, data.PomodoroState.Phase);
    }

    [Fact]
    public void Pomodoro_State_And_Config_RoundTrip_AlongsideOtherSettings()
    {
        var store = new AppSettingsStore(_tempPath);
        var startedAt = new DateTime(2026, 8, 29, 14, 0, 0, DateTimeKind.Utc);

        store.Save(data => data.AccentColorHex = "#7129D3");
        store.Save(data =>
        {
            data.Pomodoro = new PomodoroConfig { FocusMinutes = 50, ShortBreakMinutes = 10, LongBreakMinutes = 30, FocusesUntilLongBreak = 3 };
            data.PomodoroState = new PomodoroState
            {
                Phase = PomodoroPhase.Focus,
                PhaseStartedAtUtc = startedAt,
                RemainingAtPhaseStart = TimeSpan.FromMinutes(50),
                CompletedFocusCount = 2,
                LinkedTaskId = 7,
            };
        });

        var result = store.Load();

        Assert.Equal("#7129D3", result.AccentColorHex);
        Assert.Equal(50, result.Pomodoro.FocusMinutes);
        Assert.Equal(3, result.Pomodoro.FocusesUntilLongBreak);
        Assert.Equal(PomodoroPhase.Focus, result.PomodoroState.Phase);
        Assert.Equal(startedAt, result.PomodoroState.PhaseStartedAtUtc);
        Assert.Equal(DateTimeKind.Utc, result.PomodoroState.PhaseStartedAtUtc.Kind);
        Assert.Equal(TimeSpan.FromMinutes(50), result.PomodoroState.RemainingAtPhaseStart);
        Assert.Equal(2, result.PomodoroState.CompletedFocusCount);
        Assert.Equal(7, result.PomodoroState.LinkedTaskId);
    }

    [Fact]
    public void Pomodoro_UnknownPhase_BecomesIdle_WithoutDiscardingTheRestOfTheFile()
    {
        File.WriteAllText(_tempPath, """
            { "Theme": "Dark", "AccentColorHex": "#7129D3",
              "PomodoroState": { "Phase": "FaseDeUmaVersaoFutura", "LinkedTaskId": 3 } }
            """);

        var data = new AppSettingsStore(_tempPath).Load();

        Assert.Equal("Dark", data.Theme);
        Assert.Equal("#7129D3", data.AccentColorHex);
        Assert.Equal(PomodoroPhase.Idle, data.PomodoroState.Phase);
    }

    [Fact]
    public void Pomodoro_NumericPhase_IsNotAccepted()
    {
        File.WriteAllText(_tempPath, """{ "PomodoroState": { "Phase": "99" } }""");

        Assert.Equal(PomodoroPhase.Idle, new AppSettingsStore(_tempPath).Load().PomodoroState.Phase);
    }

    [Fact]
    public void Pomodoro_OutOfRangeValues_AreClampedOnRead()
    {
        File.WriteAllText(_tempPath, """
            { "Pomodoro": { "FocusMinutes": 9999, "ShortBreakMinutes": 0, "LongBreakMinutes": -5, "FocusesUntilLongBreak": 1 },
              "PomodoroState": { "Phase": "Focus", "PhaseStartedAtUtc": "2026-08-29T14:00:00Z",
                                 "RemainingAtPhaseStart": "10:00:00", "CompletedFocusCount": -3, "LinkedTaskId": -1 } }
            """);

        var data = new AppSettingsStore(_tempPath).Load();

        Assert.Equal(120, data.Pomodoro.FocusMinutes);
        Assert.Equal(1, data.Pomodoro.ShortBreakMinutes);
        Assert.Equal(1, data.Pomodoro.LongBreakMinutes);
        Assert.Equal(2, data.Pomodoro.FocusesUntilLongBreak);
        Assert.Equal(TimeSpan.FromMinutes(120), data.PomodoroState.RemainingAtPhaseStart);
        Assert.Equal(0, data.PomodoroState.CompletedFocusCount);
        Assert.Null(data.PomodoroState.LinkedTaskId);
    }

    [Fact]
    public void Pomodoro_IncoherentState_FallsBackToIdle_KeepingTheLinkedTask()
    {
        // Foco "em andamento" sem horário de início: o tempo restante seria inventado.
        File.WriteAllText(_tempPath, """{ "PomodoroState": { "Phase": "Focus", "LinkedTaskId": 4 } }""");

        var state = new AppSettingsStore(_tempPath).Load().PomodoroState;

        Assert.Equal(PomodoroPhase.Idle, state.Phase);
        Assert.Equal(4, state.LinkedTaskId);
    }

    [Fact]
    public void Pomodoro_PausedPhaseState_RoundTripsRemainingAndWhichBreak()
    {
        var store = new AppSettingsStore(_tempPath);
        store.Save(data => data.PomodoroState = new PomodoroState
        {
            Phase = PomodoroPhase.BreakPaused,
            PausedPhase = PomodoroPhase.LongBreak,
            PhaseStartedAtUtc = new DateTime(2026, 8, 29, 14, 0, 0, DateTimeKind.Utc),
            RemainingWhenPaused = TimeSpan.FromMinutes(9),
        });

        var state = store.Load().PomodoroState;

        Assert.Equal(PomodoroPhase.BreakPaused, state.Phase);
        Assert.Equal(PomodoroPhase.LongBreak, state.PausedPhase);
        Assert.Equal(TimeSpan.FromMinutes(9), state.RemainingWhenPaused);
    }

    private static string ReadLog()
    {
        using var stream = new FileStream(TestSetup.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        foreach (var path in new[] { _tempPath, _tempPath + ".tmp" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
