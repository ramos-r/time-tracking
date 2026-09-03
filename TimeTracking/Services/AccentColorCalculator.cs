using System.Windows.Media;

namespace TimeTracking.Services;

/// <summary>Variações derivadas de uma cor de destaque-base, prontas para virar brushes
/// (Seção 69, "Regras de derivação das variações").</summary>
public record AccentVariations(Color Primary, Color Hover, Color Pressed, Color Subtle, Color TextOnPrimary);

/// <summary>
/// Cálculo puro (sem estado, sem I/O) por trás da cor de destaque personalizável (Seção 69).
/// Fica separado do AccentColorService para poder ser testado isoladamente (mesmo espírito
/// de TaskDayGroupBuilder para a Seção 68) — a Seção 5 ("Regra importante") pede que esse
/// tipo de regra de negócio não fique na View nem na ViewModel; aqui ele fica no Services,
/// mas como funções puras em vez de dentro do AccentColorService, que cuida apenas de
/// orquestração (persistência + publicação de resources).
/// </summary>
public static class AccentColorCalculator
{
    // Ancoram TextOnPrimary aos tons mais claro/escuro já usados na paleta do app (Seções 28-29)
    // em vez de branco/preto puro — mantém a mesma "temperatura" visual do resto do tema.
    private static readonly Color LightText = Color.FromRgb(0xF2, 0xEC, 0xE5);
    private static readonly Color DarkText = Color.FromRgb(0x18, 0x16, 0x14);

    public static bool TryParseHex(string hex, out Color color)
    {
        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted is Color c)
            {
                color = Color.FromRgb(c.R, c.G, c.B);
                return true;
            }
        }
        catch (FormatException)
        {
            // hex inválido — cai no retorno abaixo.
        }

        color = default;
        return false;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Calcula as variações de uma cor-base para o tema ativo (Seção 69). O usuário
    /// escolhe apenas matiz+saturação; a luminosidade de Primary é sempre recalculada aqui
    /// (nunca a do hex de entrada), o que também corrige de forma automática uma cor-base
    /// ilegível (ex.: matiz muito escuro escolhido com o tema Dark ativo).</summary>
    public static AccentVariations Derive(Color baseColor, bool isDarkTheme)
    {
        var (h, s, l) = RgbToHsl(baseColor);

        double minL = isDarkTheme ? 0.50 : 0.28;
        double maxL = isDarkTheme ? 0.78 : 0.50;
        double primaryL = Math.Clamp(l, minL, maxL);
        var primary = HslToRgb(h, s, primaryL);

        // Hover/Pressed avançam na direção que mais contrasta com o fundo do tema: mais claro
        // no Dark (fundo escuro → acento claro se destaca mais), mais escuro no Light.
        double direction = isDarkTheme ? 1.0 : -1.0;
        var hover = HslToRgb(h, s, Math.Clamp(primaryL + direction * 0.08, 0.05, 0.95));
        var pressed = HslToRgb(h, s, Math.Clamp(primaryL + direction * 0.15, 0.05, 0.95));

        var subtle = Color.FromArgb(0x26, primary.R, primary.G, primary.B);

        var textOnPrimary = PickTextOnPrimary(primary);

        return new AccentVariations(primary, hover, pressed, subtle, textOnPrimary);
    }

    /// <summary>Escolhe entre um texto claro ou escuro por razão de contraste (luminância
    /// relativa, padrão WCAG) — nunca fixo por tema, sempre calculado a partir da cor de
    /// destaque real, já que ela pode ser qualquer matiz.</summary>
    private static Color PickTextOnPrimary(Color background)
    {
        var contrastWithLight = ContrastRatio(background, LightText);
        var contrastWithDark = ContrastRatio(background, DarkText);
        return contrastWithLight >= contrastWithDark ? LightText : DarkText;
    }

    private static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a) + 0.05;
        var lb = RelativeLuminance(b) + 0.05;
        return la > lb ? la / lb : lb / la;
    }

    private static double RelativeLuminance(Color c)
    {
        double r = LinearizeChannel(c.R / 255.0);
        double g = LinearizeChannel(c.G / 255.0);
        double b = LinearizeChannel(c.B / 255.0);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static double LinearizeChannel(double v) =>
        v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    public static (double H, double S, double L) RgbToHsl(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0;

        if (max == min)
        {
            return (0, 0, l);
        }

        double d = max - min;
        double s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        double h;
        if (max == r)
        {
            h = (g - b) / d + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            h = (b - r) / d + 2;
        }
        else
        {
            h = (r - g) / d + 4;
        }

        return (h / 6.0, s, l);
    }

    public static Color HslToRgb(double h, double s, double l)
    {
        double r, g, b;
        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = HueToChannel(p, q, h + 1.0 / 3.0);
            g = HueToChannel(p, q, h);
            b = HueToChannel(p, q, h - 1.0 / 3.0);
        }

        return Color.FromRgb(
            (byte)Math.Round(Math.Clamp(r, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(g, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(b, 0, 1) * 255));
    }

    private static double HueToChannel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }
}
