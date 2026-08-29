using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTracking.Services;

namespace TimeTracking.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private bool _isSidebarOpen;

    [ObservableProperty]
    private AppPage _currentPage;

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private object? _currentViewModel;

    public MainViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
        NavigateTo(AppPage.TimeTracking);
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

    [RelayCommand]
    private void CloseSidebar() => IsSidebarOpen = false;

    [RelayCommand]
    private void NavigateTo(AppPage page)
    {
        CurrentPage = page;
        PageTitle = page switch
        {
            AppPage.TimeTracking => "Time Tracking",
            AppPage.Tags => "Tags",
            AppPage.Pomodoro => "Pomodoro",
            AppPage.Settings => "Settings",
            _ => string.Empty
        };
        CurrentViewModel = _navigationService.ResolveViewModel(page);
        IsSidebarOpen = false;
    }
}
