using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Services;
using PulseAPK.Core.Utils;
using Properties = PulseAPK.Core.Properties;

namespace PulseAPK.Core.ViewModels;

public partial class DecompileViewModel : ObservableObject, IDisposable
{
    private const int MaxLogCharacters = 900_000;
    private const int LogTrimTargetCharacters = 850_000;
    private const int LogFlushDelayMilliseconds = 150;

    private readonly Queue<string> _logLines = new();
    private readonly object _logLock = new();
    private int _logCharCount;
    private bool _logFlushScheduled;
    private long _logVersion;
    private CancellationTokenSource? _logFlushCancellationTokenSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHintVisible))]
    [NotifyCanExecuteChangedFor(nameof(RunDecompileCommand))]
    private string _apkPath = string.Empty;

    [ObservableProperty]
    private bool _decodeResources = true;

    [ObservableProperty]
    private bool _decodeSources = true;

    [ObservableProperty]
    private bool _keepOriginalManifest;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunDecompileCommand))]
    private bool _extractToApkFolder;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunDecompileCommand))]
    private string? _outputFolder;

    [ObservableProperty]
    private string _consoleLog;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunDecompileCommand))]
    private bool _isRunning;

    private bool _isConsolePreviewActive = true;
    private bool _disposed;
    private CancellationTokenSource? _activeDecompileCancellationTokenSource;

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

        string normalizedOutputDir;

        try
        {
            var outputDir = ResolveOutputDirectory();
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                await _dialogService.ShowErrorAsync("Unable to derive an output folder from the selected APK. Choose an output folder and try again.", "Invalid output folder");
                return;
            }

            normalizedOutputDir = Path.GetFullPath(outputDir);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            AppendLog($"{Properties.Resources.DecompileFailed}: {ex.Message}");
            await _dialogService.ShowErrorAsync($"The selected output folder is invalid: {ex.Message}", "Invalid output folder");
            return;
        }

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
        var cancellationTokenSource = new CancellationTokenSource();
        _activeDecompileCancellationTokenSource = cancellationTokenSource;

        try
        {
            var decompileResult = await _apktoolRunner.RunDecompileAsync(ApkPath, normalizedOutputDir, DecodeResources, DecodeSources, KeepOriginalManifest, forceOverwrite, cancellationTokenSource.Token);
            var exitCode = decompileResult.ExitCode;

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
        catch (OperationCanceledException)
        {
            AppendLog("Decompile canceled.");
        }
        catch (Exception ex)
        {
            AppendLog($"{Properties.Resources.DecompileFailed}: {ex.Message}");
            await _dialogService.ShowErrorAsync(ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_activeDecompileCancellationTokenSource, cancellationTokenSource))
            {
                _activeDecompileCancellationTokenSource = null;
            }

            cancellationTokenSource.Dispose();
            IsRunning = false;
            RunDecompileCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnOutputDataReceived(string message)
    {
        QueueLogAppend(message);
    }

    private void QueueLogAppend(string message)
    {
        if (_disposed)
        {
            return;
        }

        AppendLog(message);
    }

    private void AppendLog(string message)
    {
        _isConsolePreviewActive = false;

        lock (_logLock)
        {
            var sanitized = message ?? string.Empty;
            _logLines.Enqueue(sanitized);
            _logCharCount += sanitized.Length;

            TrimLogIfNeeded();
            _logVersion++;
            ScheduleLogFlushLocked();
        }
    }

    private void SetConsoleLog(string message)
    {
        _isConsolePreviewActive = false;

        lock (_logLock)
        {
            _logLines.Clear();
            _logCharCount = 0;

            var sanitized = message ?? string.Empty;
            _logLines.Enqueue(sanitized);
            _logCharCount = sanitized.Length;
            _logVersion++;

            ScheduleLogFlushLocked();
        }
    }

    private void ScheduleLogFlushLocked()
    {
        if (_disposed || _logFlushScheduled)
        {
            return;
        }

        _logFlushCancellationTokenSource ??= new CancellationTokenSource();
        var cancellationToken = _logFlushCancellationTokenSource.Token;
        _logFlushScheduled = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(LogFlushDelayMilliseconds, cancellationToken);
                FlushLog(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }, cancellationToken);
    }

    private void FlushLog(CancellationToken cancellationToken)
    {
        string logText;
        long logVersion;

        lock (_logLock)
        {
            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                _logFlushScheduled = false;
                return;
            }

            logText = string.Join(Environment.NewLine, _logLines);
            logVersion = _logVersion;
            _logFlushScheduled = false;
        }

        if (!_dispatcherService.CheckAccess())
        {
            _ = InvokeDispatcherSafelyAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                if (ShouldApplyLogFlush(logVersion, cancellationToken))
                {
                    ConsoleLog = logText;
                }
            });
        }
        else if (ShouldApplyLogFlush(logVersion, cancellationToken))
        {
            ConsoleLog = logText;
        }
    }

    private async Task InvokeDispatcherSafelyAsync(Action action)
    {
        try
        {
            await _dispatcherService.InvokeAsync(action);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to dispatch decompile log update: {ex}");
        }
    }

    private bool ShouldApplyLogFlush(long logVersion, CancellationToken cancellationToken)
    {
        lock (_logLock)
        {
            return !_disposed
                && !cancellationToken.IsCancellationRequested
                && logVersion == _logVersion;
        }
    }

    private void TrimLogIfNeeded()
    {
        var newlineLength = Environment.NewLine.Length;
        var totalCharacters = _logCharCount + ((_logLines.Count - 1) * newlineLength);
        if (totalCharacters <= MaxLogCharacters)
        {
            return;
        }

        while (_logLines.Count > 0 && totalCharacters > LogTrimTargetCharacters)
        {
            var removed = _logLines.Dequeue();
            _logCharCount -= removed.Length;
            totalCharacters = _logCharCount + ((_logLines.Count - 1) * newlineLength);
        }
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
        if (IsRunning || string.IsNullOrWhiteSpace(ApkPath) || !File.Exists(ApkPath))
        {
            return false;
        }

        try
        {
            return !string.IsNullOrWhiteSpace(ResolveOutputDirectory());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
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

        var apkDirectory = Path.GetDirectoryName(apkPath);
        if (string.IsNullOrWhiteSpace(apkDirectory))
        {
            apkDirectory = Directory.GetCurrentDirectory();
        }

        return Path.Combine(apkDirectory, Path.GetFileNameWithoutExtension(apkPath));
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _activeDecompileCancellationTokenSource?.Cancel();
        _activeDecompileCancellationTokenSource?.Dispose();
        _activeDecompileCancellationTokenSource = null;

        lock (_logLock)
        {
            _logFlushCancellationTokenSource?.Cancel();
            _logFlushCancellationTokenSource?.Dispose();
            _logFlushCancellationTokenSource = null;
            _logFlushScheduled = false;
        }

        _apktoolRunner.OutputDataReceived -= OnOutputDataReceived;
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
