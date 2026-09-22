using System.Windows;
using System.Windows.Media;

namespace TimeTracking.Services;

/// <summary>
/// Orquestra a cor de destaque personalizável (Seção 69): carrega/persiste a preferência via
/// AppSettingsStore, delega o cálculo das variações ao AccentColorCalculator (puro, sem
/// estado) e publica o resultado como brushes via DynamicResource nos recursos globais —
/// paralelo ao ThemeService, mas sem um arquivo Accent.xaml estático equivalente a
/// Dark.xaml/Light.xaml, já que a cor não é fixa (Seção 30, "Cor de destaque dinâmica").
///
/// Assina IThemeService.EffectiveThemeChanged para recalcular as variações sempre que o tema
/// mudar: a mesma cor-base pode exigir luminosidades diferentes em Dark vs. Light.
/// </summary>
public class AccentColorService : IAccentColorService
{
    private readonly IThemeService _themeService;
    private readonly AppSettingsStore _settingsStore;
    private ResourceDictionary? _publishedDictionary;

    // Saturação reduzida a ~65% da original em todo o leque (mesmos matizes) — Seção 71,
    // feedback de usuário: as cores predefinidas estavam muito saturadas/"neon" por padrão.
    public IReadOnlyList<string> PredefinedSwatches { get; } = new[]
    {
        IAccentColorService.DefaultAccentHex, // roxo/lilás (interface-ref.png)
        "#6893D8", // azul
        "#34A79A", // verde-azulado
        "#D4834A", // laranja
        "#D370A1", // rosa
        "#D56E6E", // vermelho
    };

    public string CurrentAccentHex { get; private set; } = IAccentColorService.DefaultAccentHex;

    public AccentColorService(IThemeService themeService, AppSettingsStore settingsStore)
    {
        _themeService = themeService;
        _settingsStore = settingsStore;
        _themeService.EffectiveThemeChanged += _ => Publish(CurrentAccentHex);
    }

    public void Initialize()
    {
        CurrentAccentHex = LoadSavedAccentHex();
        Publish(CurrentAccentHex);
    }

    public void ApplyAccentColor(string hex)
    {
        if (!AccentColorCalculator.TryParseHex(hex, out _))
        {
            return;
        }

        CurrentAccentHex = hex;
        SaveAccentHex(hex);
        Publish(hex);
    }

    private void Publish(string hex)
    {
        if (!AccentColorCalculator.TryParseHex(hex, out var baseColor))
        {
            baseColor = (Color)ColorConverter.ConvertFromString(IAccentColorService.DefaultAccentHex)!;
        }

        var isDarkTheme = _themeService.EffectiveTheme != AppTheme.Light;
        var variations = AccentColorCalculator.Derive(baseColor, isDarkTheme);

        var newDictionary = new ResourceDictionary
        {
            ["PrimaryBrush"] = Freeze(new SolidColorBrush(variations.Primary)),
            ["PrimaryHoverBrush"] = Freeze(new SolidColorBrush(variations.Hover)),
            ["PrimaryPressedBrush"] = Freeze(new SolidColorBrush(variations.Pressed)),
            ["PrimarySubtleBrush"] = Freeze(new SolidColorBrush(variations.Subtle)),
            ["TextOnPrimaryBrush"] = Freeze(new SolidColorBrush(variations.TextOnPrimary)),
        };

        var mergedDictionaries = Application.Current.Resources.MergedDictionaries;
        if (_publishedDictionary is not null)
        {
            var index = mergedDictionaries.IndexOf(_publishedDictionary);
            if (index >= 0)
            {
                mergedDictionaries[index] = newDictionary;
            }
            else
            {
                mergedDictionaries.Add(newDictionary);
            }
        }
        else
        {
            mergedDictionaries.Add(newDictionary);
        }

        _publishedDictionary = newDictionary;
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private string LoadSavedAccentHex()
    {
        var data = _settingsStore.Load();
        return !string.IsNullOrWhiteSpace(data.AccentColorHex) && AccentColorCalculator.TryParseHex(data.AccentColorHex, out _)
            ? data.AccentColorHex
            : IAccentColorService.DefaultAccentHex;
    }

    private void SaveAccentHex(string hex) =>
        _settingsStore.Save(data => data.AccentColorHex = hex);
}
