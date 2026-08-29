using TimeTracking.ViewModels;

namespace TimeTracking.Services;

public interface INavigationService
{
    object ResolveViewModel(AppPage page);
}
