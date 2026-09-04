using System.Windows;
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

    private readonly ITimerService _timerService;

    public MainWindow(MainViewModel viewModel, ITimerService timerService)
    {
        InitializeComponent();
        DataContext = viewModel;

        _timerService = timerService;
        // Troca o ícone da barra de tarefas quando há uma tarefa em execução (Seção 71,
        // feedback de usuário) — assinado aqui em vez de reagir a polling porque o evento já
        // dispara exatamente quando Start/Pause muda a tarefa ativa (Seção 15).
        _timerService.ActiveTaskChanged += OnActiveTaskChanged;
        _ = RefreshIconAsync();
    }

    private async void OnActiveTaskChanged() => await RefreshIconAsync();

    private async Task RefreshIconAsync()
    {
        var activeTask = await _timerService.GetActiveTaskAsync();
        Icon = activeTask is not null ? WorkingIcon : DefaultIcon;
    }
}
