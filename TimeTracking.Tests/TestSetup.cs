using System.Runtime.CompilerServices;
using TimeTracking.Helpers;

namespace TimeTracking.Tests;

/// <summary>Roda uma vez, antes de qualquer teste: aponta o AppLog para um arquivo temporário,
/// para que testes que exercitam falhas registradas em log nunca escrevam no app.log real do
/// usuário (mesmo raciocínio dos caminhos temporários do AppSettingsStore).</summary>
internal static class TestSetup
{
    internal static readonly string LogPath = Path.Combine(Path.GetTempPath(), "timetracking_tests.log");

    [ModuleInitializer]
    internal static void Initialize() => AppLog.FilePath = LogPath;
}
