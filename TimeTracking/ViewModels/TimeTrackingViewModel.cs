using CommunityToolkit.Mvvm.ComponentModel;

namespace TimeTracking.ViewModels;

public partial class TimeTrackingViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Time Tracking";
}
