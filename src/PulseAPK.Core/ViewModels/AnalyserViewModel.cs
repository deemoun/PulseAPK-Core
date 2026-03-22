using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PulseAPK.Core.Services;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Utils;
using Properties = PulseAPK.Core.Properties;

namespace PulseAPK.Core.ViewModels;

public partial class AnalyserViewModel : ObservableObject
{
    private const int MaxLogCharacters = 900_000;
    private const int LogTrimTargetCharacters = 850_000;
    
    private readonly Queue<string> _logLines = new Queue<string>();
    private readonly object _logLock = new object();
    private int _logCharCount;
    private volatile bool _logFlushPending;
    
    private readonly SmaliAnalyserService _analyserService;
    private readonly ReportService _reportService;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogService _dialogService;
    private readonly IDispatcherService _dispatcherService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHintVisible))]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    private string _projectPath = string.Empty;

    [ObservableProperty]
    private string _consoleLog = Properties.Resources.WaitingForCommand;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunAnalysisCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveReportCommand))]
    private bool _isRunning;

    public bool IsHintVisible => string.IsNullOrEmpty(ProjectPath);
    public bool HasProject => !string.IsNullOrWhiteSpace(ProjectPath);

    public AnalyserViewModel(
        SmaliAnalyserService analyserService,
        ReportService reportService,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        IDispatcherService dispatcherService)
    {
        _analyserService = analyserService;
        _reportService = reportService;
        _filePickerService = filePickerService;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;
        
        StartLogFlushLoop();
    }

    partial void OnProjectPathChanged(string value)
    {
        RunAnalysisCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task BrowseProject()
    {
        var folder = await _filePickerService.OpenFolderAsync(ProjectPath);

        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (!Directory.Exists(folder))
        {
            await _dialogService.ShowWarningAsync(Properties.Resources.Error_InvalidProjectSelection, Properties.Resources.AnalyserHeader);
            return;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        var smaliFiles = Directory.EnumerateFiles(folder, "*.smali", options);
        if (!smaliFiles.Any())
        {
            await _dialogService.ShowWarningAsync(Properties.Resources.Error_InvalidSmaliProject, Properties.Resources.AnalyserHeader);
            return;
        }

        ProjectPath = folder;
    }

    [RelayCommand(CanExecute = nameof(CanRunAnalysis))]
    private async Task RunAnalysis()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            await _dialogService.ShowWarningAsync(Properties.Resources.SelectProjectFolder, Properties.Resources.AnalyserHeader);
            return;
        }

        SetConsoleLog("");
        IsRunning = true;

        try
        {
            var result = await _analyserService.AnalyseProjectAsync(ProjectPath, AppendLog);

            AppendLog("");
            AppendLog("=== Analysis Results ===");
            AppendLog("");

            // Root Check Results
            LogCategoryResult(result.ActiveCategories.Contains("root_check"), result.RootChecks, Properties.Resources.RootCheckFound, Properties.Resources.RootCheckNotFound);

            AppendLog("");

            // Emulator Check Results
            LogCategoryResult(result.ActiveCategories.Contains("emulator_check"), result.EmulatorChecks, Properties.Resources.EmulatorCheckFound, Properties.Resources.EmulatorCheckNotFound);

            AppendLog("");

            // Hardcoded Credentials Results
            LogCategoryResult(result.ActiveCategories.Contains("hardcoded_creds"), result.HardcodedCredentials, Properties.Resources.CredentialsFound, Properties.Resources.CredentialsNotFound);

            AppendLog("");

            // SQL Queries Results
            LogCategoryResult(result.ActiveCategories.Contains("sql_query"), result.SqlQueries, Properties.Resources.SqlQueriesFound, Properties.Resources.SqlQueriesNotFound);

            AppendLog("");

            // HTTP/HTTPS URLs Results
            LogCategoryResult(result.ActiveCategories.Contains("http_url"), result.HttpUrls, Properties.Resources.HttpUrlsFound, Properties.Resources.HttpUrlsNotFound);

            AppendLog("");
            AppendLog(Properties.Resources.AnalysisComplete);
            await _dialogService.ShowInfoAsync(Properties.Resources.AnalysisComplete);
        }
        catch (Exception ex)
        {
            AppendLog($"{Properties.Resources.AnalysisFailed}: {ex.Message}");
            await _dialogService.ShowErrorAsync(ex.Message);
        }
        finally
        {
            IsRunning = false;
            RunAnalysisCommand.NotifyCanExecuteChanged();
            SaveReportCommand.NotifyCanExecuteChanged();
        }
    }
    
    private void LogCategoryResult(bool isActive, List<Finding> findings, string foundMsg, string notFoundMsg)
    {
        if (isActive)
        {
            if (findings.Any())
            {
                AppendLog(foundMsg);
                AppendLog(Properties.Resources.FoundIn);
                foreach (var finding in findings)
                {
                    AppendLog($"  - {GetRelativePath(finding.FilePath, ProjectPath)} (Line {finding.LineNumber}): {finding.Context}");
                }
            }
            else
            {
                AppendLog(notFoundMsg);
            }
        }
        else
        {
            AppendLog("N/A");
        }
    }

    private bool CanRunAnalysis()
    {
        return !IsRunning && HasProject;
    }

    private void AppendLog(string message)
    {
        lock (_logLock)
        {
            var sanitized = message ?? string.Empty;
            if (_logLines.Count == 0 && ConsoleLog == Properties.Resources.WaitingForCommand)
            {
                _logLines.Clear();
                _logCharCount = 0;
            }

            _logLines.Enqueue(sanitized);
            _logCharCount += sanitized.Length;

            TrimLogIfNeeded();
            _logFlushPending = true;
        }
    }

    private void SetConsoleLog(string message)
    {
        lock (_logLock)
        {
            _logLines.Clear();
            _logCharCount = 0;

            var sanitized = message ?? string.Empty;
            _logLines.Enqueue(sanitized);
            _logCharCount = sanitized.Length;
            
            _logFlushPending = true;
        }
    }
    
    private void StartLogFlushLoop()
    {
        // Simple fire-and-forget loop
        Task.Run(async () =>
        {
            while (true)
            {
                if (_logFlushPending)
                {
                   FlushLog();
                }
                await Task.Delay(150);
            }
        });
    }

    private void FlushLog()
    {
        string logText;
        lock (_logLock)
        {
            logText = string.Join(Environment.NewLine, _logLines);
            _logFlushPending = false;
        }

        // Marshal to UI thread
        if (!_dispatcherService.CheckAccess())
        {
            _dispatcherService.InvokeAsync(() => ConsoleLog = logText);
        }
        else
        {
             ConsoleLog = logText;
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

    private string GetRelativePath(string fullPath, string basePath)
    {
        try
        {
            var baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }
        catch
        {
            return fullPath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveReport))]
    private async Task SaveReport()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            await _dialogService.ShowWarningAsync("No project loaded.", "Save Failed");
            return;
        }

        try
        {
            string folderName = new DirectoryInfo(ProjectPath).Name;
            string filePath = await _reportService.SaveReportAsync(ConsoleLog, folderName);
            await _dialogService.ShowInfoAsync($"Report saved successfully to:\n{filePath}", "Report Saved");
        }
        catch (Exception ex)
        {
             await _dialogService.ShowErrorAsync($"Failed to save report: {ex.Message}");
        }
    }

    private bool CanSaveReport()
    {
        // Use a less stringent check or ensure ConsoleLog is accessible. 
        // Note: accessing ConsoleLog here is on UI thread (CanExecute is usually called on UI thread).
        return !string.IsNullOrWhiteSpace(ConsoleLog) && ConsoleLog != Properties.Resources.WaitingForCommand;
    }
}
