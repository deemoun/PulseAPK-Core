using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Models;
using PulseAPK.Core.Services;
using PulseAPK.Core.Utils;
using System.Text;
using Properties = PulseAPK.Core.Properties;

namespace PulseAPK.Core.ViewModels;

public partial class PatchViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHintVisible))]
    private string _apkPath = string.Empty;

    [ObservableProperty]
    private string _outputFolderPath = string.Empty;

    [ObservableProperty]
    private string _outputApkName = string.Empty;

    [ObservableProperty]
    private string _outputApkPath = string.Empty;

    [ObservableProperty]
    private bool _signApk = true;

    [ObservableProperty]
    private bool _decodeResources = true;

    [ObservableProperty]
    private bool _decodeSources = true;

    [ObservableProperty]
    private bool _useAapt2ForBuild;

    [ObservableProperty]
    private string _consoleLog;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunPatchCommand))]
    private bool _isRunning;

    private readonly IFilePickerService _filePickerService;
    private readonly ISettingsService _settingsService;
    private readonly IPatchPipelineService _patchPipelineService;
    private readonly IDialogService _dialogService;

    public bool IsHintVisible => string.IsNullOrWhiteSpace(ApkPath);

    public PatchViewModel(
        IFilePickerService filePickerService,
        ISettingsService settingsService,
        IPatchPipelineService patchPipelineService,
        IDialogService dialogService)
    {
        _filePickerService = filePickerService;
        _settingsService = settingsService;
        _patchPipelineService = patchPipelineService;
        _dialogService = dialogService;

        _consoleLog = Properties.Resources.WaitingForCommand;

        OutputFolderPath = EnsureCompiledDirectory();
        OutputApkName = "patched.apk";
        UpdateOutputApkPath();
        UpdateCommandPreview();
    }

    partial void OnApkPathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var fileName = Path.GetFileNameWithoutExtension(value);
            OutputApkName = $"{fileName}_patched.apk";
        }

        UpdateOutputApkPath();
        RunPatchCommand.NotifyCanExecuteChanged();
    }

    partial void OnOutputFolderPathChanged(string value)
    {
        UpdateOutputApkPath();
    }

    partial void OnOutputApkNameChanged(string value)
    {
        UpdateOutputApkPath();
        RunPatchCommand.NotifyCanExecuteChanged();
    }

    partial void OnOutputApkPathChanged(string value) => UpdateCommandPreview();
    partial void OnSignApkChanged(bool value) => UpdateCommandPreview();
    partial void OnDecodeResourcesChanged(bool value) => UpdateCommandPreview();
    partial void OnDecodeSourcesChanged(bool value) => UpdateCommandPreview();
    partial void OnUseAapt2ForBuildChanged(bool value) => UpdateCommandPreview();

    [RelayCommand]
    private async Task BrowseApk()
    {
        var file = await _filePickerService.OpenFileAsync("APK Files (*.apk)|*.apk|All Files (*.*)|*.*");
        if (file is null)
        {
            return;
        }

        var (isValid, message) = FileSanitizer.ValidateApk(file);
        if (!isValid)
        {
            await _dialogService.ShowErrorAsync(message, "Invalid APK File");
            return;
        }

        ApkPath = file;
    }

    [RelayCommand]
    private async Task BrowseOutputFolder()
    {
        var folder = await _filePickerService.OpenFolderAsync(OutputFolderPath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            OutputFolderPath = folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunPatch))]
    private async Task RunPatch()
    {
        if (string.IsNullOrWhiteSpace(ApkPath))
        {
            await _dialogService.ShowWarningAsync("Please select an APK file to patch.", "Missing APK");
            return;
        }

        var apktoolPath = _settingsService.Settings.ApktoolPath?.Trim();
        if (string.IsNullOrWhiteSpace(apktoolPath) || !File.Exists(apktoolPath))
        {
            await _dialogService.ShowErrorAsync(string.Format(Properties.Resources.Error_InvalidApktoolPath, apktoolPath), Properties.Resources.SettingsHeader);
            return;
        }

        IsRunning = true;
        SetConsoleLog("Starting patch pipeline...");

        try
        {
            var request = new PatchRequest
            {
                InputApkPath = ApkPath,
                OutputApkPath = OutputApkPath,
                SignOutput = SignApk,
                DecodeResources = DecodeResources,
                DecodeSources = DecodeSources,
                UseAapt2ForBuild = UseAapt2ForBuild,
                WorkingDirectory = Path.Combine(Path.GetTempPath(), "pulseapk-patch-ui"),
                KeepIntermediateFiles = false
            };

            AppendLog(BuildRunSummary(request));

            var result = await _patchPipelineService.RunAsync(request);

            foreach (var stage in result.StageSummaries)
            {
                var icon = stage.Success ? "[OK]" : "[FAIL]";
                AppendLog($"{icon} {stage.Stage}: {stage.Message}");
            }

            foreach (var warning in result.Warnings)
            {
                AppendLog($"[WARN] {warning}");
            }

            if (result.Success)
            {
                AppendLog($"Patched APK created: {result.OutputApkPath}");
                await _dialogService.ShowInfoAsync($"Patch completed successfully.\nOutput: {result.OutputApkPath}", "Patch complete");
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    AppendLog($"[ERROR] {error}");
                }

                await _dialogService.ShowErrorAsync("Patch failed. See console output for details.", "Patch failed");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
            await _dialogService.ShowErrorAsync(ex.Message, "Patch failed");
        }
        finally
        {
            IsRunning = false;
            RunPatchCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRunPatch()
    {
        return !IsRunning
            && !string.IsNullOrWhiteSpace(ApkPath)
            && !string.IsNullOrWhiteSpace(OutputApkPath);
    }

    private void UpdateOutputApkPath()
    {
        var folder = string.IsNullOrWhiteSpace(OutputFolderPath) ? EnsureCompiledDirectory() : OutputFolderPath;
        OutputFolderPath = folder;

        if (string.IsNullOrWhiteSpace(OutputApkName))
        {
            OutputApkPath = folder;
            return;
        }

        OutputApkPath = Path.Combine(folder, OutputApkName);
    }

    private string EnsureCompiledDirectory()
    {
        var preferred = PathUtils.GetDefaultCompiledPath();
        try
        {
            Directory.CreateDirectory(preferred);
            return preferred;
        }
        catch
        {
            return Directory.GetCurrentDirectory();
        }
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(ConsoleLog) || ConsoleLog == "Waiting for command...")
        {
            ConsoleLog = message;
        }
        else
        {
            ConsoleLog += $"{Environment.NewLine}{message}";
        }
    }

    private void SetConsoleLog(string message)
    {
        ConsoleLog = message;
    }

    private void UpdateCommandPreview()
    {
        if (IsRunning)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Patch preview:");
        builder.AppendLine($"Input APK: {(string.IsNullOrWhiteSpace(ApkPath) ? "<select apk>" : ApkPath)}");
        builder.AppendLine($"Output APK: {(string.IsNullOrWhiteSpace(OutputApkPath) ? "<output apk>" : OutputApkPath)}");
        builder.AppendLine($"Decode resources: {DecodeResources}");
        builder.AppendLine($"Decode sources: {DecodeSources}");
        builder.AppendLine($"Use AAPT2: {UseAapt2ForBuild}");
        builder.Append($"Sign output: {SignApk}");
        ConsoleLog = builder.ToString();
    }

    private static string BuildRunSummary(PatchRequest request)
    {
        return $"Patching '{request.InputApkPath}' -> '{request.OutputApkPath}' (sign={request.SignOutput}, decodeRes={request.DecodeResources}, decodeSrc={request.DecodeSources}, aapt2={request.UseAapt2ForBuild})";
    }
}
