using System.Diagnostics;
using System.IO;

namespace TimeTracking.Helpers;

/// <summary>
/// Registro mínimo de falhas (Seção 46), sem dependência externa (Seção 3): acrescenta linhas a
/// um app.log ao lado do settings.json. Existe porque algumas falhas são engolidas de propósito
/// (ex.: erro ao gravar o settings.json não pode derrubar o app) — mas não podem sumir sem rastro.
/// Nunca lança: registrar uma falha não pode causar outra.
///
/// Privacidade (Seção 46): o log leva apenas ids e mensagens técnicas. Quem chama NUNCA passa
/// nome ou descrição de tarefa/tag na mensagem — use o id.
/// </summary>
public static class AppLog
{
    /// <summary>Acima deste tamanho o arquivo é recomeçado do zero (não cresce indefinidamente).</summary>
    public const long MaxFileBytes = 1024 * 1024;

    private static readonly object Gate = new();

    /// <summary>Caminho do arquivo de log. Nulo usa %LocalAppData%\TimeTracking\app.log; os testes
    /// apontam para um arquivo temporário, para não escrever no log real do usuário.</summary>
    public static string? FilePath { get; set; }

    public static void Error(string message, Exception? exception = null)
    {
        var line = $"{DateTime.UtcNow:O} [ERROR] {message}";
        if (exception is not null)
        {
            line += $"{Environment.NewLine}{exception}";
        }

        Debug.WriteLine(line);

        try
        {
            AppendLine(FilePath ?? DefaultPath(), line, MaxFileBytes);
        }
        catch (Exception)
        {
            // Sem onde registrar — o Debug.WriteLine acima é o melhor esforço restante.
        }
    }

    /// <summary>Acrescenta a linha ao arquivo; se ele já passou de maxBytes, recomeça do zero
    /// (sem rotação nem arquivos de backup — o log é só para diagnóstico recente).</summary>
    public static void AppendLine(string path, string line, long maxBytes)
    {
        lock (Gate)
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length > maxBytes)
            {
                File.WriteAllText(path, line + Environment.NewLine);
            }
            else
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
    }

    private static string DefaultPath() =>
        Path.Combine(Path.GetDirectoryName(SettingsFilePathProvider.GetSettingsFilePath())!, "app.log");
}
