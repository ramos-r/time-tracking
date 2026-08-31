using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TimeTracking.Views.Components;

public partial class ConfirmDialog : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ConfirmDialog));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(ConfirmDialog));

    public static readonly DependencyProperty ConfirmTextProperty =
        DependencyProperty.Register(nameof(ConfirmText), typeof(string), typeof(ConfirmDialog), new PropertyMetadata("Confirmar"));

    public static readonly DependencyProperty CancelTextProperty =
        DependencyProperty.Register(nameof(CancelText), typeof(string), typeof(ConfirmDialog), new PropertyMetadata("Cancelar"));

    public static readonly DependencyProperty ConfirmCommandProperty =
        DependencyProperty.Register(nameof(ConfirmCommand), typeof(ICommand), typeof(ConfirmDialog));

    public static readonly DependencyProperty CancelCommandProperty =
        DependencyProperty.Register(nameof(CancelCommand), typeof(ICommand), typeof(ConfirmDialog));

    /// <summary>Quando true (padrão), o botão de confirmação usa a cor de perigo — para ações
    /// destrutivas (excluir, limpar histórico). Quando false, usa a cor primária (ex.: o
    /// diálogo de conflito de timer da Seção 15, que não é uma ação destrutiva).</summary>
    public static readonly DependencyProperty IsDestructiveProperty =
        DependencyProperty.Register(nameof(IsDestructive), typeof(bool), typeof(ConfirmDialog), new PropertyMetadata(true));

    public bool IsDestructive
    {
        get => (bool)GetValue(IsDestructiveProperty);
        set => SetValue(IsDestructiveProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string ConfirmText
    {
        get => (string)GetValue(ConfirmTextProperty);
        set => SetValue(ConfirmTextProperty, value);
    }

    public string CancelText
    {
        get => (string)GetValue(CancelTextProperty);
        set => SetValue(CancelTextProperty, value);
    }

    public ICommand? ConfirmCommand
    {
        get => (ICommand?)GetValue(ConfirmCommandProperty);
        set => SetValue(ConfirmCommandProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => (ICommand?)GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public ConfirmDialog()
    {
        InitializeComponent();
    }
}
