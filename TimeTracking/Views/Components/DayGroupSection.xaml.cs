using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using TimeTracking.ViewModels;

namespace TimeTracking.Views.Components;

public partial class DayGroupSection : UserControl
{
    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(200);

    private DayGroupViewModel? _viewModel;
    private bool _initialized;

    public DayGroupSection()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as DayGroupViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ChevronRotate.Angle = _viewModel.IsExpanded ? 180 : 0;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized || _viewModel is null)
        {
            return;
        }

        _initialized = true;
        SetContentState(_viewModel.IsExpanded, animate: false);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DayGroupViewModel.IsExpanded))
        {
            return;
        }

        var expanded = _viewModel!.IsExpanded;
        SetContentState(expanded, animate: true);

        var rotateAnimation = new DoubleAnimation
        {
            To = expanded ? 180 : 0,
            Duration = TransitionDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        ChevronRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, rotateAnimation);
    }

    /// <summary>Altura e opacidade não têm "Auto" animável no WPF — por isso a altura-alvo é
    /// medida aqui e as duas propriedades são animadas juntas (Seção 68 — ajuste visual,
    /// item 4: transição suave e discreta, sem bounce).</summary>
    private void SetContentState(bool expanded, bool animate)
    {
        var availableWidth = TasksHost.ActualWidth > 0 ? TasksHost.ActualWidth : ContentHost.ActualWidth;
        TasksHost.Measure(new Size(availableWidth > 0 ? availableWidth : double.PositiveInfinity, double.PositiveInfinity));
        var targetHeight = expanded ? TasksHost.DesiredSize.Height : 0;
        var targetOpacity = expanded ? 1d : 0d;

        ContentHost.BeginAnimation(HeightProperty, null);
        ContentHost.BeginAnimation(OpacityProperty, null);

        if (!animate)
        {
            ContentHost.Height = targetHeight;
            ContentHost.Opacity = targetOpacity;
            return;
        }

        var easing = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

        ContentHost.BeginAnimation(HeightProperty, new DoubleAnimation
        {
            To = targetHeight,
            Duration = TransitionDuration,
            EasingFunction = easing
        });

        ContentHost.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            To = targetOpacity,
            Duration = TransitionDuration,
            EasingFunction = easing
        });
    }
}
