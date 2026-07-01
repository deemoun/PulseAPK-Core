namespace PulseAPK.Core.Abstractions;

/// <summary>
/// Platform-agnostic theme service used to apply UI theme changes at runtime.
/// </summary>
public interface IThemeService
{
    void ApplyTheme(string? themeMode);
}
