using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TimeTracking.Views.Components;

/// <summary>Campo numérico com botões − e +. Só mexe no texto (um inteiro dentro de
/// Minimum..Maximum); quem valida e grava é o ViewModel que está ligado a Text.</summary>
public partial class NumberStepper : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(NumberStepper),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, _) => ((NumberStepper)d).UpdateButtons()));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(NumberStepper),
            new PropertyMetadata(0, (d, _) => ((NumberStepper)d).UpdateButtons()));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(NumberStepper),
            new PropertyMetadata(999, (d, _) => ((NumberStepper)d).UpdateButtons()));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public NumberStepper()
    {
        InitializeComponent();
    }

    private void Decrement_Click(object sender, RoutedEventArgs e) => Step(-1);

    private void Increment_Click(object sender, RoutedEventArgs e) => Step(1);

    private void Step(int delta)
    {
        // Um texto ainda não confirmado no campo (digitado, sem sair dele) vale como ponto de partida.
        var source = ValueBox.Text;
        if (!int.TryParse(source?.Trim(), out var value))
        {
            return;
        }

        Text = Math.Clamp(value + delta, Minimum, Maximum).ToString();
    }

    private void UpdateButtons()
    {
        if (DecrementButton is null || IncrementButton is null)
        {
            return;
        }

        var hasValue = int.TryParse(Text?.Trim(), out var value);
        DecrementButton.IsEnabled = !hasValue || value > Minimum;
        IncrementButton.IsEnabled = !hasValue || value < Maximum;
    }

    /// <summary>Enter aplica o valor digitado na hora (sem precisar sair do campo).</summary>
    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ValueBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }
    }
}
