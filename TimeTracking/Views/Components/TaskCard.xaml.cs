using System.Windows;
using System.Windows.Controls;

namespace TimeTracking.Views.Components;

public partial class TaskCard : UserControl
{
    public TaskCard()
    {
        InitializeComponent();
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (button.ContextMenu is not null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }
}
