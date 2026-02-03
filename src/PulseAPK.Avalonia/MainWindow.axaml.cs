using Avalonia.Controls;
using PulseAPK.Core.ViewModels;

namespace PulseAPK.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
