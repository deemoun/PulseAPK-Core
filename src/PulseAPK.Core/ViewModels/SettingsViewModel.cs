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
    private readonly IThemeService _themeService;

    [ObservableProperty]
    private string _apktoolPath;

    [ObservableProperty]
    private string _ubersignPath;

    [ObservableProperty]
    private bool _isDownloadingTools;
    
    [ObservableProperty]
    private LanguageItem _selectedLanguage;

    [ObservableProperty]
    private ThemeModeItem _selectedThemeMode;
    
    public List<LanguageItem> AvailableLanguages => _localizationService.AvailableLanguages;
    private List<ThemeModeItem> _availableThemeModes = [];

    public List<ThemeModeItem> AvailableThemeModes
    {
        get => _availableThemeModes;
        private set => SetProperty(ref _availableThemeModes, value);
    }

    public SettingsViewModel(
        ISettingsService settingsService,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        IToolRepository toolRepository,
        IToolDownloadService toolDownloadService,
        LocalizationService localizationService,
        IThemeService themeService)
    {
        _settingsService = settingsService;
        _filePickerService = filePickerService;
        _dialogService = dialogService;
        _toolRepository = toolRepository;
        _toolDownloadService = toolDownloadService;
        _localizationService = localizationService;
        _themeService = themeService;

        _apktoolPath = _settingsService.Settings.ApktoolPath;
        _ubersignPath = _settingsService.Settings.UbersignPath;
        _selectedLanguage = _localizationService.CurrentLanguage;

        RefreshThemeModes(_settingsService.Settings.ThemeMode);
        _localizationService.PropertyChanged += OnLocalizationChanged;

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

    partial void OnSelectedThemeModeChanged(ThemeModeItem value)
    {
        if (value is null)
        {
            return;
        }

        _settingsService.Settings.ThemeMode = value.Key;
        _settingsService.Save();
        _themeService.ApplyTheme(value.Key);
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


    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Item[]" || e.PropertyName == string.Empty)
        {
            RefreshThemeModes(SelectedThemeMode?.Key);
        }
    }

    private void RefreshThemeModes(string? selectedThemeKey)
    {
        AvailableThemeModes =
        [
            new ThemeModeItem("dark_mode", _localizationService["ThemeModeDark"]),
            new ThemeModeItem("light_mode", _localizationService["ThemeModeLight"])
        ];

        SelectedThemeMode = ResolveThemeMode(selectedThemeKey);
    }

    private ThemeModeItem ResolveThemeMode(string? themeMode)
    {
        return AvailableThemeModes.FirstOrDefault(mode =>
                   string.Equals(mode.Key, themeMode, StringComparison.OrdinalIgnoreCase))
               ?? AvailableThemeModes[0];
    }
}

public sealed record ThemeModeItem(string Key, string Name);
