using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Services;
using Properties = PulseAPK.Core.Properties;

namespace PulseAPK.Core.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogService _dialogService;
    private readonly IToolRepository _toolRepository;
    private readonly IToolDownloadService _toolDownloadService;
    private readonly LocalizationService _localizationService;

    [ObservableProperty]
    private string _apktoolPath;

    [ObservableProperty]
    private string _ubersignPath;

    [ObservableProperty]
    private bool _isDownloadingTools;
    
    [ObservableProperty]
    private LanguageItem _selectedLanguage;
    
    public List<LanguageItem> AvailableLanguages => _localizationService.AvailableLanguages;

    public SettingsViewModel(
        ISettingsService settingsService,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        IToolRepository toolRepository,
        IToolDownloadService toolDownloadService,
        LocalizationService localizationService)
    {
        _settingsService = settingsService;
        _filePickerService = filePickerService;
        _dialogService = dialogService;
        _toolRepository = toolRepository;
        _toolDownloadService = toolDownloadService;
        _localizationService = localizationService;

        _apktoolPath = _settingsService.Settings.ApktoolPath;
        _ubersignPath = _settingsService.Settings.UbersignPath;
        _selectedLanguage = _localizationService.CurrentLanguage;

        NormalizeManagedToolPathsIfMissing();
    }

    partial void OnApktoolPathChanged(string value)
    {
        _settingsService.Settings.ApktoolPath = value;
        _settingsService.Save();
    }

    partial void OnUbersignPathChanged(string value)
    {
        _settingsService.Settings.UbersignPath = value;
        _settingsService.Save();
    }
    
    partial void OnSelectedLanguageChanged(LanguageItem value)
    {
        if (value != null && value.Code != _localizationService.CurrentLanguage.Code)
        {
            _localizationService.SetLanguage(value.Code);
            _settingsService.Settings.SelectedLanguage = value.Code;
            _settingsService.Save();
        }
    }

    [RelayCommand]
    private async Task BrowseApktool()
    {
        var file = await _filePickerService.OpenFileAsync("Apktool files (*.jar;*.bat;*.cmd;*.exe)|*.jar;*.bat;*.cmd;*.exe|All Files (*.*)|*.*");
        if (file != null)
        {
            ApktoolPath = file;
        }
    }

    [RelayCommand]
    private async Task BrowseUbersign()
    {
        var file = await _filePickerService.OpenFileAsync("Jar/Exe Files (*.jar;*.exe)|*.jar;*.exe|All Files (*.*)|*.*");
        if (file != null)
        {
            UbersignPath = file;
        }
    }

    [RelayCommand]
    private async Task DownloadApktool()
    {
        await DownloadToolAsync(
            () => _toolDownloadService.DownloadApktoolAsync(),
            path => ApktoolPath = path,
            Properties.Resources.ResourceManager.GetString("DownloadApktoolButton") ?? Properties.Resources.DownloadApktool);
    }

    [RelayCommand]
    private async Task DownloadUbersigner()
    {
        await DownloadToolAsync(
            () => _toolDownloadService.DownloadUbersignerAsync(),
            path => UbersignPath = path,
            Properties.Resources.ResourceManager.GetString("DownloadUbersignerButton") ?? "Ubersigner");
    }

    private async Task DownloadToolAsync(
        Func<Task<ToolDownloadResult>> action,
        Action<string> applyPath,
        string toolDisplayName)
    {
        if (IsDownloadingTools)
        {
            return;
        }

        try
        {
            IsDownloadingTools = true;
            var result = await action();
            applyPath(result.Path);

            if (result.Downloaded)
            {
                await _dialogService.ShowInfoAsync($"{toolDisplayName} downloaded successfully.", Properties.Resources.SettingsHeader);
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync($"Failed to download {toolDisplayName}: {ex.Message}", Properties.Resources.SettingsHeader);
        }
        finally
        {
            IsDownloadingTools = false;
        }
    }

    private void NormalizeManagedToolPathsIfMissing()
    {
        var changed = false;

        if (IsManagedToolMissing(ApktoolPath))
        {
            ApktoolPath = string.Empty;
            changed = true;
        }

        if (IsManagedToolMissing(UbersignPath))
        {
            UbersignPath = string.Empty;
            changed = true;
        }

        if (changed)
        {
            _settingsService.Save();
        }
    }

    private bool IsManagedToolMissing(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return false;
        }

        if (File.Exists(configuredPath))
        {
            return false;
        }

        var normalizedToolFolder = Path.GetFullPath(_toolRepository.ToolsDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedConfiguredPath = Path.GetFullPath(configuredPath);

        return normalizedConfiguredPath.StartsWith(normalizedToolFolder, StringComparison.OrdinalIgnoreCase);
    }
}
