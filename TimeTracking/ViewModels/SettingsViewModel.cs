using CommunityToolkit.Mvvm.ComponentModel;

namespace TimeTracking.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Settings";
}
