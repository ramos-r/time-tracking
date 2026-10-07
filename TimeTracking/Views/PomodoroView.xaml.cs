using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using TimeTracking.ViewModels;

namespace TimeTracking.Views;

public partial class PomodoroView : UserControl
{
    // Mesmas durações da aba de edição do Time Tracking (abre 0.18 s, fecha 0.15 s).
    private static readonly Duration OpenDuration = new(TimeSpan.FromSeconds(0.18));
    private static readonly Duration CloseDuration = new(TimeSpan.FromSeconds(0.15));

    private PomodoroViewModel? _viewModel;

    public PomodoroView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Scroll.SizeChanged += (_, _) => Recenter();
        MainBlock.SizeChanged += (_, _) => Recenter();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PomodoroViewModel viewModel)
        {
            return;
        }

        _viewModel = viewModel;

        // O ViewModel é um singleton que vive desde o startup, mas esta View é criada de novo a
        // cada navegação: a aba de configurações sempre abre recolhida, e a lista de tarefas é
        // recarregada para refletir tarefas criadas ou excluídas em outras telas.
        viewModel.IsSettingsOpen = false;
        ApplySettingsState(open: false, animate: false);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Recenter();
        await viewModel.RefreshTasksAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // O ViewModel sobrevive à View — sem desassinar, cada visita à tela deixaria um
        // handler pendurado.
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PomodoroViewModel.IsSettingsOpen) && _viewModel is not null)
        {
            ApplySettingsState(_viewModel.IsSettingsOpen, animate: true);
        }
    }

    /// <summary>Centraliza o bloco principal na vertical (a horizontal já é centralizada pelo
    /// próprio painel). A aba de configurações não conta: ela cresce para baixo a partir do bloco,
    /// então abrir ou fechar a aba nunca move o que está acima.</summary>
    private void Recenter()
    {
        var top = Math.Max(16, (Scroll.ActualHeight - MainBlock.ActualHeight) / 2);
        var current = RootStack.Margin;
        if (Math.Abs(current.Top - top) > 0.5)
        {
            RootStack.Margin = new Thickness(current.Left, top, current.Right, current.Bottom);
        }
    }

    private void ApplySettingsState(bool open, bool animate)
    {
        // Altura final = a do conteúdo medido com a largura real do painel.
        var width = RootStack.ActualWidth > 0 ? RootStack.ActualWidth : MaxWidthFallback;
        SettingsContent.Measure(new Size(width, double.PositiveInfinity));
        var target = open ? SettingsContent.DesiredSize.Height : 0;

        if (!animate)
        {
            SettingsHost.BeginAnimation(HeightProperty, null);
            SettingsHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            SettingsHost.Height = open ? double.NaN : 0;
            return;
        }

        // From explícito: BeginAnimation sem ele parte do valor-base antigo (0 ou NaN) e pula.
        var from = SettingsHost.Visibility == Visibility.Visible ? SettingsHost.ActualHeight : 0;
        SettingsHost.Visibility = Visibility.Visible;

        var animation = new DoubleAnimation(from, target, open ? OpenDuration : CloseDuration);
        animation.Completed += (_, _) =>
        {
            SettingsHost.BeginAnimation(HeightProperty, null);
            if (open)
            {
                // Volta ao automático: se uma mensagem de erro aparecer, a aba acompanha.
                SettingsHost.Height = double.NaN;
                // Só rola se a aba abriu parcialmente fora da tela (janela baixa).
                SettingsHost.BringIntoView();
            }
            else
            {
                SettingsHost.Height = 0;
                SettingsHost.Visibility = Visibility.Collapsed;
            }
        };
        SettingsHost.BeginAnimation(HeightProperty, animation);
    }

    private const double MaxWidthFallback = 392;
}
