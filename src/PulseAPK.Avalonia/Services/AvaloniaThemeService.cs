using Avalonia;
using Avalonia.Styling;
using PulseAPK.Core.Abstractions;
using System;

namespace PulseAPK.Avalonia.Services;

public sealed class AvaloniaThemeService : IThemeService
{
    public void ApplyTheme(string? themeMode)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = string.Equals(themeMode, "light_mode", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }
}
