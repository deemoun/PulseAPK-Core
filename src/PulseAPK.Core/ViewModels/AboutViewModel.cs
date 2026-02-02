using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Reflection;
using PulseAPK.Core.Abstractions;
using Properties = PulseAPK.Core.Properties;

namespace PulseAPK.Core.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly ISystemService _systemService;

    public string AppVersion => GetAppVersion();
    public string DeveloperName { get; } = "Dmitry Yarygin";
    public string Year { get; } = "2026";

    public AboutViewModel(ISystemService systemService)
    {
        _systemService = systemService;
    }

    private string GetAppVersion()
    {
        var informationalVersion = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
                                            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                                            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataSeparatorIndex = informationalVersion.IndexOf('+');
            if (metadataSeparatorIndex >= 0)
            {
                informationalVersion = informationalVersion[..metadataSeparatorIndex];
            }

            var prereleaseSeparatorIndex = informationalVersion.IndexOf('-');
            if (prereleaseSeparatorIndex >= 0)
            {
                informationalVersion = informationalVersion[..prereleaseSeparatorIndex];
            }
        }

        var version = string.IsNullOrWhiteSpace(informationalVersion)
            ? (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version?.ToString(3) ?? "1.2.1"
            : informationalVersion;

        return string.Format(Properties.Resources.About_Version, version);
    }

    [RelayCommand]
    private void OpenProjectPage()
    {
        _systemService.OpenUrl("https://github.com/deemoun/PulseAPK");
    }

    [RelayCommand]
    private void OpenDeveloperPage()
    {
        _systemService.OpenUrl("https://yarygintech.com/");
    }
}
