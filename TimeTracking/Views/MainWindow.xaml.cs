using System.Windows;
using TimeTracking.ViewModels;

namespace TimeTracking.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
