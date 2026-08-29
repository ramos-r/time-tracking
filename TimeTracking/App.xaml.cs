using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Data;
using TimeTracking.Helpers;
using TimeTracking.Repositories;
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
