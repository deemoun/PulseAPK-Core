using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Services;
using PulseAPK.Core.Utils;
using Properties = PulseAPK.Core.Properties;

namespace PulseAPK.Core.ViewModels;

public partial class BuildViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHintVisible))]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    private string _projectPath = string.Empty;

    [ObservableProperty]
    private string _outputApkPath = string.Empty;

    [ObservableProperty]
    private string _outputFolderPath = string.Empty;

    [ObservableProperty]
    private string _outputApkName = string.Empty;

    [ObservableProperty]
    private bool _useAapt2;

    [ObservableProperty]
    private bool _signApk = true;

    [ObservableProperty]
    private string _consoleLog;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunBuildCommand))]
    private bool _isRunning;

    private bool _isConsolePreviewActive = true;

    private readonly IFilePickerService _filePickerService;
    private readonly ISettingsService _settingsService;
    private readonly ApktoolRunner _apktoolRunner;
    private readonly UbersignRunner _ubersignRunner;
    private readonly IDialogService _dialogService;
    private readonly IDispatcherService _dispatcherService;

    public bool IsHintVisible => string.IsNullOrEmpty(ProjectPath);

    public bool HasProject => !string.IsNullOrWhiteSpace(ProjectPath);

    public BuildViewModel(
        IFilePickerService filePickerService,
        ISettingsService settingsService,
        ApktoolRunner apktoolRunner,
        UbersignRunner ubersignRunner,
        IDialogService dialogService,
        IDispatcherService dispatcherService)
    {
        _filePickerService = filePickerService;
        _settingsService = settingsService;
        _apktoolRunner = apktoolRunner;
        _ubersignRunner = ubersignRunner;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;
        
        _consoleLog = Properties.Resources.WaitingForCommand;

        InitializeOutputPath();

        _apktoolRunner.OutputDataReceived += OnOutputDataReceived;
        _ubersignRunner.OutputDataReceived += OnOutputDataReceived;

        UpdateCommandPreview();
        RunBuildCommand.NotifyCanExecuteChanged();
    }

    partial void OnProjectPathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var sanitizedPath = value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(sanitizedPath);
            var compiledDir = EnsureCompiledDirectory();

            OutputFolderPath = compiledDir;
            OutputApkName = $"{folderName}.apk";
        }
        UpdateOutputApkPath();
        RunBuildCommand.NotifyCanExecuteChanged();
    }

    partial void OnOutputApkPathChanged(string value) => UpdateCommandPreview();
    partial void OnOutputFolderPathChanged(string value)
    {
        EnsureOutputFolderPathInitialized();

        UpdateOutputApkPath();
        BrowseOutputApkCommand.NotifyCanExecuteChanged();
    }
    partial void OnOutputApkNameChanged(string value)
    {
        UpdateOutputApkPath();
        RunBuildCommand.NotifyCanExecuteChanged();
    }
    partial void OnUseAapt2Changed(bool value) => UpdateCommandPreview();
    partial void OnSignApkChanged(bool value) => UpdateCommandPreview();

    [RelayCommand]
    private async Task BrowseProject()
    {
        var folder = await _filePickerService.OpenFolderAsync(ProjectPath);

        if (folder != null)
        {
            var (isValid, message) = FileSanitizer.ValidateProjectFolder(folder);
            if (!isValid)
            {
                await _dialogService.ShowWarningAsync(message, Properties.Resources.Warning_InvalidProjectFolder);
                return;
            }
            ProjectPath = folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowseOutputApk))]
    private async Task BrowseOutputApk()
    {
        var folder = await _filePickerService.OpenFolderAsync(OutputFolderPath);

        if (!string.IsNullOrWhiteSpace(folder))
        {
            OutputFolderPath = folder;
        }
    }

    private bool CanBrowseOutputApk() => !string.IsNullOrWhiteSpace(OutputFolderPath);

    [RelayCommand(CanExecute = nameof(CanRunBuild))]
    private async Task RunBuild()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
             await _dialogService.ShowWarningAsync(Properties.Resources.SelectProjectFolder, Properties.Resources.BuildHeader);
             return;
        }

        var apktoolPath = _settingsService.Settings.ApktoolPath?.Trim();
         if (string.IsNullOrWhiteSpace(apktoolPath) || !File.Exists(apktoolPath))
        {
            await _dialogService.ShowErrorAsync(string.Format(Properties.Resources.Error_InvalidApktoolPath, apktoolPath), Properties.Resources.SettingsHeader);
            return;
        }

        if (File.Exists(OutputApkPath))
        {
             var result = await _dialogService.ShowQuestionAsync($"The output file '{OutputApkPath}' already exists. Overwrite?", "Confirm overwrite");
             if (!result) return;
        }

        var signedApkPath = SignApk ? GetSignedApkPath(OutputApkPath) : string.Empty;
        if (SignApk && !string.IsNullOrWhiteSpace(signedApkPath) && File.Exists(signedApkPath))
        {
            var result = await _dialogService.ShowQuestionAsync($"The signed output file '{signedApkPath}' already exists. Overwrite?", "Confirm overwrite");
            if (!result) return;
        }

        SetConsoleLog(Properties.Resources.StartingApktool);
        IsRunning = true;

        try
        {
            var exitCode = await _apktoolRunner.RunBuildAsync(ProjectPath, OutputApkPath, UseAapt2);

            if (exitCode == 0)
            {
                AppendLog(Properties.Resources.BuildSuccessful);

                if (SignApk && !string.IsNullOrWhiteSpace(signedApkPath))
                {
                    await RunSigningAsync(OutputApkPath, signedApkPath);
                }

                 await _dialogService.ShowInfoAsync(Properties.Resources.BuildSuccessful);
            }
            else
            {
                 AppendLog($"{Properties.Resources.BuildFailed} (Exit Code: {exitCode})");
                 await _dialogService.ShowErrorAsync(Properties.Resources.BuildFailed);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"{Properties.Resources.BuildFailed}: {ex.Message}");
            await _dialogService.ShowErrorAsync(ex.Message);
        }
        finally
        {
            IsRunning = false;
            RunBuildCommand.NotifyCanExecuteChanged();
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
        if (!_isConsolePreviewActive) return;
         ConsoleLog = BuildCommandPreview();
    }

    private bool CanRunBuild()
    {
        return !IsRunning
            && HasProject
            && !string.IsNullOrWhiteSpace(OutputApkName)
            && !string.IsNullOrWhiteSpace(OutputApkPath)
            && !Directory.Exists(OutputApkPath);
    }

    private string BuildCommandPreview()
    {
         var apktoolPath = _settingsService.Settings.ApktoolPath?.Trim();
        var apktool = string.IsNullOrWhiteSpace(apktoolPath) ? "<set apktool path>" : $"\"{apktoolPath}\"";
        var project = string.IsNullOrWhiteSpace(ProjectPath) ? "<select project>" : $"\"{ProjectPath}\"";
        var hasOutputPath = !string.IsNullOrWhiteSpace(OutputApkPath);
        var output = hasOutputPath ? $"\"{OutputApkPath}\"" : "<output apk>";

        var builder = new StringBuilder();
        builder.Append($"java -jar {apktool} b {project} -o {output}");
        if(UseAapt2) builder.Append(" --use-aapt2");

        var commandPreview = new StringBuilder($"Command preview: {builder}");

        var signingCommandPreview = BuildSigningCommandPreview(OutputApkPath);
        if (!string.IsNullOrWhiteSpace(signingCommandPreview))
        {
            commandPreview.Append($"{Environment.NewLine}{signingCommandPreview}");
        }

        return commandPreview.ToString();
    }

    private void InitializeOutputPath()
    {
        OutputFolderPath = EnsureCompiledDirectory();
        OutputApkName = string.Empty;
        OutputApkPath = OutputFolderPath;
    }

    private string EnsureCompiledDirectory()
    {
        var preferredCompiledDir = PathUtils.GetDefaultCompiledPath();

        if (TryEnsureDirectory(preferredCompiledDir, out var ensuredCompiledDir))
        {
            return ensuredCompiledDir;
        }

        var fallbackCompiledDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "compiled");
        if (TryEnsureDirectory(fallbackCompiledDir, out var ensuredFallbackDir))
        {
            return ensuredFallbackDir;
        }

        return Directory.GetCurrentDirectory();
    }


    private static bool TryEnsureDirectory(string path, out string ensuredPath)
    {
        ensuredPath = path;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
    private string GetApplicationRootPath()
    {
        return string.IsNullOrWhiteSpace(AppDomain.CurrentDomain.BaseDirectory)
            ? Directory.GetCurrentDirectory()
            : AppDomain.CurrentDomain.BaseDirectory;
    }

    private void UpdateOutputApkPath()
    {
        var folderPath = EnsureOutputFolderPathInitialized();

        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(OutputApkName))
        {
            OutputApkPath = string.IsNullOrWhiteSpace(folderPath) ? string.Empty : folderPath;
            UpdateCommandPreview();
            RunBuildCommand.NotifyCanExecuteChanged();
            return;
        }

        OutputApkPath = Path.Combine(folderPath, OutputApkName);
        UpdateCommandPreview();
        RunBuildCommand.NotifyCanExecuteChanged();
    }

    private string EnsureOutputFolderPathInitialized()
    {
        if (string.IsNullOrWhiteSpace(OutputFolderPath))
        {
            var defaultPath = EnsureCompiledDirectory();
            if (!string.Equals(OutputFolderPath, defaultPath, StringComparison.OrdinalIgnoreCase))
            {
                OutputFolderPath = defaultPath;
            }
        }

        return OutputFolderPath;
    }

    private async Task RunSigningAsync(string inputApk, string signedApkPath)
    {
        AppendLog($"Signing APK via ubersign to '{signedApkPath}'...");

        try
        {
            var exitCode = await _ubersignRunner.RunSigningAsync(inputApk, signedApkPath);

            if (exitCode == 0)
            {
                AppendLog($"Signed APK created at '{signedApkPath}'.");
            }
            else
            {
                AppendLog($"Signing failed (Exit Code: {exitCode}).");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Signing failed: {ex.Message}");
            await _dialogService.ShowWarningAsync(ex.Message, "Signing failed");
        }
    }

    private string GetSignedApkPath(string outputApkPath)
    {
        if (string.IsNullOrWhiteSpace(outputApkPath))
        {
            return string.Empty;
        }

        var folder = Path.GetDirectoryName(outputApkPath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return string.Empty;
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(outputApkPath);
        var extension = Path.GetExtension(outputApkPath);

        return Path.Combine(folder, $"{fileNameWithoutExtension}_signed{extension}");
    }

    private string BuildSigningCommandPreview(string outputApk)
    {
        if (!SignApk)
        {
            return string.Empty;
        }

        var hasOutputPath = !string.IsNullOrWhiteSpace(outputApk) && outputApk != "<output apk>";
        var sanitizedOutputApk = hasOutputPath ? outputApk.Trim().Trim('"') : string.Empty;
        var hasExtension = hasOutputPath && !string.IsNullOrWhiteSpace(Path.GetExtension(sanitizedOutputApk));

        var signedApk = hasExtension ? GetSignedApkPath(sanitizedOutputApk) : string.Empty;

        string signingCommand;

        var appRoot = GetApplicationRootPath();
        var configuredUbersign = _settingsService.Settings.UbersignPath?.Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(configuredUbersign))
        {
            var resolvedUbersign = Path.IsPathRooted(configuredUbersign)
                ? configuredUbersign
                : Path.Combine(appRoot, configuredUbersign);

            var configuredExists = File.Exists(resolvedUbersign);
            var isJar = string.Equals(Path.GetExtension(resolvedUbersign), ".jar", StringComparison.OrdinalIgnoreCase);

            signingCommand = isJar
                ? $"java -jar \"{resolvedUbersign}\""
                : $"\"{resolvedUbersign}\"";

            if (!configuredExists)
            {
                signingCommand = $"<{signingCommand} (not found)>";
            }
        }
        else
        {
            var ubersignJarPath = Path.Combine(appRoot, "ubersign.jar");
            var ubersignPath = Path.Combine(appRoot, "ubersign");
            var windowsUbersign = $"{ubersignPath}.exe";

            if (File.Exists(ubersignJarPath))
            {
                signingCommand = $"java -jar \"{ubersignJarPath}\"";
            }
            else if (File.Exists(ubersignPath))
            {
                signingCommand = $"\"{ubersignPath}\"";
            }
            else if (File.Exists(windowsUbersign))
            {
                signingCommand = $"\"{windowsUbersign}\"";
            }
            else
            {
                signingCommand = "<ubersign.jar in app root>";
            }
        }

        if (!hasOutputPath)
        {
            return "Signing preview: ubersign -a <output apk> -o <output folder>";
        }

        var sanitizedSignedApk = signedApk.Trim().Trim('"');

        var outputFolder = hasExtension
            ? Path.GetDirectoryName(sanitizedSignedApk) ?? string.Empty
            : sanitizedOutputApk;

        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            outputFolder = EnsureCompiledDirectory();
        }

        return $"Signing preview: {signingCommand} -a \"{sanitizedOutputApk}\" -o \"{outputFolder}\"";
    }
}
