using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using PulseAPK.Core.Abstractions;
using System;

namespace PulseAPK.Avalonia.Services;

public sealed class AvaloniaThemeService : IThemeService
{
    private const string DefaultBackground = "#121212";
    private const double DefaultScale = 1.0;
    private const double MinimumScale = 0.75;
    private const double MaximumScale = 1.5;

    public void ApplyTheme(string? themeMode)
    {
        ApplyVisualPreferences(themeMode, DefaultBackground, DefaultScale);
    }

    public void ApplyVisualPreferences(string? themeMode, string? backgroundColor, double objectScale)
    {
        if (Application.Current is null)
        {
            return;
        }

        var isLightMode = string.Equals(themeMode, "light_mode", StringComparison.OrdinalIgnoreCase);
        Application.Current.RequestedThemeVariant = isLightMode ? ThemeVariant.Light : ThemeVariant.Dark;

        var resources = Application.Current.Resources;
        var normalizedScale = Math.Clamp(objectScale, MinimumScale, MaximumScale);
        resources["UiObjectScale"] = normalizedScale;

        if (string.IsNullOrWhiteSpace(backgroundColor)
            || string.Equals(backgroundColor, DefaultBackground, StringComparison.OrdinalIgnoreCase))
        {
            resources["MainBackgroundBrush"] = new SolidColorBrush(isLightMode ? Color.Parse("#FFFFFF") : Color.Parse("#121212"));
            return;
        }

        if (Color.TryParse(backgroundColor, out var color))
        {
            resources["MainBackgroundBrush"] = new SolidColorBrush(color);
        }
    }
}
