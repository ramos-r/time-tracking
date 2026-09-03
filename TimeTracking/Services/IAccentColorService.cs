namespace TimeTracking.Services;

public interface IAccentColorService
{
    /// <summary>Padrão de fábrica: roxo/lilás da referência visual (interface-ref.png, Seção 31).</summary>
    const string DefaultAccentHex = "#7129D3";

    /// <summary>Leque de swatches predefinidos (Seção 69, item 1) — o primeiro é o padrão de fábrica.</summary>
    IReadOnlyList<string> PredefinedSwatches { get; }

    /// <summary>Cor-base de destaque atualmente escolhida (hex, matiz+saturação do usuário —
    /// a luminosidade exibida é recalculada por tema, nunca é este valor bruto).</summary>
    string CurrentAccentHex { get; }

    /// <summary>Carrega a preferência salva (ou o padrão de fábrica) e publica as brushes
    /// derivadas para o tema efetivo atual. Deve ser chamado uma vez, após IThemeService.Initialize().</summary>
    void Initialize();

    /// <summary>Troca a cor de destaque em runtime (Seção 69, item 3): persiste a escolha,
    /// recalcula as variações (Hover/Pressed/Subtle/TextOnPrimary) para o tema efetivo atual,
    /// e publica as brushes — os bindings DynamicResource nas Views atualizam sozinhos.</summary>
    void ApplyAccentColor(string hex);
}
