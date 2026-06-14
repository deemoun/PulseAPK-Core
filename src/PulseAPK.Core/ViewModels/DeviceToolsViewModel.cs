using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Models;
using PulseAPK.Core.Services;
using Properties = PulseAPK.Core.Properties;

namespace PulseAPK.Core.ViewModels;

public enum DeviceToolMode
{
    InstallApk,
    LaunchApp,
    AppMaintenance,
    ShellPresets
}

public sealed record DeviceToolModeOption(DeviceToolMode Mode, string DisplayName);

public enum DeviceToolPresetKind
{
    Model,
    AndroidRelease,
    Sdk,
    CpuAbi,
    ListPackages,
    ThirdPartyPackages,
    SearchPackage,
    AppPath,
    CurrentActivity,
    Reboot,
    AdbRoot,
    AdbRemount
}

public sealed record DeviceToolPreset(string DisplayName, DeviceToolPresetKind Kind);

public partial class DeviceToolsViewModel : ObservableObject
{
    private readonly AdbService _adbService;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdbStatus))]
    private string _detectedAdbPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdbStatus))]
    [NotifyPropertyChangedFor(nameof(DeviceListStatus))]
    [NotifyPropertyChangedFor(nameof(IsAdbMissing))]
    [NotifyPropertyChangedFor(nameof(HasNoConnectedDevices))]
    private bool _isAdbFound;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdbStatus))]
    [NotifyPropertyChangedFor(nameof(IsAdbMissing))]
    private bool _hasCheckedAdb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdbStatus))]
    [NotifyPropertyChangedFor(nameof(IsAdbMissing))]
    private bool _isCheckingAdb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceListStatus))]
    [NotifyPropertyChangedFor(nameof(HasNoConnectedDevices))]
    private bool _hasCheckedDevices;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceListStatus))]
    [NotifyPropertyChangedFor(nameof(HasNoConnectedDevices))]
    private bool _isRefreshingDevices;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkingDevice))]
    [NotifyPropertyChangedFor(nameof(CanShowNoDeviceHint))]
    [NotifyPropertyChangedFor(nameof(CanShowPackageRequiredHint))]
    [NotifyCanExecuteChangedFor(nameof(InstallApkCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetectPackageCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchActivityCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForceStopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearDataCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(CurrentActivityCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunShellCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAdbCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunModelPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAndroidReleasePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunSdkPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCpuAbiPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunListPackagesPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunThirdPartyPackagesPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunSearchPackagePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAppPathPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCurrentActivityPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunRebootPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAdbRootPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAdbRemountPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunSelectedPresetCommand))]
    private AdbDevice? _selectedDevice;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallApkCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetectPackageCommand))]
    private string _selectedApkPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowPackageRequiredHint))]
    [NotifyCanExecuteChangedFor(nameof(LaunchAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchActivityCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForceStopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearDataCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunSearchPackagePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAppPathPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunSelectedPresetCommand))]
    private string _packageName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchActivityCommand))]
    private string _activity = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunShellCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAdbCommand))]
    private string _commandText = "getprop ro.product.model";

    [ObservableProperty]
    private string _consoleLog = Properties.Resources.WaitingForCommand;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallApkMode))]
    [NotifyPropertyChangedFor(nameof(IsLaunchAppMode))]
    [NotifyPropertyChangedFor(nameof(IsAppMaintenanceMode))]
    [NotifyPropertyChangedFor(nameof(IsShellPresetsMode))]
    private DeviceToolModeOption? _selectedDeviceToolMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunSelectedPresetCommand))]
    private DeviceToolPreset? _selectedPreset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowNoDeviceHint))]
    [NotifyPropertyChangedFor(nameof(CanShowPackageRequiredHint))]
    [NotifyCanExecuteChangedFor(nameof(InstallApkCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetectPackageCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchActivityCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForceStopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearDataCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(CurrentActivityCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunShellCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAdbCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunModelPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAndroidReleasePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunSdkPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCpuAbiPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunListPackagesPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunThirdPartyPackagesPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunSearchPackagePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAppPathPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCurrentActivityPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunRebootPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAdbRootPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAdbRemountPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunSelectedPresetCommand))]
    private bool _isRunning;

    public ObservableCollection<AdbDevice> Devices { get; } = [];
    public ObservableCollection<string> Activities { get; } = [];
    public ObservableCollection<DeviceToolPreset> Presets { get; } =
    [
        new("Model", DeviceToolPresetKind.Model),
        new("Android Release", DeviceToolPresetKind.AndroidRelease),
        new("SDK", DeviceToolPresetKind.Sdk),
        new("CPU ABI", DeviceToolPresetKind.CpuAbi),
        new("List Packages", DeviceToolPresetKind.ListPackages),
        new("Third-Party Packages", DeviceToolPresetKind.ThirdPartyPackages),
        new("Search Package", DeviceToolPresetKind.SearchPackage),
        new("App Path", DeviceToolPresetKind.AppPath),
        new("Current Activity", DeviceToolPresetKind.CurrentActivity),
        new("Reboot", DeviceToolPresetKind.Reboot),
        new("ADB Root", DeviceToolPresetKind.AdbRoot),
        new("ADB Remount", DeviceToolPresetKind.AdbRemount)
    ];
    public IReadOnlyList<DeviceToolModeOption> DeviceToolModes { get; } =
    [
        new(DeviceToolMode.InstallApk, "Install APK"),
        new(DeviceToolMode.LaunchApp, "Launch"),
        new(DeviceToolMode.AppMaintenance, "Manage App"),
        new(DeviceToolMode.ShellPresets, "Shell")
    ];
    public string AdbStatus => IsCheckingAdb
        ? "ADB: checking..."
        : HasCheckedAdb
            ? (IsAdbFound ? "ADB: found" : "ADB: not found")
            : "ADB: not checked";
    public string DeviceListStatus => IsRefreshingDevices
        ? "Checking devices..."
        : HasCheckedDevices && IsAdbFound && Devices.Count == 0
            ? "No Android device connected. Connect a device and enable USB debugging."
            : string.Empty;
    public bool IsAdbMissing => HasCheckedAdb && !IsCheckingAdb && !IsAdbFound;
    public bool HasNoConnectedDevices => HasCheckedDevices && IsAdbFound && !IsRefreshingDevices && Devices.Count == 0;
    public bool IsInstallApkMode => SelectedDeviceToolMode?.Mode == DeviceToolMode.InstallApk;
    public bool IsLaunchAppMode => SelectedDeviceToolMode?.Mode == DeviceToolMode.LaunchApp;
    public bool IsAppMaintenanceMode => SelectedDeviceToolMode?.Mode == DeviceToolMode.AppMaintenance;
    public bool IsShellPresetsMode => SelectedDeviceToolMode?.Mode == DeviceToolMode.ShellPresets;
    public bool HasWorkingDevice => SelectedDevice?.IsUsable == true;
    public bool CanShowNoDeviceHint => !IsRunning && !HasWorkingDevice;
    public bool CanShowPackageRequiredHint => !IsRunning && string.IsNullOrWhiteSpace(PackageName);

    public DeviceToolsViewModel(AdbService adbService, IFilePickerService filePickerService, IDialogService dialogService)
    {
        _adbService = adbService;
        _filePickerService = filePickerService;
        _dialogService = dialogService;
        _selectedDeviceToolMode = DeviceToolModes[0];
        _selectedPreset = Presets[0];
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshDevices()
    {
        await RefreshDevicesAsync();
    }

    public async Task RefreshDevicesAsync()
    {
        IsRefreshingDevices = true;
        try
        {
            await CheckAdbAsync();

            Devices.Clear();
            SelectedDevice = null;
            HasCheckedDevices = true;

            if (!IsAdbFound)
            {
                AppendLog("ADB was not found. Configure ADB Path in Settings or install Android platform-tools.");
                return;
            }

            var result = await RunAdbAndLogAsync(["devices", "-l"]);
            var devices = AdbService.ParseDevices(result.StandardOutput);

            foreach (var device in devices)
            {
                Devices.Add(device);
            }

            SelectedDevice = Devices.FirstOrDefault(device => device.IsUsable) ?? Devices.FirstOrDefault();
            OnPropertyChanged(nameof(DeviceListStatus));
            OnPropertyChanged(nameof(HasNoConnectedDevices));
        }
        finally
        {
            IsRefreshingDevices = false;
            OnPropertyChanged(nameof(HasNoConnectedDevices));
        }
    }

    public async Task CheckAdbAsync()
    {
        IsCheckingAdb = true;
        try
        {
            var resolvedPath = await _adbService.ResolveAdbPathAsync();
            IsAdbFound = !string.IsNullOrWhiteSpace(resolvedPath);
            DetectedAdbPath = resolvedPath ?? string.Empty;
            HasCheckedAdb = true;
        }
        catch
        {
            IsAdbFound = false;
            DetectedAdbPath = string.Empty;
            HasCheckedAdb = true;
        }
        finally
        {
            IsCheckingAdb = false;
        }
    }

    [RelayCommand]
    private async Task BrowseApk()
    {
        var file = await _filePickerService.OpenFileAsync("APK Files (*.apk)|*.apk|All Files (*.*)|*.*");
        if (!string.IsNullOrWhiteSpace(file))
        {
            SelectedApkPath = file;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstallApk))]
    private async Task InstallApk()
    {
        if (!EnsureWorkingDevice() || !File.Exists(SelectedApkPath))
        {
            AppendLog("Select a valid APK and connected device first.");
            return;
        }

        IsRunning = true;
        try
        {
            var result = await RunForDeviceAndLogAsync(["install", "-r", "-d", SelectedApkPath]);
            if (result.ExitCode == 0)
            {
                await DetectPackage();
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDetectPackage))]
    private async Task DetectPackage()
    {
        if (!File.Exists(SelectedApkPath))
        {
            AppendLog("Select a valid APK first.");
            return;
        }

        IsRunning = true;
        try
        {
            var detection = await _adbService.DetectPackageFromApkAsync(SelectedApkPath);
            if (detection.CommandResult != null)
            {
                AppendLog(CommandLogFormatter.FormatCommandResult(detection.CommandResult));
            }

            if (!string.IsNullOrWhiteSpace(detection.PackageName))
            {
                PackageName = detection.PackageName;
            }

            if (!string.IsNullOrWhiteSpace(detection.LaunchableActivity))
            {
                AddActivity(detection.LaunchableActivity);
                Activity = detection.LaunchableActivity;
            }

            if (string.IsNullOrWhiteSpace(detection.PackageName) && string.IsNullOrWhiteSpace(detection.LaunchableActivity))
            {
                AppendLog("Could not detect package/activity automatically. Enter package name manually.");
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void ClearApk()
    {
        SelectedApkPath = string.Empty;
    }

    [RelayCommand]
    private void ClearPackage()
    {
        PackageName = string.Empty;
        Activity = string.Empty;
        Activities.Clear();
    }

    [RelayCommand]
    private void ClearConsole()
    {
        ConsoleLog = Properties.Resources.WaitingForCommand;
    }

    [RelayCommand(CanExecute = nameof(CanRunPackageAction))]
    private async Task LaunchApp()
    {
        await RunPackageActionAsync(["shell", "monkey", "-p", PackageName.Trim(), "-c", "android.intent.category.LAUNCHER", "1"]);
    }

    [RelayCommand(CanExecute = nameof(CanLaunchActivity))]
    private async Task LaunchActivity()
    {
        AddActivity(Activity);
        await RunPackageActionAsync(["shell", "am", "start", "-n", NormalizeActivityComponent()]);
    }

    [RelayCommand(CanExecute = nameof(CanRunPackageAction))]
    private async Task ForceStop()
    {
        await RunPackageActionAsync(["shell", "am", "force-stop", PackageName.Trim()]);
    }

    [RelayCommand(CanExecute = nameof(CanRunPackageAction))]
    private async Task ClearData()
    {
        var package = PackageName.Trim();
        var confirmed = await _dialogService.ShowQuestionAsync(
            $"Clear all app data for '{package}'? This removes app storage and cannot be undone.",
            "Confirm clear app data");

        if (!confirmed)
        {
            AppendLog($"Clear data cancelled for '{package}'.");
            return;
        }

        await RunPackageActionAsync(["shell", "pm", "clear", package]);
    }

    [RelayCommand(CanExecute = nameof(CanRunPackageAction))]
    private async Task Uninstall()
    {
        var package = PackageName.Trim();
        var confirmed = await _dialogService.ShowQuestionAsync(
            $"Uninstall '{package}' from the selected device? This removes the app and its data.",
            "Confirm uninstall app");

        if (!confirmed)
        {
            AppendLog($"Uninstall cancelled for '{package}'.");
            return;
        }

        await RunPackageActionAsync(["uninstall", package]);
    }

    [RelayCommand(CanExecute = nameof(CanRunDeviceAction))]
    private async Task CurrentActivity()
    {
        IsRunning = true;
        try
        {
            var grepResult = await RunForDeviceAndLogAsync(["shell", "sh", "-c", "dumpsys window | grep -E \"mCurrentFocus|mFocusedApp\""]);
            if (grepResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(grepResult.StandardOutput))
            {
                return;
            }

            var windowResult = await RunForDeviceAndLogAsync(["shell", "dumpsys", "window"]);
            var windowLines = FilterLines(windowResult.StandardOutput, "mCurrentFocus", "mFocusedApp");
            if (windowLines.Count > 0)
            {
                AppendLog("Parsed current window focus:");
                foreach (var line in windowLines)
                {
                    AppendLog(line.Trim());
                }
            }

            var activityResult = await RunForDeviceAndLogAsync(["shell", "dumpsys", "activity", "activities"]);
            var activityLines = FilterLines(activityResult.StandardOutput, "mResumedActivity", "topResumedActivity", "ResumedActivity");
            if (activityLines.Count > 0)
            {
                AppendLog("Parsed resumed activity:");
                foreach (var line in activityLines)
                {
                    AppendLog(line.Trim());
                }
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunUserCommand))]
    private async Task RunShell()
    {
        var userArgs = AdbService.SplitCommandLine(CommandText);
        await RunPackageActionAsync(["shell", .. userArgs]);
    }

    [RelayCommand(CanExecute = nameof(CanRunUserCommand))]
    private async Task RunAdb()
    {
        var userArgs = AdbService.SplitCommandLine(CommandText);
        await RunPackageActionAsync(userArgs);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedPreset))]
    private async Task RunSelectedPreset()
    {
        if (SelectedPreset is null)
        {
            AppendLog("Select a preset first.");
            return;
        }

        switch (SelectedPreset.Kind)
        {
            case DeviceToolPresetKind.Model:
                await RunModelPreset();
                break;
            case DeviceToolPresetKind.AndroidRelease:
                await RunAndroidReleasePreset();
                break;
            case DeviceToolPresetKind.Sdk:
                await RunSdkPreset();
                break;
            case DeviceToolPresetKind.CpuAbi:
                await RunCpuAbiPreset();
                break;
            case DeviceToolPresetKind.ListPackages:
                await RunListPackagesPreset();
                break;
            case DeviceToolPresetKind.ThirdPartyPackages:
                await RunThirdPartyPackagesPreset();
                break;
            case DeviceToolPresetKind.SearchPackage:
                await RunSearchPackagePreset();
                break;
            case DeviceToolPresetKind.AppPath:
                await RunAppPathPreset();
                break;
            case DeviceToolPresetKind.CurrentActivity:
                await RunCurrentActivityPreset();
                break;
            case DeviceToolPresetKind.Reboot:
                await RunRebootPreset();
                break;
            case DeviceToolPresetKind.AdbRoot:
                await RunAdbRootPreset();
                break;
            case DeviceToolPresetKind.AdbRemount:
                await RunAdbRemountPreset();
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunDeviceAction))]
    private async Task RunModelPreset() => await RunPackageActionAsync(["shell", "getprop", "ro.product.model"]);

    [RelayCommand(CanExecute = nameof(CanRunDeviceAction))]
    private async Task RunAndroidReleasePreset() => await RunPackageActionAsync(["shell", "getprop", "ro.build.version.release"]);

    [RelayCommand(CanExecute = nameof(CanRunDeviceAction))]
    private async Task RunSdkPreset() => await RunPackageActionAsync(["shell", "getprop", "ro.build.version.sdk"]);

    [RelayCommand(CanExecute = nameof(CanRunDeviceAction))]
    private async Task RunCpuAbiPreset() => await RunPackageActionAsync(["shell", "getprop", "ro.product.cpu.abi"]);

    [RelayCommand(CanExecute = nameof(CanRunDeviceAction))]
    private async Task RunListPackagesPreset() => await RunPackageActionAsync(["shell", "pm", "list", "packages"]);

    [RelayCommand(CanExecute = nameof(CanRunDeviceAction))]
    private async Task RunThirdPartyPackagesPreset() => await RunPackageActionAsync(["shell", "pm", "list", "packages", "-3"]);

    [RelayCommand(CanExecute = nameof(CanRunPackageAction))]
    private async Task RunSearchPackagePreset() => await RunLongActionAsync(SearchPackageAsync);

    [RelayCommand(CanExecute = nameof(CanRunPackageAction))]
    private async Task RunAppPathPreset() => await RunPackageActionAsync(["shell", "pm", "path", PackageName.Trim()]);

    [RelayCommand(CanExecute = nameof(CanRunDeviceAction))]
    private async Task RunCurrentActivityPreset() => await CurrentActivity();

    [RelayCommand(CanExecute = nameof(CanRunDeviceAction))]
    private async Task RunRebootPreset() => await RunPackageActionAsync(["reboot"]);

    [RelayCommand(CanExecute = nameof(CanRunDeviceAction))]
    private async Task RunAdbRootPreset() => await RunPackageActionAsync(["root"]);

    [RelayCommand(CanExecute = nameof(CanRunDeviceAction))]
    private async Task RunAdbRemountPreset() => await RunPackageActionAsync(["remount"]);

    private async Task SearchPackageAsync()
    {
        var filter = PackageName.Trim();
        if (string.IsNullOrWhiteSpace(filter))
        {
            AppendLog("Enter a package name or search text first.");
            return;
        }

        var grepResult = await RunForDeviceAndLogAsync(["shell", "sh", "-c", $"pm list packages | grep {QuoteForDeviceShell(filter)}"]);
        if (grepResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(grepResult.StandardOutput))
        {
            return;
        }

        var result = await RunForDeviceAndLogAsync(["shell", "pm", "list", "packages"]);
        var matches = result.StandardOutput
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => line.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        AppendLog("Filtered package matches:");
        AppendLog(matches.Length == 0 ? "(none)" : string.Join(Environment.NewLine, matches));
    }

    private async Task RunPackageActionAsync(IReadOnlyList<string> arguments)
    {
        if (!EnsureWorkingDevice())
        {
            return;
        }

        IsRunning = true;
        try
        {
            await RunForDeviceAndLogAsync(arguments);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task RunLongActionAsync(Func<Task> action)
    {
        if (!EnsureWorkingDevice())
        {
            return;
        }

        IsRunning = true;
        try
        {
            await action();
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task<AdbCommandResult> RunForDeviceAndLogAsync(IReadOnlyList<string> arguments)
    {
        var serial = SelectedDevice?.Serial ?? string.Empty;
        return await RunAdbAndLogAsync(["-s", serial, .. arguments]);
    }

    private async Task<AdbCommandResult> RunAdbAndLogAsync(IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(DetectedAdbPath))
        {
            await CheckAdbAsync();
        }

        var result = await _adbService.RunAdbAsync(DetectedAdbPath, arguments);
        AppendLog(CommandLogFormatter.FormatCommandResult(result));
        return result;
    }

    private bool EnsureWorkingDevice()
    {
        if (SelectedDevice is null)
        {
            AppendLog("No device selected. Refresh devices and choose a connected device.");
            return false;
        }

        if (!SelectedDevice.IsUsable)
        {
            AppendLog($"Selected device '{SelectedDevice.Serial}' is {SelectedDevice.State}. Actions require state 'device'.");
            return false;
        }

        return true;
    }

    private bool RequirePackage()
    {
        if (!string.IsNullOrWhiteSpace(PackageName))
        {
            return true;
        }

        AppendLog("Enter a package name first.");
        return false;
    }

    private string NormalizeActivityComponent()
    {
        var package = PackageName.Trim();
        var activity = Activity.Trim();
        return $"{package}/{activity}";
    }

    private void AddActivity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Activities.Any(activity => string.Equals(activity, value, StringComparison.Ordinal)))
        {
            Activities.Add(value);
        }
    }

    private void AppendLog(string message)
    {
        if (ConsoleLog == Properties.Resources.WaitingForCommand)
        {
            ConsoleLog = message;
            return;
        }

        ConsoleLog += $"{Environment.NewLine}{Environment.NewLine}{message}";
    }

    private static IReadOnlyList<string> FilterLines(string text, params string[] needles)
    {
        return text
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => needles.Any(needle => line.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static string QuoteForDeviceShell(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'")}'";
    }

    private bool CanRunDeviceAction() => !IsRunning && HasWorkingDevice;
    private bool CanInstallApk() => CanRunDeviceAction() && File.Exists(SelectedApkPath);
    private bool CanDetectPackage() => !IsRunning && File.Exists(SelectedApkPath);
    private bool CanRunPackageAction() => CanRunDeviceAction() && !string.IsNullOrWhiteSpace(PackageName);
    private bool CanLaunchActivity() => CanRunPackageAction() && !string.IsNullOrWhiteSpace(Activity);
    private bool CanRunUserCommand() => CanRunDeviceAction() && !string.IsNullOrWhiteSpace(CommandText);
    private bool CanRunSelectedPreset()
    {
        if (SelectedPreset is null || !CanRunDeviceAction())
        {
            return false;
        }

        return SelectedPreset.Kind is not DeviceToolPresetKind.SearchPackage and not DeviceToolPresetKind.AppPath
            || !string.IsNullOrWhiteSpace(PackageName);
    }
}
