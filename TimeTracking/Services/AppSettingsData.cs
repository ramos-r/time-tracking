namespace TimeTracking.Services;

/// <summary>
/// Modelo do arquivo settings.json (Seção 26/57). Compartilhado entre ThemeService e
/// AccentColorService — ver AppSettingsStore para o porquê de um modelo único.
/// </summary>
public class AppSettingsData
{
    public string Theme { get; set; } = nameof(AppTheme.System);

    /// <summary>Hex (#RRGGBB) da cor de destaque escolhida pelo usuário (Seção 69). Null =
    /// nenhuma preferência salva ainda — AccentColorService aplica o padrão de fábrica.</summary>
    public string? AccentColorHex { get; set; }
}
