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

    public void Dispose()
    {
        if (File.Exists(_tempPath))
        {
            File.Delete(_tempPath);
        }
    }
}
