using System.Windows;
using DigitalBrain.UI.ViewModels;

namespace DigitalBrain.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
