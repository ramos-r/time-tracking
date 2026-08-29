using Microsoft.Extensions.DependencyInjection;
using TimeTracking.ViewModels;

namespace TimeTracking.Services;

public class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object ResolveViewModel(AppPage page) => page switch
    {
        AppPage.TimeTracking => _serviceProvider.GetRequiredService<TimeTrackingViewModel>(),
        AppPage.Tags => _serviceProvider.GetRequiredService<TagsViewModel>(),
        AppPage.Pomodoro => _serviceProvider.GetRequiredService<PomodoroViewModel>(),
        AppPage.Settings => _serviceProvider.GetRequiredService<SettingsViewModel>(),
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, "Página desconhecida.")
    };
}
