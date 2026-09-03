using System.IO;
using System.Text.Json;
using TimeTracking.Helpers;

namespace TimeTracking.Services;

/// <summary>
/// Leitura/escrita do settings.json único (Seção 26/57), compartilhado por ThemeService e
/// AccentColorService (Seção 69). Antes da Seção 69, apenas o tema era persistido e cada
/// gravação sobrescrevia o arquivo inteiro com um objeto contendo só o campo Theme — o que
/// era seguro enquanto havia um único gravador. Com a cor de destaque persistindo no mesmo
/// arquivo, duas gravações "cegas" (escrever só o próprio campo) fariam uma apagar o valor
/// salvo pela outra. Save() sempre recarrega o conteúdo atual do disco antes de aplicar a
/// alteração, para que os dois serviços nunca se pisem.
/// </summary>
public class AppSettingsStore
{
    private readonly string _path;

    /// <summary>filePath nulo (padrão em produção, via DI) usa o local definido pela Seção 9
    /// (%LocalAppData%). Um caminho explícito existe só para isolar testes de persistência do
    /// settings.json real do usuário — mesmo raciocínio do "Data Source=:memory:" da Seção 47.</summary>
    public AppSettingsStore(string? filePath = null)
    {
        _path = filePath ?? SettingsFilePathProvider.GetSettingsFilePath();
    }

    public AppSettingsData Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var data = JsonSerializer.Deserialize<AppSettingsData>(json);
                if (data is not null)
                {
                    return data;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Arquivo ausente/corrompido — cai no padrão abaixo.
        }

        return new AppSettingsData();
    }

    public void Save(Action<AppSettingsData> mutate)
    {
        try
        {
            var data = Load();
            mutate(data);
            var json = JsonSerializer.Serialize(data);
            File.WriteAllText(_path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Falha ao persistir não deve impedir a alteração de valer para a sessão atual.
        }
    }
}
