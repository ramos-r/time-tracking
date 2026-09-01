using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TimeTracking.Views.Components;

public partial class SimpleCalendar : UserControl
{
    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime?), typeof(SimpleCalendar),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedDateChanged));

    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    private DateTime _displayMonth;

    public SimpleCalendar()
    {
        InitializeComponent();
        _displayMonth = FirstOfMonth(SelectedDate ?? DateTime.Today);
        Loaded += (_, _) => BuildGrid();
    }

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SimpleCalendar)d;
        control._displayMonth = FirstOfMonth((DateTime?)e.NewValue ?? DateTime.Today);
        control.BuildGrid();
    }

    private static DateTime FirstOfMonth(DateTime date) => new(date.Year, date.Month, 1);

    private void PreviousMonth_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(-1);
        BuildGrid();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(1);
        BuildGrid();
    }

    private void BuildGrid()
    {
        if (MonthYearText is null || DaysPanel is null)
        {
            return;
        }

        MonthYearText.Text = _displayMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        DaysPanel.Children.Clear();

        var startDate = _displayMonth.AddDays(-(int)_displayMonth.DayOfWeek);
        var selectedDate = SelectedDate?.Date;
        var dayCellStyle = (Style)FindResource("CalendarDayCellStyle");

        for (var i = 0; i < 42; i++)
        {
            var day = startDate.AddDays(i);
            var button = new Button
            {
                Content = day.Day.ToString(CultureInfo.InvariantCulture),
                Style = dayCellStyle,
                Tag = day
            };

            if (day.Month != _displayMonth.Month)
            {
                button.Opacity = 0.35;
            }

            if (day.Date == DateTime.Today)
            {
                button.FontWeight = FontWeights.Bold;
            }

            if (selectedDate.HasValue && day.Date == selectedDate.Value)
            {
                button.Background = (Brush)FindResource("BorderBrush");
            }

            button.Click += DayButton_Click;
            DaysPanel.Children.Add(button);
        }
    }

    private void DayButton_Click(object sender, RoutedEventArgs e)
    {
        var day = (DateTime)((Button)sender).Tag;
        SelectedDate = day;

        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is Popup popup)
            {
                popup.IsOpen = false;
                return;
            }

            current = LogicalTreeHelper.GetParent(current);
        }
    }
}
