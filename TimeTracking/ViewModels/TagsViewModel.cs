using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTracking.Models;
using TimeTracking.Services;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.ViewModels;

public partial class TagsViewModel : ObservableObject
{
    private readonly ITagService _tagService;
    private readonly Func<TagEditorViewModel> _editorFactory;

    [ObservableProperty]
    private ObservableCollection<Tag> _tags = new();

    [ObservableProperty]
    private bool _isEditorOpen;

    [ObservableProperty]
    private TagEditorViewModel? _editor;

    [ObservableProperty]
    private Tag? _pendingDelete;

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [ObservableProperty]
    private string? _listErrorMessage;

    public string DeleteConfirmMessage =>
        $"Tem certeza que deseja excluir a tag \"{PendingDelete?.Name}\"? As tarefas associadas serão mantidas, apenas sem essa tag.";

    public TagsViewModel(ITagService tagService, Func<TagEditorViewModel> editorFactory)
    {
        _tagService = tagService;
        _editorFactory = editorFactory;
        _ = LoadTagsAsync();
    }

    [RelayCommand]
    private async Task LoadTagsAsync()
    {
        try
        {
            ListErrorMessage = null;
            var tags = await _tagService.GetAllAsync();
            Tags = new ObservableCollection<Tag>(tags);
        }
        catch (Exception)
        {
            ListErrorMessage = "Não foi possível carregar as tags.";
        }
    }

    [RelayCommand]
    private async Task OpenNewTagAsync()
    {
        var editor = _editorFactory();
        AttachEditorHandlers(editor);
        await editor.LoadForNewAsync();
        Editor = editor;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private async Task SelectTagAsync(Tag tag)
    {
        var editor = _editorFactory();
        AttachEditorHandlers(editor);
        await editor.LoadForEditAsync(tag.Id);
        Editor = editor;
        IsEditorOpen = true;
    }

    private void AttachEditorHandlers(TagEditorViewModel editor)
    {
        editor.Saved += OnEditorSaved;
        editor.CloseRequested += OnEditorClosed;
    }

    private void DetachEditorHandlers(TagEditorViewModel editor)
    {
        editor.Saved -= OnEditorSaved;
        editor.CloseRequested -= OnEditorClosed;
    }

    private async void OnEditorSaved()
    {
        CloseEditor();
        await LoadTagsAsync();
    }

    private void OnEditorClosed() => CloseEditor();

    [RelayCommand]
    private void CloseEditor()
    {
        if (Editor is not null)
        {
            DetachEditorHandlers(Editor);
        }

        IsEditorOpen = false;
        Editor = null;
    }

    [RelayCommand]
    private void RequestDelete(Tag tag)
    {
        PendingDelete = tag;
        OnPropertyChanged(nameof(DeleteConfirmMessage));
        IsDeleteConfirmOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (PendingDelete is not null)
        {
            try
            {
                await _tagService.DeleteAsync(PendingDelete.Id);
                await LoadTagsAsync();
            }
            catch (Exception)
            {
                ListErrorMessage = "Não foi possível excluir a tag. Tente novamente.";
            }
        }

        CancelDelete();
    }

    [RelayCommand]
    private void CancelDelete()
    {
        PendingDelete = null;
        IsDeleteConfirmOpen = false;
    }
}
