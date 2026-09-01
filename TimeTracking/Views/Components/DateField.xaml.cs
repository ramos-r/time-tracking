using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TimeTracking.Views.Components;

public partial class DateField : UserControl
{
    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime?), typeof(DateField),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedDateChanged));

    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public static readonly DependencyProperty DateTextProperty =
        DependencyProperty.Register(nameof(DateText), typeof(string), typeof(DateField), new PropertyMetadata(string.Empty));

    public string DateText
    {
        get => (string)GetValue(DateTextProperty);
        set => SetValue(DateTextProperty, value);
    }

    public DateField()
    {
        InitializeComponent();
        UpdateTextFromDate();
    }

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DateField)d;
        control.UpdateTextFromDate();
    }

    private void UpdateTextFromDate()
    {
        DateText = SelectedDate?.ToString("d", CultureInfo.CurrentCulture) ?? string.Empty;
    }

    private void CommitText()
    {
        if (DateTime.TryParse(DateText, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed))
        {
            SelectedDate = parsed.Date;
        }
        else
        {
            UpdateTextFromDate();
        }
    }

    private void DateTextBox_LostFocus(object sender, RoutedEventArgs e) => CommitText();

    private void DateTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitText();
        }
    }

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        CalendarPopup.IsOpen = !CalendarPopup.IsOpen;
    }
}
