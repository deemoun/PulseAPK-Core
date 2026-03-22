using Avalonia.Controls;
using PulseAPK.Core.ViewModels;

namespace PulseAPK.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
