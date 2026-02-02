using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Utils;
using PulseAPK.Core.ViewModels;
using Properties = PulseAPK.Core.Properties;

namespace PulseAPK.Avalonia.Views;

public partial class DecompileView : UserControl
{
    public DecompileView()
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

        var (isValid, message) = FileSanitizer.ValidateApk(path);
        if (!isValid)
        {
            await ShowWarningAsync(message, Properties.Resources.Error_InvalidApkFile);
            return;
        }

        if (DataContext is DecompileViewModel viewModel)
        {
            viewModel.ApkPath = path;
        }

        e.Handled = true;
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
