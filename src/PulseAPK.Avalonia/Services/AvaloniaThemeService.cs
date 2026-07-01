using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using PulseAPK.Core.Abstractions;
using System;

namespace PulseAPK.Avalonia.Services;

public sealed class AvaloniaThemeService : IThemeService
{
    private const string DefaultDarkBackground = "#121212";
    private const string DefaultLightBackground = "#FFFFFF";
    private const double DefaultScale = 1.0;
    private const double MinimumScale = 0.75;
    private const double MaximumScale = 1.5;

    public void ApplyTheme(string? themeMode)
    {
        ApplyVisualPreferences(themeMode, DefaultDarkBackground, DefaultScale);
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

        var backgroundBrush = CreateBackgroundBrush(backgroundColor, isLightMode);
        SetApplicationResource(resources, "MainBackgroundBrush", backgroundBrush);

        if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            desktop.MainWindow.Background = backgroundBrush;
        }
    }

    private static SolidColorBrush CreateBackgroundBrush(string? backgroundColor, bool isLightMode)
    {
        var defaultBackground = isLightMode ? DefaultLightBackground : DefaultDarkBackground;
        var colorValue = string.IsNullOrWhiteSpace(backgroundColor)
            || (!isLightMode && string.Equals(backgroundColor, DefaultDarkBackground, StringComparison.OrdinalIgnoreCase))
            ? defaultBackground
            : backgroundColor;

        return Color.TryParse(colorValue, out var color)
            ? new SolidColorBrush(color)
            : new SolidColorBrush(Color.Parse(defaultBackground));
    }

    private static void SetApplicationResource(ResourceDictionary resources, string key, object value)
    {
        resources[key] = value;

        foreach (var themeDictionary in resources.ThemeDictionaries.Values)
        {
            themeDictionary[key] = value;
        }
    }
}
