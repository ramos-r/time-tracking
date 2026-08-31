using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTracking.Services;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IThemeService _themeService;
    private readonly ITaskService _taskService;

    [ObservableProperty]
    private AppTheme _selectedTheme;

    [ObservableProperty]
    private bool _isClearHistoryConfirmOpen;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isStatusError;

    public string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public SettingsViewModel(IThemeService themeService, ITaskService taskService)
    {
        _themeService = themeService;
        _taskService = taskService;
        _selectedTheme = themeService.CurrentTheme;
    }

    partial void OnSelectedThemeChanged(AppTheme value) => _themeService.ApplyTheme(value);

    [RelayCommand]
    private void RequestClearHistory()
    {
        StatusMessage = null;
        IsClearHistoryConfirmOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmClearHistoryAsync()
    {
        try
        {
            await _taskService.ClearHistoryAsync();
            StatusMessage = "Sucesso.";
            IsStatusError = false;
        }
        catch (Exception)
        {
            StatusMessage = "Não foi possível limpar o histórico. Tente novamente.";
            IsStatusError = true;
        }

        IsClearHistoryConfirmOpen = false;
    }

    [RelayCommand]
    private void CancelClearHistory() => IsClearHistoryConfirmOpen = false;
}
