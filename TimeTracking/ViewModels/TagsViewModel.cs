using CommunityToolkit.Mvvm.ComponentModel;

namespace TimeTracking.ViewModels;

public partial class TagsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Tags";
}
