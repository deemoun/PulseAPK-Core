using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections;
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
        // Some file managers expose drag payloads lazily; advertise copy so Drop is still fired.
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = TryGetFirstLocalPath(e);
        if (string.IsNullOrWhiteSpace(path))
        {
            await ShowWarningAsync("Could not read a local file path from the dropped data. Please drag an APK file from your file manager.", Properties.Resources.Error_InvalidApkFile);
            e.Handled = true;
            return;
        }

        var (isValid, message) = FileSanitizer.ValidateApk(path);
        if (!isValid)
        {
            await ShowWarningAsync(message, Properties.Resources.Error_InvalidApkFile);
            e.Handled = true;
            return;
        }

        if (DataContext is DecompileViewModel viewModel)
        {
            viewModel.ApkPath = path;
        }

        e.Handled = true;
    }

    private static string? TryGetFirstLocalPath(DragEventArgs e)
    {
        var data = e.Data;
        var storageItem = data.GetFiles()?.FirstOrDefault();
        var localPath = storageItem?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            return localPath;
        }

        var reflectedPath = TryGetFirstLocalPathFromDataTransfer(e);
        if (!string.IsNullOrWhiteSpace(reflectedPath))
        {
            return reflectedPath;
        }

        var text = data.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var firstLine = text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !line.StartsWith("#", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return null;
        }

        if (Uri.TryCreate(firstLine, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return uri.LocalPath;
        }

        return firstLine;
    }

    private static string? TryGetFirstLocalPathFromDataTransfer(DragEventArgs e)
    {
        var dataTransferProperty = e.GetType().GetProperty("DataTransfer");
        var dataTransfer = dataTransferProperty?.GetValue(e);
        if (dataTransfer is null)
        {
            return null;
        }

        var extensionType = typeof(DragDrop).Assembly.GetType("Avalonia.Input.DataTransferExtensions");
        if (extensionType is null)
        {
            return null;
        }

        var tryGetFilesMethod = extensionType.GetMethods()
            .FirstOrDefault(m => m.Name == "TryGetFiles" && m.GetParameters().Length == 1);
        object? filesResult = null;
        try
        {
            filesResult = tryGetFilesMethod?.Invoke(null, new[] { dataTransfer });
        }
        catch
        {
            // Ignore and continue with other extraction paths.
        }
        if (filesResult is IEnumerable fileItems)
        {
            foreach (var item in fileItems)
            {
                if (item is IStorageItem storageItem)
                {
                    var path = storageItem.TryGetLocalPath();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return path;
                    }
                }
            }
        }

        var tryGetTextMethod = extensionType.GetMethods()
            .FirstOrDefault(m => m.Name == "TryGetText" && m.GetParameters().Length == 1);
        string? textResult = null;
        try
        {
            textResult = tryGetTextMethod?.Invoke(null, new[] { dataTransfer }) as string;
        }
        catch
        {
            // Ignore and continue with other extraction paths.
        }
        if (!string.IsNullOrWhiteSpace(textResult))
        {
            if (Uri.TryCreate(textResult.Trim(), UriKind.Absolute, out var uri) && uri.IsFile)
            {
                return uri.LocalPath;
            }

            return textResult.Trim();
        }

        return null;
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
