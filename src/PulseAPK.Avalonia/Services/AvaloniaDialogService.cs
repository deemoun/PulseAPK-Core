using Avalonia.Controls;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using PulseAPK.Core.Abstractions;
using System.Threading.Tasks;

namespace PulseAPK.Avalonia.Services;

public class AvaloniaDialogService : IDialogService
{
    public async Task ShowInfoAsync(string message, string? title = null)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(BuildParameters(title ?? "Info", message, ButtonEnum.Ok, Icon.Info));
        await box.ShowAsync();
    }

    public async Task ShowWarningAsync(string message, string? title = null)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(BuildParameters(title ?? "Warning", message, ButtonEnum.Ok, Icon.Warning));
        await box.ShowAsync();
    }

    public async Task ShowErrorAsync(string message, string? title = null)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(BuildParameters(title ?? "Error", message, ButtonEnum.Ok, Icon.Error));
        await box.ShowAsync();
    }

    public async Task<bool> ShowQuestionAsync(string message, string? title = null)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(BuildParameters(title ?? "Question", message, ButtonEnum.YesNo, Icon.Question));
        var result = await box.ShowAsync();
        return result == ButtonResult.Yes;
    }

    private static MessageBoxStandardParams BuildParameters(string title, string message, ButtonEnum buttons, Icon icon)
    {
        return new MessageBoxStandardParams
        {
            ContentTitle = title,
            ContentMessage = message,
            ButtonDefinitions = buttons,
            Icon = icon,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
            Width = 420,
            MaxWidth = 720
        };
    }
}
