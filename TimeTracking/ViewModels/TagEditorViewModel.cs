using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTracking.Services;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.ViewModels;

/// <summary>ViewModel do painel de criação/edição de tag (Seção 24/25).</summary>
public partial class TagEditorViewModel : ObservableObject
{
    private static readonly Regex HexColorRegex = new(@"^#[0-9A-Fa-f]{6}$");

    private readonly ITagService _tagService;
    private int? _editingTagId;

    [ObservableProperty]
    private bool _isNew;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _nameError;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string _color = PaletteColors[0];

    [ObservableProperty]
    private string? _colorError;

    [ObservableProperty]
    private string? _errorMessage;

    public string Title => IsNew ? "Nova tag" : "Editar tag";

    public static IReadOnlyList<string> PaletteColors { get; } = new[]
    {
        "#C89B6D", "#7FAE82", "#6D8FC8", "#C86D9B",
        "#D3A85C", "#7FC8AE", "#C87575", "#9B7FC8"
    };

    public event Action? Saved;
    public event Action? CloseRequested;

    public TagEditorViewModel(ITagService tagService)
    {
        _tagService = tagService;
    }

    public Task LoadForNewAsync()
    {
        _editingTagId = null;
        IsNew = true;
        Name = string.Empty;
        Description = null;
        Color = PaletteColors[0];
        NameError = null;
        ColorError = null;
        ErrorMessage = null;
        return Task.CompletedTask;
    }

    public async Task LoadForEditAsync(int tagId)
    {
        var tag = await _tagService.GetByIdAsync(tagId)
            ?? throw new InvalidOperationException($"Tag {tagId} não encontrada.");

        _editingTagId = tagId;
        IsNew = false;
        Name = tag.Name;
        Description = tag.Description;
        Color = tag.Color;
        NameError = null;
        ColorError = null;
        ErrorMessage = null;
    }

    partial void OnNameChanged(string value) => ValidateName();

    partial void OnColorChanged(string value) => ValidateColor();

    partial void OnIsNewChanged(bool value) => OnPropertyChanged(nameof(Title));

    [RelayCommand]
    private void SelectColor(string hex) => Color = hex;

    private bool ValidateName()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            NameError = "Nome é obrigatório.";
        }
        else if (Name.Trim().Length > 100)
        {
            NameError = "Nome deve ter no máximo 100 caracteres.";
        }
        else
        {
            NameError = null;
        }

        SaveCommand.NotifyCanExecuteChanged();
        return NameError is null;
    }

    private bool ValidateColor()
    {
        ColorError = HexColorRegex.IsMatch(Color) ? null : "Cor inválida (use #RRGGBB).";
        SaveCommand.NotifyCanExecuteChanged();
        return ColorError is null;
    }

    private bool CanSave() => string.IsNullOrEmpty(NameError) && string.IsNullOrEmpty(ColorError);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var nameValid = ValidateName();
        var colorValid = ValidateColor();
        if (!nameValid || !colorValid)
        {
            return;
        }

        try
        {
            ErrorMessage = null;

            if (IsNew)
            {
                await _tagService.CreateAsync(Name, Description, Color);
            }
            else
            {
                await _tagService.UpdateAsync(_editingTagId!.Value, Name, Description, Color);
            }

            Saved?.Invoke();
        }
        catch (Exception)
        {
            ErrorMessage = "Não foi possível salvar a tag. Verifique os dados e tente novamente.";
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}
