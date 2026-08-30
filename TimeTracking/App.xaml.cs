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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        using (var context = _serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
        {
            context.Database.Migrate();
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider.Dispose();
        base.OnExit(e);
    }
}
