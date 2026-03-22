using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Services;
using PulseAPK.Core.Utils;
using Properties = PulseAPK.Core.Properties;

namespace PulseAPK.Core.ViewModels;

public partial class DecompileViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHintVisible))]
    private string _apkPath = string.Empty;

    [ObservableProperty]
    private bool _decodeResources = true;

    [ObservableProperty]
    private bool _decodeSources = true;

    [ObservableProperty]
    private bool _keepOriginalManifest;

    [ObservableProperty]
    private bool _extractToApkFolder;

    [ObservableProperty]
    private string? _outputFolder;

    [ObservableProperty]
    private string _consoleLog;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunDecompileCommand))]
    private bool _isRunning;

    private bool _isConsolePreviewActive = true;

    private readonly IFilePickerService _filePickerService;
    private readonly ISettingsService _settingsService;
    private readonly ApktoolRunner _apktoolRunner;
    private readonly IDialogService _dialogService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ISystemService _systemService;

    public bool IsHintVisible => string.IsNullOrEmpty(ApkPath);

    public DecompileViewModel(
        IFilePickerService filePickerService,
        ISettingsService settingsService,
        ApktoolRunner apktoolRunner,
        IDialogService dialogService,
        IDispatcherService dispatcherService,
        ISystemService systemService)
    {
        _filePickerService = filePickerService;
        _settingsService = settingsService;
        _apktoolRunner = apktoolRunner;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;
        _systemService = systemService;
        
        _consoleLog = Properties.Resources.WaitingForCommand;

        _apktoolRunner.OutputDataReceived += OnOutputDataReceived;

        OutputFolder = PathUtils.GetDefaultDecompilePath();

        UpdateCommandPreview();
        RunDecompileCommand.NotifyCanExecuteChanged();
    }

    partial void OnApkPathChanged(string value)
    {
        if (ExtractToApkFolder)
        {
            var apkDerivedOutputFolder = GetApkDerivedOutputFolder(value);
            if (!string.IsNullOrWhiteSpace(apkDerivedOutputFolder))
            {
                OutputFolder = apkDerivedOutputFolder;
            }
        }

        UpdateCommandPreview();
        RunDecompileCommand.NotifyCanExecuteChanged();
    }

    partial void OnDecodeResourcesChanged(bool value) => UpdateCommandPreview();
    partial void OnDecodeSourcesChanged(bool value) => UpdateCommandPreview();
    partial void OnKeepOriginalManifestChanged(bool value) => UpdateCommandPreview();

    partial void OnExtractToApkFolderChanged(bool value)
    {
        if (value)
        {
            var apkDerivedOutputFolder = GetApkDerivedOutputFolder(ApkPath);
            if (!string.IsNullOrWhiteSpace(apkDerivedOutputFolder))
            {
                OutputFolder = apkDerivedOutputFolder;
            }
        }
        else
        {
            OutputFolder = PathUtils.GetDefaultDecompilePath();
        }

        UpdateCommandPreview();
    }

    partial void OnOutputFolderChanged(string? value) => UpdateCommandPreview();

    [RelayCommand]
    private async Task BrowseApk()
    {
        var file = await _filePickerService.OpenFileAsync("APK Files (*.apk)|*.apk|All Files (*.*)|*.*");
        if (file != null)
        {
            var (isValid, message) = FileSanitizer.ValidateApk(file);
            if (!isValid)
            {
                await _dialogService.ShowErrorAsync(message, "Invalid APK File");
                return;
            }
            ApkPath = file;
        }
    }

    [RelayCommand]
    private async Task BrowseOutputFolder()
    {
        var initialDir = OutputFolder;
        
        if (!string.IsNullOrWhiteSpace(initialDir) && !Directory.Exists(initialDir))
        {
             initialDir = null;
        }

        var folder = await _filePickerService.OpenFolderAsync(initialDir);

        if (folder != null)
        {
            OutputFolder = folder;
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        var path = OutputFolder;
        if (string.IsNullOrWhiteSpace(path) )
        {
             _dialogService.ShowWarningAsync(Properties.Resources.Error_OutputFolderNotSet);
             return;
        }
        
        if (!Directory.Exists(path))
        {
            _dialogService.ShowWarningAsync(string.Format(Properties.Resources.Error_FolderNotFound, path));
            return;
        }

        _systemService.OpenFolder(path);
    }

    [RelayCommand(CanExecute = nameof(CanRunDecompile))]
    private async Task RunDecompile()
    {
        if (string.IsNullOrWhiteSpace(ApkPath))
        {
            await _dialogService.ShowWarningAsync("Please select an APK file to decompile.", "Missing APK");
            return;
        }

        var apktoolPath = _settingsService.Settings.ApktoolPath?.Trim();
        if (string.IsNullOrWhiteSpace(apktoolPath))
        {
            await _dialogService.ShowWarningAsync(Properties.Resources.Error_MissingApktool, Properties.Resources.SettingsHeader);
            return;
        }

        if (!File.Exists(apktoolPath))
        {
            await _dialogService.ShowErrorAsync(string.Format(Properties.Resources.Error_InvalidApktoolPath, apktoolPath), Properties.Resources.Error_InvalidApkFile);
            RunDecompileCommand.NotifyCanExecuteChanged();
            return;
        }

        SetConsoleLog(Properties.Resources.StartingApktool);

        var outputDir = ResolveOutputDirectory();
        var normalizedOutputDir = Path.GetFullPath(outputDir);

        if (IsHighRiskOutputDirectory(normalizedOutputDir))
        {
            await _dialogService.ShowErrorAsync($"The selected output folder '{normalizedOutputDir}' is unsafe. Choose a different location.", "Invalid output folder");
            return;
        }

        var forceOverwrite = false;

        if (Directory.Exists(normalizedOutputDir))
        {
            var isEmpty = !Directory.EnumerateFileSystemEntries(normalizedOutputDir).Any();

            if (!isEmpty)
            {
                var result = await _dialogService.ShowQuestionAsync($"The output directory '{normalizedOutputDir}' already exists and is not empty. Overwrite its contents?", "Confirm overwrite");

                if (!result)
                {
                    return;
                }
            }

            forceOverwrite = true;
        }

        IsRunning = true;

        try
        {
            var exitCode = await _apktoolRunner.RunDecompileAsync(ApkPath, normalizedOutputDir, DecodeResources, DecodeSources, KeepOriginalManifest, forceOverwrite);

            if (exitCode == 0)
            {
                AppendLog(Properties.Resources.DecompileSuccessful);
                await _dialogService.ShowInfoAsync(Properties.Resources.DecompileSuccessful);
            }
            else
            {
                AppendLog($"{Properties.Resources.DecompileFailed} (Exit Code: {exitCode})");
                await _dialogService.ShowErrorAsync(Properties.Resources.DecompileFailed);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"{Properties.Resources.DecompileFailed}: {ex.Message}");
            await _dialogService.ShowErrorAsync(ex.Message);
        }
        finally
        {
            IsRunning = false;
            RunDecompileCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnOutputDataReceived(string message)
    {
        if (!_dispatcherService.CheckAccess())
        {
            _dispatcherService.InvokeAsync(() => AppendLog(message));
        }
        else
        {
            AppendLog(message);
        }
    }

    private void AppendLog(string message)
    {
        _isConsolePreviewActive = false;

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
        _isConsolePreviewActive = false;
        ConsoleLog = message;
    }

    private void UpdateCommandPreview()
    {
        if (!_isConsolePreviewActive)
        {
            return;
        }

        ConsoleLog = BuildCommandPreview();
    }

    private bool CanRunDecompile()
    {
        return !IsRunning;
    }

    private string BuildCommandPreview()
    {
        var apktoolPath = _settingsService.Settings.ApktoolPath?.Trim();
        var apktool = string.IsNullOrWhiteSpace(apktoolPath)
            ? "<set apktool path>"
            : $"\"{apktoolPath}\"";

        var apkInput = string.IsNullOrWhiteSpace(ApkPath)
            ? "<select apk>"
            : $"\"{ApkPath}\"";

        var outputDir = ResolveOutputDirectoryPreview();

        var builder = new StringBuilder();
        builder.Append($"java -jar {apktool} d {apkInput} -o \"{outputDir}\"");

        if (!DecodeResources) builder.Append(" -r");
        if (!DecodeSources) builder.Append(" -s");
        if (KeepOriginalManifest) builder.Append(" -m");

        return $"Command preview: {builder}";
    }

    private string ResolveOutputDirectory()
    {
        if (ExtractToApkFolder)
        {
            return GetApkDerivedOutputFolder(ApkPath);
        }

        return !string.IsNullOrWhiteSpace(OutputFolder)
            ? OutputFolder
            : GetApkDerivedOutputFolder(ApkPath);
    }

    private string ResolveOutputDirectoryPreview()
    {
        if (ExtractToApkFolder)
        {
            return !string.IsNullOrWhiteSpace(ApkPath)
                ? GetApkDerivedOutputFolder(ApkPath)
                : "<apk folder>";
        }

        return !string.IsNullOrWhiteSpace(OutputFolder)
            ? OutputFolder
            : !string.IsNullOrWhiteSpace(ApkPath)
                ? GetApkDerivedOutputFolder(ApkPath)
                : "<output folder>";
    }

    private static string GetApkDerivedOutputFolder(string? apkPath)
    {
        if (string.IsNullOrWhiteSpace(apkPath))
        {
            return string.Empty;
        }

        return Path.Combine(Path.GetDirectoryName(apkPath)!, Path.GetFileNameWithoutExtension(apkPath));
    }

    private static bool IsHighRiskOutputDirectory(string outputDir)
    {
        var normalizedOutput = NormalizePath(outputDir);
        var outputRoot = Path.GetPathRoot(normalizedOutput);

        var riskyPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        };

        if (!string.IsNullOrWhiteSpace(outputRoot) && string.Equals(normalizedOutput, NormalizePath(outputRoot), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return riskyPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Any(riskyPath => IsSameOrSubPath(riskyPath, normalizedOutput));
    }

    private static bool IsSameOrSubPath(string basePath, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(basePath, candidatePath, comparison))
        {
            return true;
        }

        if (basePath.Length == 1 && (basePath[0] == Path.DirectorySeparatorChar || basePath[0] == Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        var basePathWithSeparator = basePath.EndsWith(Path.DirectorySeparatorChar)
            ? basePath
            : basePath + Path.DirectorySeparatorChar;

        return candidatePath.StartsWith(basePathWithSeparator, comparison);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var root = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrWhiteSpace(root) && string.Equals(fullPath, root, comparison))
        {
            return root.Length > 1
                ? root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : root;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
