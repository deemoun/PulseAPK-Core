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


public sealed record DexPreservationOption(string Label, DexPreservationMode Mode);
public sealed record ScriptInjectionOption(string Label, bool IsEnabledForSelection);

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
    private bool _injectLibForAllArchitectures;

    [ObservableProperty]
    private bool _skipDexValidation;

    [ObservableProperty]
    private bool _addCustomScript;

    [ObservableProperty]
    private DexPreservationOption _selectedDexPreservationOption = new("Disabled (default)", DexPreservationMode.Disabled);

    [ObservableProperty]
    private ScriptInjectionOption _selectedScriptInjectionOption = new("Inject frida-gadget", false);

    [ObservableProperty]
    private string _consoleLog;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunPatchCommand))]
    private bool _isRunning;

    private readonly IFilePickerService _filePickerService;
    private readonly ISettingsService _settingsService;
    private readonly IPatchPipelineService _patchPipelineService;
    private readonly IDialogService _dialogService;
    private readonly LocalizationService _localizationService;
    private readonly ISystemService _systemService;

    public bool IsHintVisible => string.IsNullOrWhiteSpace(ApkPath);

    public IReadOnlyList<DexPreservationOption> DexPreservationOptions { get; }

    public IReadOnlyList<ScriptInjectionOption> ScriptInjectionOptions { get; }

    public PatchViewModel(
        IFilePickerService filePickerService,
        ISettingsService settingsService,
        IPatchPipelineService patchPipelineService,
        IDialogService dialogService,
        LocalizationService localizationService,
        ISystemService systemService)
    {
        _filePickerService = filePickerService;
        _settingsService = settingsService;
        _patchPipelineService = patchPipelineService;
        _dialogService = dialogService;
        _localizationService = localizationService;
        _systemService = systemService;

        DexPreservationOptions =
        [
            new(L("PatchDexDisabledDefault"), DexPreservationMode.Disabled),
            new(L("PatchDexPreserveUnmodifiedSecondary"), DexPreservationMode.PreserveUnmodifiedSecondaryDexFiles),
            new(L("PatchDexReplaceAllDangerous"), DexPreservationMode.ReplaceAllDexFiles)
        ];

        ScriptInjectionOptions =
        [
            new(L("PatchScriptInjectFridaGadget"), false)
        ];

        _consoleLog = Properties.Resources.WaitingForCommand;

        SelectedDexPreservationOption = DexPreservationOptions[0];
        SelectedScriptInjectionOption = ScriptInjectionOptions[0];

        OutputFolderPath = EnsureCompiledDirectory();
        EnsureUserScriptTemplatesExist();
        OutputApkName = L("PatchOutputApkNamePlaceholder");
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
    partial void OnInjectLibForAllArchitecturesChanged(bool value) => UpdateCommandPreview();
    partial void OnSkipDexValidationChanged(bool value) => UpdateCommandPreview();
    partial void OnAddCustomScriptChanged(bool value) => UpdateCommandPreview();
    partial void OnSelectedDexPreservationOptionChanged(DexPreservationOption value) => UpdateCommandPreview();
    partial void OnSelectedScriptInjectionOptionChanged(ScriptInjectionOption value) => UpdateCommandPreview();

    [RelayCommand]
    private async Task BrowseApk()
    {
        var file = await _filePickerService.OpenFileAsync(Properties.Resources.FileFilter_Apk);
        if (file is null)
        {
            return;
        }

        var (isValid, message) = FileSanitizer.ValidateApk(file);
        if (!isValid)
        {
            await _dialogService.ShowErrorAsync(message, Properties.Resources.Error_InvalidApkFile);
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

    [RelayCommand]
    private async Task OpenOutputFolder()
    {
        var folder = OutputFolderPath;
        if (string.IsNullOrWhiteSpace(folder))
        {
            await _dialogService.ShowWarningAsync(Properties.Resources.Error_OutputFolderNotSet);
            return;
        }

        if (!Directory.Exists(folder))
        {
            await _dialogService.ShowWarningAsync(string.Format(Properties.Resources.Error_FolderNotFound, folder));
            return;
        }

        _systemService.OpenFolder(folder);
    }

    [RelayCommand(CanExecute = nameof(CanRunPatch))]
    private async Task RunPatch()
    {
        if (string.IsNullOrWhiteSpace(ApkPath))
        {
            await _dialogService.ShowWarningAsync(L("PatchErrorSelectApk"), L("PatchErrorMissingApkTitle"));
            return;
        }

        var apktoolPath = _settingsService.Settings.ApktoolPath?.Trim();
        if (string.IsNullOrWhiteSpace(apktoolPath) || !File.Exists(apktoolPath))
        {
            await _dialogService.ShowErrorAsync(string.Format(Properties.Resources.Error_InvalidApktoolPath, apktoolPath), Properties.Resources.SettingsHeader);
            return;
        }

        var selectedDexMode = SelectedDexPreservationOption.Mode;
        var confirmedDangerousDexMode = false;
        if (selectedDexMode == DexPreservationMode.ReplaceAllDexFiles)
        {
            confirmedDangerousDexMode = await _dialogService.ShowQuestionAsync(
                L("PatchDangerousDexConfirmation"),
                L("PatchDangerousDexTitle"));
            if (!confirmedDangerousDexMode)
            {
                AppendLog(L("PatchLogDangerousDexCancelled"));
                return;
            }
        }

        IsRunning = true;
        SetConsoleLog(L("PatchLogStartingPipeline"));

        try
        {
            var request = new PatchRequest
            {
                InputApkPath = ApkPath,
                OutputApkPath = OutputApkPath,
                SignOutput = SignApk,
                DecodeResources = true,
                DecodeSources = true,
                UseAapt2ForBuild = false,
                WorkingDirectory = Path.Combine(Path.GetTempPath(), "pulseapk-patch-ui"),
                KeepIntermediateFiles = false,
                PreserveOriginalDexFiles = false,
                DexPreservationMode = selectedDexMode,
                ConfirmDangerousDexReplacement = confirmedDangerousDexMode,
                InjectForAllArchitectures = InjectLibForAllArchitectures,
                SkipDexValidation = SkipDexValidation,
                ScriptFilePath = AddCustomScript ? ResolveCustomScriptPath("script.js") : null,
                ConfigFilePath = AddCustomScript ? ResolveCustomScriptPath("frida-gadget.config") : null
            };

            AppendLog(BuildRunSummary(request));
            AppendLog(string.Format(L("PatchLogScriptProfile"), SelectedScriptInjectionOption.Label));
            AppendLog($"AddCustomScript enabled: {AddCustomScript}");
            AppendLog($"Resolved ScriptFilePath: {request.ScriptFilePath ?? "<none>"}");
            AppendLog($"Resolved ConfigFilePath: {request.ConfigFilePath ?? "<none>"}");

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
                AppendLog(string.Format(L("PatchLogCreated"), result.OutputApkPath));
                await _dialogService.ShowInfoAsync(string.Format(L("PatchInfoCompleteMessage"), result.OutputApkPath), L("PatchInfoCompleteTitle"));
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    AppendLog($"[ERROR] {error}");
                }

                await _dialogService.ShowErrorAsync(L("PatchErrorFailedMessage"), L("PatchErrorFailedTitle"));
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
            await _dialogService.ShowErrorAsync(ex.Message, L("PatchErrorFailedTitle"));
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
        if (string.IsNullOrWhiteSpace(ConsoleLog) || ConsoleLog == Properties.Resources.WaitingForCommand)
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
        builder.AppendLine(L("PatchPreviewHeader"));
        builder.AppendLine(string.Format(L("PatchPreviewInputApk"), string.IsNullOrWhiteSpace(ApkPath) ? L("PatchPreviewSelectApk") : ApkPath));
        builder.AppendLine(string.Format(L("PatchPreviewOutputApk"), string.IsNullOrWhiteSpace(OutputApkPath) ? L("PatchPreviewOutputApkPlaceholder") : OutputApkPath));
        builder.AppendLine(L("PatchPreviewDecodeResources"));
        builder.AppendLine(L("PatchPreviewDecodeSources"));
        builder.AppendLine(L("PatchPreviewUseAapt2"));
        builder.AppendLine(string.Format(L("PatchPreviewScriptProfile"), SelectedScriptInjectionOption.Label));
        builder.AppendLine(string.Format(L("PatchPreviewInjectAllArchitectures"), InjectLibForAllArchitectures));
        builder.AppendLine(string.Format(L("PatchPreviewSkipDexValidation"), SkipDexValidation));
        builder.AppendLine($"Add custom script: {AddCustomScript}");
        builder.AppendLine(string.Format(L("PatchPreviewDexPreservation"), SelectedDexPreservationOption.Label));
        builder.Append(string.Format(L("PatchPreviewSignOutput"), SignApk));
        ConsoleLog = builder.ToString();
    }

    private static void EnsureUserScriptTemplatesExist()
    {
        EnsureUserScriptTemplateExists("script.js");
        EnsureUserScriptTemplateExists("frida-gadget.config");
    }

    private static void EnsureUserScriptTemplateExists(string fileName)
    {
        var userScriptsDirectory = PathUtils.GetDefaultScriptsPath();
        Directory.CreateDirectory(userScriptsDirectory);

        var targetPath = Path.Combine(userScriptsDirectory, fileName);
        if (File.Exists(targetPath))
        {
            return;
        }

        var sourcePath = GetBundledCustomScriptPath(fileName);
        if (File.Exists(sourcePath))
        {
            File.Copy(sourcePath, targetPath, overwrite: false);
        }
    }

    private static string ResolveCustomScriptPath(string fileName)
    {
        EnsureUserScriptTemplateExists(fileName);

        var userScriptPath = Path.Combine(PathUtils.GetDefaultScriptsPath(), fileName);
        if (File.Exists(userScriptPath))
        {
            return userScriptPath;
        }

        return GetBundledCustomScriptPath(fileName);
    }

    private static string GetBundledCustomScriptPath(string fileName)
    {
        var fromCurrentDirectory = Path.Combine(Directory.GetCurrentDirectory(), "scripts", fileName);
        if (File.Exists(fromCurrentDirectory))
        {
            return fromCurrentDirectory;
        }

        return Path.Combine(AppContext.BaseDirectory, "scripts", fileName);
    }

    private string L(string key) => _localizationService[key];

    private string BuildRunSummary(PatchRequest request)
    {
        return string.Format(
            L("PatchLogRunSummary"),
            request.InputApkPath,
            request.OutputApkPath,
            request.SignOutput,
            request.DecodeResources,
            request.DecodeSources,
            request.UseAapt2ForBuild,
            request.DexPreservationMode);
    }
}
