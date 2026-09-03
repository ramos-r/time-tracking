using System.Windows.Media;
using TimeTracking.Services;

namespace TimeTracking.Tests;

/// <summary>Testes da Seção 69 (cor de destaque personalizável) para a matemática pura de
/// AccentColorCalculator — a lista de casos no final da Seção 69 do spec guiou estes testes.</summary>
public class AccentColorCalculatorTests
{
    [Theory]
    [InlineData("#7129D3", 0x71, 0x29, 0xD3)]
    [InlineData("#FFFFFF", 0xFF, 0xFF, 0xFF)]
    [InlineData("#000000", 0x00, 0x00, 0x00)]
    public void TryParseHex_And_ToHex_RoundTrip(string hex, byte r, byte g, byte b)
    {
        Assert.True(AccentColorCalculator.TryParseHex(hex, out var color));
        Assert.Equal(r, color.R);
        Assert.Equal(g, color.G);
        Assert.Equal(b, color.B);
        Assert.Equal(hex, AccentColorCalculator.ToHex(color));
    }

    [Fact]
    public void TryParseHex_Rejects_Invalid_Hex()
    {
        Assert.False(AccentColorCalculator.TryParseHex("not-a-color", out _));
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0)] // preto
    [InlineData(0.0, 0.0, 1.0)] // branco
    [InlineData(0.73, 0.62, 0.47)] // matiz/saturação/luminosidade arbitrárias (evita pontos médios exatos no arredondamento)
    public void HslToRgb_And_RgbToHsl_RoundTrip(double h, double s, double l)
    {
        var color = AccentColorCalculator.HslToRgb(h, s, l);
        var (h2, s2, l2) = AccentColorCalculator.RgbToHsl(color);

        // Preto/branco têm S=0 (matiz indefinido) — só L é significativo nesse caso.
        Assert.Equal(l, l2, precision: 1);
        if (s > 0 && l is > 0.0 and < 1.0)
        {
            Assert.Equal(h, h2, precision: 1);
            Assert.Equal(s, s2, precision: 1);
        }
    }

    [Fact]
    public void Derive_DarkTheme_ClampsVeryDarkBaseColor_ToLegibleLightness()
    {
        // Roxo quase preto — ilegível sobre o fundo escuro do tema Dark sem ajuste.
        AccentColorCalculator.TryParseHex("#1A0033", out var nearBlack);

        var variations = AccentColorCalculator.Derive(nearBlack, isDarkTheme: true);
        var (_, _, primaryL) = AccentColorCalculator.RgbToHsl(variations.Primary);

        Assert.True(primaryL >= 0.50, $"Primary deveria ter luminosidade >= 0.50 no tema Dark, mas foi {primaryL}.");
    }

    [Fact]
    public void Derive_LightTheme_ClampsVeryLightBaseColor_ToLegibleLightness()
    {
        // Lavanda quase branca — ilegível sobre o fundo claro do tema Light sem ajuste.
        AccentColorCalculator.TryParseHex("#F5ECFF", out var nearWhite);

        var variations = AccentColorCalculator.Derive(nearWhite, isDarkTheme: false);
        var (_, _, primaryL) = AccentColorCalculator.RgbToHsl(variations.Primary);

        Assert.True(primaryL <= 0.50, $"Primary deveria ter luminosidade <= 0.50 no tema Light, mas foi {primaryL}.");
    }

    [Fact]
    public void Derive_DarkTheme_HoverIsLighterThanPrimary()
    {
        AccentColorCalculator.TryParseHex("#7129D3", out var baseColor);
        var variations = AccentColorCalculator.Derive(baseColor, isDarkTheme: true);

        var (_, _, primaryL) = AccentColorCalculator.RgbToHsl(variations.Primary);
        var (_, _, hoverL) = AccentColorCalculator.RgbToHsl(variations.Hover);
        var (_, _, pressedL) = AccentColorCalculator.RgbToHsl(variations.Pressed);

        Assert.True(hoverL > primaryL);
        Assert.True(pressedL > hoverL);
    }

    [Fact]
    public void Derive_LightTheme_HoverIsDarkerThanPrimary()
    {
        AccentColorCalculator.TryParseHex("#7129D3", out var baseColor);
        var variations = AccentColorCalculator.Derive(baseColor, isDarkTheme: false);

        var (_, _, primaryL) = AccentColorCalculator.RgbToHsl(variations.Primary);
        var (_, _, hoverL) = AccentColorCalculator.RgbToHsl(variations.Hover);
        var (_, _, pressedL) = AccentColorCalculator.RgbToHsl(variations.Pressed);

        Assert.True(hoverL < primaryL);
        Assert.True(pressedL < hoverL);
    }

    [Fact]
    public void Derive_SameBaseColor_ProducesDifferentPrimary_AcrossThemes()
    {
        AccentColorCalculator.TryParseHex("#7129D3", out var baseColor);

        var darkVariations = AccentColorCalculator.Derive(baseColor, isDarkTheme: true);
        var lightVariations = AccentColorCalculator.Derive(baseColor, isDarkTheme: false);

        Assert.NotEqual(darkVariations.Primary, lightVariations.Primary);
    }

    // Mesmas âncoras usadas internamente por AccentColorCalculator.PickTextOnPrimary (Seção 69:
    // "nunca fixada em branco ou preto por padrão" — ancoradas nos tons mais claro/escuro da
    // paleta do app em vez disso). Duplicadas aqui porque são `private` no calculador; o teste
    // verifica a escolha ENTRE essas duas opções reais, não contra branco/preto puro.
    private static readonly Color LightTextAnchor = Color.FromRgb(0xF2, 0xEC, 0xE5);
    private static readonly Color DarkTextAnchor = Color.FromRgb(0x18, 0x16, 0x14);

    [Theory]
    [InlineData("#0A0A2A")] // azul muito escuro
    [InlineData("#FFF5D6")] // amarelo muito claro
    [InlineData("#7129D3")] // roxo médio (padrão de fábrica)
    [InlineData("#FF0000")]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    public void Derive_TextOnPrimary_AlwaysPicksTheHigherContrastOption(string hex)
    {
        AccentColorCalculator.TryParseHex(hex, out var baseColor);

        foreach (var isDark in new[] { true, false })
        {
            var variations = AccentColorCalculator.Derive(baseColor, isDark);

            var contrastWithLight = ContrastRatio(variations.Primary, LightTextAnchor);
            var contrastWithDark = ContrastRatio(variations.Primary, DarkTextAnchor);
            var expected = contrastWithLight >= contrastWithDark ? LightTextAnchor : DarkTextAnchor;

            Assert.Equal(expected, variations.TextOnPrimary);
        }
    }

    [Fact]
    public void Derive_Subtle_Is_TranslucentVersionOfPrimary()
    {
        AccentColorCalculator.TryParseHex("#7129D3", out var baseColor);
        var variations = AccentColorCalculator.Derive(baseColor, isDarkTheme: true);

        Assert.Equal(variations.Primary.R, variations.Subtle.R);
        Assert.Equal(variations.Primary.G, variations.Subtle.G);
        Assert.Equal(variations.Primary.B, variations.Subtle.B);
        Assert.True(variations.Subtle.A < 255, "Subtle deveria ser translúcido.");
    }

    private static double ContrastRatio(Color a, Color b)
    {
        double la = RelativeLuminance(a) + 0.05;
        double lb = RelativeLuminance(b) + 0.05;
        return la > lb ? la / lb : lb / la;
    }

    private static double RelativeLuminance(Color c)
    {
        double r = Linearize(c.R / 255.0);
        double g = Linearize(c.G / 255.0);
        double b = Linearize(c.B / 255.0);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static double Linearize(double v) =>
        v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
}
