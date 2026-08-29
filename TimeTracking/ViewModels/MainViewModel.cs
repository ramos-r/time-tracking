using CommunityToolkit.Mvvm.ComponentModel;

namespace TimeTracking.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Time Tracking";

    [ObservableProperty]
    private string _statusMessage = "Fase 1 — fundação do projeto (WPF + MVVM + DI) concluída.";
}
