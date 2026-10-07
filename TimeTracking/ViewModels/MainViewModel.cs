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

    /// <summary>A barra superior (MainWindow.xaml) só mostra o título textual da página fora
    /// de Time Tracking e Tags — ambas já têm seu próprio título no cabeçalho do conteúdo
    /// (a logo em Time Tracking, o "Tags" ao lado do botão "Nova tag" em Tags), o texto
    /// ficaria redundante. Settings também repete o título no próprio conteúdo, mas isso
    /// não foi pedido para ela ainda; a tela Pomodoro (Fase 12B) não repete — o título fica aqui.</summary>
    public bool ShowPageTitle => CurrentPage != AppPage.TimeTracking && CurrentPage != AppPage.Tags;

    /// <summary>Pomodoro, exposto para o chip da barra superior (D9).</summary>
    public PomodoroViewModel Pomodoro { get; }

    /// <summary>Chip discreto na barra superior avisando que há um Pomodoro em andamento — só
    /// aparece fora da própria tela Pomodoro, onde já há o relógio grande (Seção 70, D9).</summary>
    public bool ShowPomodoroChip => Pomodoro.IsActive && CurrentPage != AppPage.Pomodoro;

    partial void OnCurrentPageChanged(AppPage value)
    {
        OnPropertyChanged(nameof(ShowPageTitle));
        OnPropertyChanged(nameof(ShowPomodoroChip));
    }

    public MainViewModel(INavigationService navigationService, PomodoroViewModel pomodoro)
    {
        _navigationService = navigationService;
        Pomodoro = pomodoro;
        Pomodoro.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PomodoroViewModel.IsActive))
            {
                OnPropertyChanged(nameof(ShowPomodoroChip));
            }
        };

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
