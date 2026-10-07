using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Data;
using TimeTracking.Helpers;
using TimeTracking.Repositories;
using TimeTracking.Services;
using TimeTracking.ViewModels;
using TimeTracking.Views;

namespace TimeTracking;

public partial class App : Application
{
    private readonly ServiceProvider _serviceProvider;

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        var dbPath = DatabasePathProvider.GetDatabasePath();
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        services.AddSingleton<ITagRepository, TagRepository>();
        services.AddSingleton<ITaskRepository, TaskRepository>();
        services.AddSingleton<ITimeEntryRepository, TimeEntryRepository>();

        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<ITagService, TagService>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ITimerService, TimerService>();
        services.AddSingleton<AppSettingsStore>();
        services.AddSingleton<IPomodoroService, PomodoroService>();
        services.AddSingleton<INativeAlerts, WindowsAlerts>();
        services.AddSingleton<PhaseEndNotifier>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IAccentColorService, AccentColorService>();

        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<TimeTrackingViewModel>();
        services.AddSingleton<TagsViewModel>();
        services.AddSingleton<PomodoroViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // TaskEditorViewModel é transiente (uma instância nova por edição/criação); a
        // factory permite que TimeTrackingViewModel obtenha uma instância sem depender
        // diretamente do IServiceProvider (evita o anti-padrão service locator).
        services.AddTransient<TaskEditorViewModel>();
        services.AddSingleton<Func<TaskEditorViewModel>>(sp => () => sp.GetRequiredService<TaskEditorViewModel>());

        // Mesmo raciocínio para o editor de tags (Fase 7).
        services.AddTransient<TagEditorViewModel>();
        services.AddSingleton<Func<TagEditorViewModel>>(sp => () => sp.GetRequiredService<TagEditorViewModel>());

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    // async void é o formato exigido de um override de evento; o try/catch evita que uma falha
    // na recuperação do Pomodoro derrube a abertura do app (v1.5.1, D6) — ela só é registrada
    // em log e o app abre com o ciclo no estado em que estiver.
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        using (var context = _serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
        {
            context.Database.Migrate();
        }

        // Aplica o tema antes de qualquer janela ser exibida, para não haver "flash" do tema errado.
        _serviceProvider.GetRequiredService<IThemeService>().Initialize();

        // A cor de destaque (Seção 69) depende do tema efetivo já estar aplicado — precisa
        // rodar depois do ThemeService.Initialize(), também antes de qualquer janela abrir.
        _serviceProvider.GetRequiredService<IAccentColorService>().Initialize();

        // O Pomodoro é criado já no startup (singleton resolvido aqui), e não na primeira
        // navegação, para reagir ao timer mesmo com o usuário em outra tela (Seção 70). A
        // recuperação roda depois da migração, para encontrar o estado real das sessões
        // abertas (Seção 16), e é aguardada sem bloquear a thread de UI.
        try
        {
            await _serviceProvider.GetRequiredService<IPomodoroService>().RecoverOnStartupAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Falha na recuperação do Pomodoro ao iniciar.", ex);
        }

        // O PomodoroViewModel hospeda o tick de 1 segundo: resolvê-lo aqui (e não na primeira
        // navegação para a tela) faz o fim da fase ser detectado com o usuário em qualquer tela.
        _serviceProvider.GetRequiredService<PomodoroViewModel>();

        // O aviso de fim de fase (som + barra de tarefas) só existe enquanto estiver assinado ao
        // evento do serviço — resolvido aqui, depois da recuperação (que não avisa), para valer
        // desde o primeiro tick.
        _serviceProvider.GetRequiredService<PhaseEndNotifier>();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider.Dispose();
        base.OnExit(e);
    }
}
