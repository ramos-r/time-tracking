using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using TimeTracking.Services;
using TimeTracking.ViewModels;

namespace TimeTracking.Views;

public partial class MainWindow : Window
{
    // BitmapFrame.Create (não "new BitmapImage(uri)"): preserva a referência ao Decoder
    // multi-frame do .ico, que é o que permite ao WPF escolher o frame de resolução certa
    // para cada contexto (barra de título vs. taskbar vs. Alt+Tab) ao montar o HICON nativo —
    // exatamente como o Icon="..." estático no XAML já fazia. Um BitmapImage decodifica só um
    // frame fixo (o primeiro do arquivo) e esse único tamanho é usado em todo lugar, o que
    // deixava o ícone pequeno demais nos contextos maiores (Seção 71, feedback de usuário).
    private static readonly BitmapFrame DefaultIcon = BitmapFrame.Create(new Uri("pack://application:,,,/Resources/Icons/AppIcon.ico"));
    private static readonly BitmapFrame WorkingIcon = BitmapFrame.Create(new Uri("pack://application:,,,/Resources/Icons/AppIconWorking.ico"));

    // DWMWA_USE_IMMERSIVE_DARK_MODE: atributo do DWM (Windows 10 20H1+) que pinta a barra de
    // título nativa (ícone, nome do app, botões minimizar/maximizar/fechar) no esquema escuro
    // do próprio Windows, em vez do branco padrão do SO — não dá para escolher uma cor exata
    // (não é um recurso de tema do WPF), só ligar/desligar o modo escuro nativo da barra
    // (Seção 71, feedback de usuário: a barra de título ficava sempre clara, destoando do
    // resto do app no tema Dark). Em versões do Windows sem esse atributo, a chamada falha
    // silenciosamente (retorno não-zero) e a barra permanece no padrão do SO — degradação
    // aceitável, não uma falha visível para o usuário.
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private readonly ITimerService _timerService;
    private readonly IThemeService _themeService;

    public MainWindow(MainViewModel viewModel, ITimerService timerService, IThemeService themeService)
    {
        InitializeComponent();
        DataContext = viewModel;

        _timerService = timerService;
        // Troca o ícone da barra de tarefas quando há uma tarefa em execução (Seção 71,
        // feedback de usuário) — assinado aqui em vez de reagir a polling porque o evento já
        // dispara exatamente quando Start/Pause muda a tarefa ativa (Seção 15).
        _timerService.ActiveTaskChanged += OnActiveTaskChanged;
        _ = RefreshIconAsync();

        _themeService = themeService;
        // Reaplica ao trocar de tema em tempo real (Settings) — SourceInitialized cobre a
        // primeira aplicação, já que o HWND nativo só existe a partir desse ponto (não no
        // construtor).
        _themeService.EffectiveThemeChanged += _ => ApplyTitleBarTheme();
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
    }

    private void ApplyTitleBarTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var useDarkMode = _themeService.EffectiveTheme == AppTheme.Light ? 0 : 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
    }

    private async void OnActiveTaskChanged() => await RefreshIconAsync();

    private async Task RefreshIconAsync()
    {
        var activeTask = await _timerService.GetActiveTaskAsync();
        Icon = activeTask is not null ? WorkingIcon : DefaultIcon;
    }
}
