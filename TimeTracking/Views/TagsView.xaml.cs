using System.Windows;
using System.Windows.Controls;

namespace TimeTracking.Views;

public partial class TagsView : UserControl
{
    public TagsView()
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
