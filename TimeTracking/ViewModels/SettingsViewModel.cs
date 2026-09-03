using System.Reflection;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTracking.Services;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private static readonly Regex HexColorRegex = new(@"^#[0-9A-Fa-f]{6}$");

    private readonly IThemeService _themeService;
    private readonly IAccentColorService _accentColorService;
    private readonly ITaskService _taskService;

    // Verdadeiro só enquanto SelectedHue/SelectedSaturation estão sendo escritos por
    // LoadAccentColorFromCurrent() — evita que o próprio carregamento reentre em
    // ApplyAccentColorFromHsl() e sobrescreva CurrentAccentHex com uma L neutra (Seção 69,
    // "usuário escolhe apenas matiz+saturação").
    private bool _isSyncingFromAccentColor;

    [ObservableProperty]
    private AppTheme _selectedTheme;

    [ObservableProperty]
    private bool _isClearHistoryConfirmOpen;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private string _selectedAccentHex = string.Empty;

    [ObservableProperty]
    private bool _isCustomizePanelOpen;

    [ObservableProperty]
    private double _selectedHue;

    [ObservableProperty]
    private double _selectedSaturation;

    [ObservableProperty]
    private string _customHexInput = string.Empty;

    [ObservableProperty]
    private string? _customHexError;

    public IReadOnlyList<string> AccentSwatches => _accentColorService.PredefinedSwatches;

    public string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public SettingsViewModel(IThemeService themeService, IAccentColorService accentColorService, ITaskService taskService)
    {
        _themeService = themeService;
        _accentColorService = accentColorService;
        _taskService = taskService;
        _selectedTheme = themeService.CurrentTheme;

        LoadAccentColorFromCurrent();
    }

    partial void OnSelectedThemeChanged(AppTheme value) => _themeService.ApplyTheme(value);

    private void LoadAccentColorFromCurrent()
    {
        _isSyncingFromAccentColor = true;

        _selectedAccentHex = _accentColorService.CurrentAccentHex;
        OnPropertyChanged(nameof(SelectedAccentHex));

        if (AccentColorCalculator.TryParseHex(_accentColorService.CurrentAccentHex, out var color))
        {
            var (h, s, _) = AccentColorCalculator.RgbToHsl(color);
            _selectedHue = h * 360.0;
            _selectedSaturation = s * 100.0;
            OnPropertyChanged(nameof(SelectedHue));
            OnPropertyChanged(nameof(SelectedSaturation));
        }

        _customHexInput = _accentColorService.CurrentAccentHex;
        OnPropertyChanged(nameof(CustomHexInput));

        _isSyncingFromAccentColor = false;
    }

    [RelayCommand]
    private void SelectAccentSwatch(string hex)
    {
        _accentColorService.ApplyAccentColor(hex);
        LoadAccentColorFromCurrent();
    }

    [RelayCommand]
    private void ToggleCustomizePanel() => IsCustomizePanelOpen = !IsCustomizePanelOpen;

    partial void OnSelectedHueChanged(double value) => ApplyAccentColorFromHsl();

    partial void OnSelectedSaturationChanged(double value) => ApplyAccentColorFromHsl();

    private void ApplyAccentColorFromHsl()
    {
        if (_isSyncingFromAccentColor)
        {
            return;
        }

        // Luminosidade fixa em 0.5 apenas para compor o hex-base persistido — a luminosidade
        // realmente exibida é sempre recalculada por AccentColorCalculator.Derive() a partir
        // do tema ativo (Seção 69, "usuário escolhe apenas matiz+saturação").
        var color = AccentColorCalculator.HslToRgb(SelectedHue / 360.0, SelectedSaturation / 100.0, 0.5);
        var hex = AccentColorCalculator.ToHex(color);

        _accentColorService.ApplyAccentColor(hex);

        _isSyncingFromAccentColor = true;
        _selectedAccentHex = hex;
        OnPropertyChanged(nameof(SelectedAccentHex));
        _customHexInput = hex;
        OnPropertyChanged(nameof(CustomHexInput));
        _isSyncingFromAccentColor = false;
    }

    partial void OnCustomHexInputChanged(string value)
    {
        if (_isSyncingFromAccentColor)
        {
            return;
        }

        if (!HexColorRegex.IsMatch(value))
        {
            CustomHexError = "Cor inválida (use #RRGGBB).";
            return;
        }

        CustomHexError = null;
        _accentColorService.ApplyAccentColor(value);
        LoadAccentColorFromCurrent();
    }

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
