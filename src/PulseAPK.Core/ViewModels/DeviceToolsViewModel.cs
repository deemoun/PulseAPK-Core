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
    AdbRemount,
    LogcatForPackage
}

public sealed record DeviceToolPreset(string DisplayName, DeviceToolPresetKind Kind);

public partial class DeviceToolsViewModel : ObservableObject
{
    private static string T(string key) => LocalizationService.Instance[key];
    private readonly AdbService _adbService;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdbStatus))]
    private string _detectedAdbPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdbStatus))]
    [NotifyPropertyChangedFor(nameof(DeviceListStatus))]
    [NotifyPropertyChangedFor(nameof(InlineDeviceListStatus))]
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
    [NotifyPropertyChangedFor(nameof(InlineDeviceListStatus))]
    [NotifyPropertyChangedFor(nameof(HasNoConnectedDevices))]
    private bool _hasCheckedDevices;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceListStatus))]
    [NotifyPropertyChangedFor(nameof(InlineDeviceListStatus))]
    [NotifyPropertyChangedFor(nameof(HasNoConnectedDevices))]
    private bool _isRefreshingDevices;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkingDevice))]
    [NotifyPropertyChangedFor(nameof(IsDeviceOnline))]
    [NotifyPropertyChangedFor(nameof(IsDeviceOffline))]
    [NotifyPropertyChangedFor(nameof(DeviceConnectionStatus))]
    [NotifyPropertyChangedFor(nameof(CanShowNoDeviceHint))]
    [NotifyPropertyChangedFor(nameof(CanShowPackageRequiredHint))]
    [NotifyCanExecuteChangedFor(nameof(InstallApkCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetectPackageCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchActivityCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForceStopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearDataCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenAppSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyApkFromDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(CurrentActivityCommand))]
    [NotifyCanExecuteChangedFor(nameof(TakeScreenshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartScreenRecordCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopScreenRecordCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(RunLogcatForPackagePresetCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(OpenAppSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyApkFromDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunSearchPackagePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAppPathPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunLogcatForPackagePresetCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(OpenAppSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyApkFromDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(CurrentActivityCommand))]
    [NotifyCanExecuteChangedFor(nameof(TakeScreenshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartScreenRecordCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopScreenRecordCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(RunLogcatForPackagePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunSelectedPresetCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScreenRecordCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopScreenRecordCommand))]
    [NotifyPropertyChangedFor(nameof(ScreenRecordStatus))]
    private bool _isScreenRecording;

    private CancellationTokenSource? _screenRecordCancellation;
    private Task? _screenRecordTask;

    public ObservableCollection<AdbDevice> Devices { get; } = [];
    public ObservableCollection<string> Activities { get; } = [];
    public ObservableCollection<DeviceToolPreset> Presets { get; } =
    [
        new(T("DeviceToolsPresetModel"), DeviceToolPresetKind.Model),
        new(T("DeviceToolsPresetAndroidRelease"), DeviceToolPresetKind.AndroidRelease),
        new(T("DeviceToolsPresetSdk"), DeviceToolPresetKind.Sdk),
        new(T("DeviceToolsPresetCpuAbi"), DeviceToolPresetKind.CpuAbi),
        new(T("DeviceToolsPresetListPackages"), DeviceToolPresetKind.ListPackages),
        new(T("DeviceToolsPresetThirdPartyPackages"), DeviceToolPresetKind.ThirdPartyPackages),
        new(T("DeviceToolsPresetSearchPackage"), DeviceToolPresetKind.SearchPackage),
        new(T("DeviceToolsPresetAppPath"), DeviceToolPresetKind.AppPath),
        new(T("DeviceToolsPresetCurrentActivity"), DeviceToolPresetKind.CurrentActivity),
        new(T("DeviceToolsPresetReboot"), DeviceToolPresetKind.Reboot),
        new(T("DeviceToolsPresetAdbRoot"), DeviceToolPresetKind.AdbRoot),
        new(T("DeviceToolsPresetAdbRemount"), DeviceToolPresetKind.AdbRemount),
        new(T("DeviceToolsPresetLogcatForPackage"), DeviceToolPresetKind.LogcatForPackage)
    ];
    public IReadOnlyList<DeviceToolModeOption> DeviceToolModes { get; } =
    [
        new(DeviceToolMode.InstallApk, T("DeviceToolsModeInstallApk")),
        new(DeviceToolMode.LaunchApp, T("DeviceToolsModeLaunch")),
        new(DeviceToolMode.AppMaintenance, T("DeviceToolsModeManageApp")),
        new(DeviceToolMode.ShellPresets, T("DeviceToolsModeShell"))
    ];
    public string AdbStatus => IsCheckingAdb
        ? T("DeviceToolsAdbChecking")
        : HasCheckedAdb
            ? (IsAdbFound ? T("DeviceToolsAdbFound") : T("DeviceToolsAdbNotFound"))
            : T("DeviceToolsAdbNotChecked");
    public string DeviceListStatus => IsRefreshingDevices
        ? T("DeviceToolsCheckingDevices")
        : HasCheckedDevices && IsAdbFound && Devices.Count == 0
            ? T("DeviceToolsNoConnectedDevices")
            : string.Empty;
    public string InlineDeviceListStatus => HasNoConnectedDevices ? string.Empty : DeviceListStatus;
    public bool IsAdbMissing => HasCheckedAdb && !IsCheckingAdb && !IsAdbFound;
    public bool HasNoConnectedDevices => HasCheckedDevices && IsAdbFound && !IsRefreshingDevices && Devices.Count == 0;
    public bool IsInstallApkMode => SelectedDeviceToolMode?.Mode == DeviceToolMode.InstallApk;
    public bool IsLaunchAppMode => SelectedDeviceToolMode?.Mode == DeviceToolMode.LaunchApp;
    public bool IsAppMaintenanceMode => SelectedDeviceToolMode?.Mode == DeviceToolMode.AppMaintenance;
    public bool IsShellPresetsMode => SelectedDeviceToolMode?.Mode == DeviceToolMode.ShellPresets;
    public bool HasWorkingDevice => SelectedDevice?.IsUsable == true;
    public bool IsDeviceOnline => HasWorkingDevice;
    public bool IsDeviceOffline => !HasWorkingDevice;
    public string DeviceConnectionStatus => HasWorkingDevice
        ? T("DeviceToolsDeviceOnline")
        : T("DeviceToolsDeviceOffline");
    public bool CanShowNoDeviceHint => !IsRunning && !HasWorkingDevice;
    public bool CanShowPackageRequiredHint => !IsRunning && string.IsNullOrWhiteSpace(PackageName);
    public string ScreenRecordStatus => IsScreenRecording
        ? T("DeviceToolsScreenRecordInProgress")
        : T("DeviceToolsScreenRecordReady");

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
            OnPropertyChanged(nameof(InlineDeviceListStatus));
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
    private async Task TakeScreenshot()
    {
        var outputFolder = await _filePickerService.OpenFolderAsync(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            AppendLog("Screenshot cancelled. Choose a folder to save the image.");
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var remotePath = $"/sdcard/PulseAPK-screenshot-{timestamp}.png";
        var localPath = Path.Combine(outputFolder, $"PulseAPK-screenshot-{timestamp}.png");

        await RunLongActionAsync(async () =>
        {
            var screencap = await RunForDeviceAndLogAsync(["shell", "screencap", "-p", remotePath]);
            if (screencap.ExitCode != 0)
            {
                AppendLog("Screenshot failed; nothing was pulled from the device.");
                return;
            }

            var pull = await RunForDeviceAndLogAsync(["pull", remotePath, localPath]);
            await RunForDeviceAndLogAsync(["shell", "rm", "-f", remotePath]);
            if (pull.ExitCode == 0)
            {
                AppendLog($"Screenshot saved to: {localPath}");
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanStartScreenRecord))]
    private async Task StartScreenRecord()
    {
        if (!EnsureWorkingDevice())
        {
            return;
        }

        var outputFolder = await _filePickerService.OpenFolderAsync(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            AppendLog("Screen record cancelled. Choose a folder to save the recording.");
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var remotePath = $"/sdcard/PulseAPK-recording-{timestamp}.mp4";
        var localPath = Path.Combine(outputFolder, $"PulseAPK-recording-{timestamp}.mp4");
        var serial = SelectedDevice?.Serial ?? string.Empty;
        _screenRecordCancellation = new CancellationTokenSource();
        IsScreenRecording = true;
        AppendLog($"Screen recording started on the device at {remotePath}. Use Stop Screen Record to end adb shell screenrecord and pull the MP4.");

        _screenRecordTask = CompleteScreenRecordAsync(serial, remotePath, localPath, _screenRecordCancellation);
    }

    private async Task CompleteScreenRecordAsync(string serial, string remotePath, string localPath, CancellationTokenSource cancellation)
    {
        try
        {
            var recordResult = await RunAdbAndLogAsync(["-s", serial, "shell", "screenrecord", remotePath], TimeSpan.FromHours(3));
            if (recordResult.ExitCode != 0 && !cancellation.IsCancellationRequested)
            {
                AppendLog("Screen recording ended with an error; attempting to pull any completed file.");
            }

            var pull = await RunAdbAndLogAsync(["-s", serial, "pull", remotePath, localPath], TimeSpan.FromMinutes(2));
            await RunAdbAndLogAsync(["-s", serial, "shell", "rm", "-f", remotePath]);
            if (pull.ExitCode == 0)
            {
                AppendLog($"Screen recording saved to: {localPath}");
            }
        }
        finally
        {
            cancellation.Dispose();
            if (ReferenceEquals(_screenRecordCancellation, cancellation))
            {
                _screenRecordCancellation = null;
            }
            IsScreenRecording = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopScreenRecord))]
    private async Task StopScreenRecord()
    {
        if (!IsScreenRecording || _screenRecordCancellation is null)
        {
            AppendLog("No active screen recording to stop.");
            return;
        }

        AppendLog("Stopping screen recording and pulling the MP4...");
        _screenRecordCancellation.Cancel();
        await RunForDeviceAndLogAsync(["shell", "sh", "-c", "pkill -2 screenrecord || killall -2 screenrecord || pkill screenrecord || killall screenrecord"]);
        if (_screenRecordTask is not null)
        {
            await _screenRecordTask;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunPackageAction))]
    private async Task OpenAppSettings()
    {
        var package = PackageName.Trim();
        await RunPackageActionAsync(["shell", "am", "start", "-a", "android.settings.APPLICATION_DETAILS_SETTINGS", "-d", $"package:{package}"]);
    }

    [RelayCommand(CanExecute = nameof(CanRunPackageAction))]
    private async Task CopyApkFromDevice()
    {
        var outputFolder = await _filePickerService.OpenFolderAsync(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            AppendLog("Copy APK cancelled. Choose a folder to save the APK.");
            return;
        }

        await RunLongActionAsync(async () =>
        {
            var package = PackageName.Trim();
            var pathResult = await RunForDeviceAndLogAsync(["shell", "pm", "path", package]);
            var remotePath = pathResult.StandardOutput
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.StartsWith("package:", StringComparison.OrdinalIgnoreCase) ? line["package:".Length..].Trim() : line.Trim())
                .FirstOrDefault(line => line.EndsWith(".apk", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(remotePath))
            {
                AppendLog($"Could not find an APK path for '{package}'.");
                return;
            }

            var safePackage = string.Join("_", package.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var fileName = Path.GetFileName(remotePath);
            var localPath = Path.Combine(outputFolder, $"{safePackage}-{fileName}");
            var pull = await RunForDeviceAndLogAsync(["pull", remotePath, localPath]);
            if (pull.ExitCode == 0)
            {
                AppendLog($"APK copied to: {localPath}");
            }
        });
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
            case DeviceToolPresetKind.LogcatForPackage:
                await RunLogcatForPackagePreset();
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

    [RelayCommand(CanExecute = nameof(CanRunPackageAction))]
    private async Task RunLogcatForPackagePreset() => await RunLongActionAsync(LogcatForPackageAsync);

    [RelayCommand(CanExecute = nameof(CanRunDeviceAction))]
    private async Task RunCurrentActivityPreset() => await CurrentActivity();

    [RelayCommand(CanExecute = nameof(CanRunDeviceAction))]
    private async Task RunRebootPreset() => await RunPackageActionAsync(["reboot"]);

    [RelayCommand(CanExecute = nameof(CanRunDeviceAction))]
    private async Task RunAdbRootPreset() => await RunPackageActionAsync(["root"]);

    [RelayCommand(CanExecute = nameof(CanRunDeviceAction))]
    private async Task RunAdbRemountPreset() => await RunPackageActionAsync(["remount"]);

    private async Task LogcatForPackageAsync()
    {
        var package = PackageName.Trim();
        var pidResult = await RunForDeviceAndLogAsync(["shell", "pidof", package]);
        var pid = pidResult.StandardOutput
            .Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.All(char.IsDigit));

        if (!string.IsNullOrWhiteSpace(pid))
        {
            AppendLog($"Found running PID {pid} for '{package}'. Using logcat --pid filtering where supported by the device.");
            await RunForDeviceAndLogAsync(["logcat", "-d", "--pid", pid]);
            return;
        }

        AppendLog($"No running PID found for '{package}'. Falling back to package text filtering.");
        await RunForDeviceAndLogAsync(["shell", "sh", "-c", $"logcat -d | grep {QuoteForDeviceShell(package)}"]);
    }

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

    private async Task<AdbCommandResult> RunAdbAndLogAsync(IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(DetectedAdbPath))
        {
            await CheckAdbAsync();
        }

        var result = await _adbService.RunAdbAsync(DetectedAdbPath, arguments, timeout, cancellationToken);
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
    private bool CanStartScreenRecord() => !IsRunning && !IsScreenRecording && HasWorkingDevice;
    private bool CanStopScreenRecord() => IsScreenRecording;
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

        return SelectedPreset.Kind is not DeviceToolPresetKind.SearchPackage
                and not DeviceToolPresetKind.AppPath
                and not DeviceToolPresetKind.LogcatForPackage
            || !string.IsNullOrWhiteSpace(PackageName);
    }
}
