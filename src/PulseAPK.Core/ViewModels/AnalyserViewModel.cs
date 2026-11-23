using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PulseAPK.Core.Services;
using PulseAPK.Core.Abstractions;
using Properties = PulseAPK.Core.Properties;

namespace PulseAPK.Core.ViewModels;

public sealed record SearchFileGroup(string Path, ObservableCollection<Finding> Matches);

public partial class AnalyserViewModel : ObservableObject
{
    private const int MaxLogCharacters = 900_000;
    private const int LogTrimTargetCharacters = 850_000;
    
    private readonly Queue<string> _logLines = new Queue<string>();
    private readonly object _logLock = new object();
    private int _logCharCount;
    private bool _logFlushPending;
    
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
    [NotifyCanExecuteChangedFor(nameof(SearchProjectCommand))]
    private bool _isRunning;

    private Finding[] _all = [];
    [ObservableProperty] private Finding[] _visibleFindings = [];
    [ObservableProperty] private string[] _categories = ["All categories"];
    [ObservableProperty] private string? _category = "All categories";
    [ObservableProperty] private string _query = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SelectedFindingDescription))] private Finding? _selectedFinding;
    public string SelectedFindingDescription => SelectedFinding?.Description ?? string.Empty;
    [ObservableProperty] private string _preview = "";
    [ObservableProperty] private string _findingCount = "";
    [ObservableProperty] private string _scanStatus = "";
    private CancellationTokenSource? _previewRead;
    private CancellationTokenSource? _searchPreviewRead;
    [ObservableProperty] private string _searchPreview = "";

    [ObservableProperty] private string _dexText = "";
    [ObservableProperty] private string _dexSummary = "";

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task InspectDex(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(DexText)) { DexSummary = "Enter text to find in DEX strings."; return; }
        var query = DexText;
        var apk = await _filePickerService.OpenFileAsync(Properties.Resources.FileFilter_Apk);
        if (apk == null) return;
        DexSummary = "Reading DEX string tables…";
        try
        {
            var entries = await ReadOnlyDexSearch.InspectAsync(apk, query, token);
            DexSummary = entries.Count == 0 ? "No DEX entries found." :
                string.Join(Environment.NewLine, entries.Select(e => e.Error == null
                    ? $"{e.Entry}: {e.Matches} matching strings" : $"{e.Entry}: unreadable — {e.Error}"));
        }
        catch (OperationCanceledException) { DexSummary = "Inspection cancelled."; }
        catch (Exception ex) { DexSummary = $"Could not inspect APK: {ex.Message}"; }
    }

    private AnalysisResult? _lastScan;
    public IReadOnlyList<string> SearchTypes { get; } = [.. SmaliAnalyserService.SearchTypes, SmaliAnalyserService.Extensionless, "Custom extension"];
    public bool UsesCustomExtension => FileType == "Custom extension";
    [ObservableProperty] private string _customExtension = "";
    [ObservableProperty] private string _searchCoverage = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SearchProjectCommand))] private string _searchText = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UsesCustomExtension))] private string _fileType = SmaliAnalyserService.SearchTypes[0];
    [ObservableProperty] private bool _matchCase;
    [ObservableProperty] private bool _includeLibraries;
    [ObservableProperty] private bool _includeLibrariesInAnalysis;
    public ObservableCollection<SearchFileGroup> SearchFiles { get; } = [];
    [ObservableProperty] private string _searchStatus = "";
    [ObservableProperty] private object? _searchSelection;
    [ObservableProperty] private string _ruleInfo = "";

    partial void OnSearchSelectionChanged(object? value)
    {
        _searchPreviewRead?.Cancel();
        SearchPreview = "";
        if (value is Finding match) _ = LoadPreview(match, true);
    }

    private bool CanSearchProject() => CanRunAnalysis() && !string.IsNullOrWhiteSpace(SearchText);

    [RelayCommand(CanExecute = nameof(CanSearchProject), IncludeCancelCommand = true)]
    private async Task SearchProject(CancellationToken token)
    {
        SearchFiles.Clear();
        SearchCoverage = "";
        SearchSelection = null;
        SearchStatus = "Searching…";
        IsRunning = true;
        try
        {
            string extension = FileType;
            if (UsesCustomExtension)
            {
                string value = CustomExtension.Trim().TrimStart('.');
                if (value.Length == 0 || !value.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
                    throw new ArgumentException("Enter one extension, for example .toml.");
                extension = "." + value;
            }
            var options = new ProjectSearch(SearchText, MatchCase, extension, IncludeLibraries);
            var groups = new Dictionary<string, SearchFileGroup>(StringComparer.Ordinal);
            int displayed = 0;
            var result = await _analyserService.SearchAsync(ProjectPath, options, token, batch =>
                _dispatcherService.InvokeAsync(() =>
                {
                    foreach (var match in batch)
                    {
                        if (!groups.TryGetValue(match.RelativePath, out var group))
                        {
                            group = new SearchFileGroup(match.RelativePath, []);
                            groups.Add(match.RelativePath, group);
                            int index = 0;
                            while (index < SearchFiles.Count && string.CompareOrdinal(SearchFiles[index].Path, group.Path) < 0) index++;
                            SearchFiles.Insert(index, group);
                        }
                        group.Matches.Add(match);
                    }
                    displayed += batch.Length;
                    SearchStatus = $"Searching… Showing {displayed} matches (limit {SmaliAnalyserService.SearchLimit}).";
                }).GetAwaiter().GetResult());
            string scope = extension == SearchTypes[0] ? string.Join(", ", SmaliAnalyserService.SearchTypes.Skip(1)) : extension;
            string exclusions = options.IncludeLibraries ? "none" : string.Join(", ", result.LibraryPaths);
            SearchCoverage = $"File types: {scope} · Excluded library paths: {exclusions}" +
                (result.RuleWarnings.Count == 0 ? "" : Environment.NewLine + string.Join(Environment.NewLine, result.RuleWarnings));
            SearchStatus = $"Showing {displayed} of {result.MatchingLines} matching lines · {result.Status} · {result.Coverage}";
        }
        catch (Exception ex) { SearchStatus = $"Search failed: {ex.Message}"; }
        finally { IsRunning = false; }
    }

    partial void OnQueryChanged(string value) => Filter();
    partial void OnCategoryChanged(string? value) => Filter();
    private void Filter()
    {
        VisibleFindings = _all.Where(f => (Category == null || Category == "All categories" || Category == f.Category)
            && (f.RelativePath.Contains(Query, StringComparison.OrdinalIgnoreCase)
                || f.Context.Contains(Query, StringComparison.OrdinalIgnoreCase))).ToArray();
        FindingCount = _lastScan?.Scanned == 0 ? "No files scanned" : _all.Length == 0 ? "No findings" : $"{VisibleFindings.Length} of {_all.Length} findings";
        if (SelectedFinding != null && !VisibleFindings.Contains(SelectedFinding)) SelectedFinding = null;
    }

    partial void OnSelectedFindingChanged(Finding? value) => ShowPreview(value);

    private void ShowPreview(Finding? value)
    {
        _previewRead?.Cancel();
        Preview = "";
        if (value != null) _ = LoadPreview(value);
    }

    private async Task LoadPreview(Finding finding, bool search = false)
    {
        using var read = new CancellationTokenSource();
        if (search) _searchPreviewRead = read; else _previewRead = read;
        try
        {
            var text = await Task.Run(() => ReadPreview(finding, read.Token));
            if (read.IsCancellationRequested) return;
            if (search) SearchPreview = text; else Preview = text;
        }
        catch (OperationCanceledException) when (read.IsCancellationRequested) { }
        finally
        {
            if (_previewRead == read) _previewRead = null;
            if (_searchPreviewRead == read) _searchPreviewRead = null;
        }
    }

    internal static string ReadPreview(Finding finding, CancellationToken token)
    {
        try
        {
            int number = 0;
            var lines = new List<string>();
            foreach (var line in File.ReadLines(finding.FilePath))
            {
                token.ThrowIfCancellationRequested();
                number++;
                if (number < finding.LineNumber - 5) continue;
                if (number == finding.LineNumber && line != finding.OriginalLine) return "The matched line has changed.";
                lines.Add($"{(number == finding.LineNumber ? "▶" : " ")} {number,5}  {line}");
                if (number >= finding.LineNumber + 5) break;
            }
            return number < finding.LineNumber ? "The matched line is missing." : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Cannot read source: {ex.Message}";
        }
    }

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
        

    }

    partial void OnProjectPathChanged(string value)
    {
        _lastScan = null;
        SearchSelection = null;
        SearchStatus = RuleInfo = "";
        SearchFiles.Clear();
        SearchCoverage = "";
        SearchProjectCommand.NotifyCanExecuteChanged();
        _all = [];
        Filter();
        ScanStatus = "";
        SetConsoleLog(Properties.Resources.WaitingForCommand);
        SaveReportCommand.NotifyCanExecuteChanged();
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

        ProjectPath = folder;
    }

    [RelayCommand(CanExecute = nameof(CanRunAnalysis), IncludeCancelCommand = true)]
    private async Task RunAnalysis(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            await _dialogService.ShowWarningAsync(Properties.Resources.SelectProjectFolder, Properties.Resources.AnalyserHeader);
            return;
        }

        _lastScan = null;
        RuleInfo = "";
        var root = ProjectPath;
        _all = [];
        Filter();
        ScanStatus = "Scanning…";
        SetConsoleLog("");
        IsRunning = true;

        try
        {
            var result = await _analyserService.AnalyseProjectAsync(root, AppendLog, cancellationToken, progress: coverage =>
                _dispatcherService.InvokeAsync(() => ScanStatus = coverage).GetAwaiter().GetResult(), includeLibraries: IncludeLibrariesInAnalysis);

            AppendLog("");
            AppendLog("=== Analysis Results ===");
            AppendLog("");

            // Log counts only; full source context belongs in the selectable findings view.
            var counts = result.Findings.GroupBy(f => f.Category, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var category in result.ActiveCategories.Order(StringComparer.Ordinal))
                AppendLog($"{category}: {counts.GetValueOrDefault(category)} observed");
            AppendLog("");
            _lastScan = result;
            RuleInfo = "Excluded library paths: " + string.Join(", ", result.LibraryPaths) + Environment.NewLine
                + string.Join(Environment.NewLine, result.Rules.Select(rule => $"{rule.Category} ({rule.Severity}) — {rule.Description}").Concat(result.RuleWarnings));
            _all = result.Findings.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ThenBy(f => f.LineNumber)
                .ThenBy(f => f.Category, StringComparer.Ordinal).ToArray();
            Categories = ["All categories", .. result.ActiveCategories.Order(StringComparer.Ordinal)];
            Category = "All categories";
            Filter();
            ScanStatus = $"{result.Status} · {result.Coverage}";
            if (!result.Cancelled && result.Scanned == 0 && result.Excluded > 0 && result.Unreadable == 0 && result.Inaccessible == 0)
            {
                ScanStatus = $"All {result.Excluded} Smali files matched library exclusions. Select ‘Include library paths’ and run analysis again.";
            }
            AppendLog(ScanStatus);
        }
        catch (Exception ex)
        {
            ScanStatus = Properties.Resources.AnalysisFailed;
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
            ScheduleLogFlush();
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
            
            ScheduleLogFlush();
        }
    }
    
    // Called under _logLock. Coalesce writes into one queued UI update.
    private void ScheduleLogFlush()
    {
        if (_logFlushPending) return;
        _logFlushPending = true;
        _ = FlushLogAsync();
    }

    private async Task FlushLogAsync()
    {
        await Task.Delay(150).ConfigureAwait(false);
        await _dispatcherService.InvokeAsync(() =>
        {
            string text;
            lock (_logLock)
            {
                text = string.Join(Environment.NewLine, _logLines);
                _logFlushPending = false;
            }
            ConsoleLog = text;
        });
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
            if (_lastScan == null) return;
            string filePath = await _reportService.SaveAnalysisAsync(_lastScan, folderName);
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
        return !IsRunning && _lastScan != null;
    }
}
