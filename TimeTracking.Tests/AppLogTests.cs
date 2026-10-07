using System.IO;
using TimeTracking.Helpers;

namespace TimeTracking.Tests;

/// <summary>Limite de tamanho do app.log (Seção 46). Usa arquivos temporários próprios — nunca o
/// AppLog.FilePath estático, compartilhado com os outros testes.</summary>
public class AppLogTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"tt_applog_test_{Guid.NewGuid():N}.log");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void AppendLine_BelowLimit_KeepsPreviousLines()
    {
        AppLog.AppendLine(_path, "primeira", maxBytes: 1000);
        AppLog.AppendLine(_path, "segunda", maxBytes: 1000);

        var lines = File.ReadAllLines(_path);
        Assert.Equal(new[] { "primeira", "segunda" }, lines);
    }

    [Fact]
    public void AppendLine_AboveLimit_RestartsTheFile()
    {
        AppLog.AppendLine(_path, new string('x', 200), maxBytes: 100);
        AppLog.AppendLine(_path, "nova", maxBytes: 100);

        Assert.Equal(new[] { "nova" }, File.ReadAllLines(_path));
    }

    [Fact]
    public void MaxFileBytes_IsOneMegabyte()
    {
        Assert.Equal(1024 * 1024, AppLog.MaxFileBytes);
    }
}
