using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.ViewModels;
using Properties = PulseAPK.Core.Properties;

namespace PulseAPK.Avalonia.Views;

public partial class AnalyserView : UserControl
{
    public AnalyserView()
    {
        InitializeComponent();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        var storageItems = e.Data.GetFiles();
        var item = storageItems?.FirstOrDefault();
        var path = item?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!Directory.Exists(path))
        {
            await ShowWarningAsync(Properties.Resources.Error_InvalidProjectSelection, Properties.Resources.AnalyserHeader);
            return;
        }

        var smaliFiles = Directory.EnumerateFiles(path, "*.smali", SearchOption.AllDirectories);
        if (!smaliFiles.Any())
        {
            await ShowWarningAsync(Properties.Resources.Error_InvalidSmaliProject, Properties.Resources.AnalyserHeader);
            return;
        }

        if (DataContext is AnalyserViewModel viewModel)
        {
            viewModel.ProjectPath = path;
        }
    }

    private static async Task ShowWarningAsync(string message, string title)
    {
        if (Application.Current is App app && app.Services != null)
        {
            var dialogService = app.Services.GetService<IDialogService>();
            if (dialogService != null)
            {
                await dialogService.ShowWarningAsync(message, title);
            }
        }
    }
}
