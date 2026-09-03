namespace TimeTracking.Services;

public interface IThemeService
{
    /// <summary>Preferência atual do usuário (pode ser System — não necessariamente o tema
    /// efetivamente aplicado na tela).</summary>
    AppTheme CurrentTheme { get; }

    /// <summary>Tema efetivamente aplicado (Dark ou Light — nunca System; "Sistema" já vem
    /// resolvido pelo registro do Windows). Seção 69: o AccentColorService usa isso para saber
    /// se deve clarear ou escurecer a cor de destaque.</summary>
    AppTheme EffectiveTheme { get; }

    /// <summary>Disparado sempre que o tema efetivo muda (troca manual ou Initialize). Seção 69:
    /// o AccentColorService assina este evento para recalcular as variações da cor de destaque
    /// quando o tema muda, já que a mesma cor-base exige luminosidades diferentes em cada tema.</summary>
    event Action<AppTheme>? EffectiveThemeChanged;

    /// <summary>Carrega a preferência salva (ou o padrão) e aplica o dicionário de recursos
    /// correspondente. Deve ser chamado uma vez, antes de qualquer janela ser exibida.</summary>
    void Initialize();

    /// <summary>Troca o tema em runtime (Seção 57): persiste a preferência e substitui o
    /// ResourceDictionary de tema — os bindings DynamicResource nas Views atualizam sozinhos,
    /// sem precisar reiniciar a aplicação.</summary>
    void ApplyTheme(AppTheme theme);
}
