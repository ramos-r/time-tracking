namespace TimeTracking.Services;

public interface IThemeService
{
    /// <summary>Preferência atual do usuário (pode ser System — não necessariamente o tema
    /// efetivamente aplicado na tela).</summary>
    AppTheme CurrentTheme { get; }

    /// <summary>Carrega a preferência salva (ou o padrão) e aplica o dicionário de recursos
    /// correspondente. Deve ser chamado uma vez, antes de qualquer janela ser exibida.</summary>
    void Initialize();

    /// <summary>Troca o tema em runtime (Seção 57): persiste a preferência e substitui o
    /// ResourceDictionary de tema — os bindings DynamicResource nas Views atualizam sozinhos,
    /// sem precisar reiniciar a aplicação.</summary>
    void ApplyTheme(AppTheme theme);
}
