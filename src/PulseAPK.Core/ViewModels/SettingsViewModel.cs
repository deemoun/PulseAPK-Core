using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Services;
using Properties = PulseAPK.Core.Properties;

namespace PulseAPK.Core.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogService _dialogService;
    private readonly IToolRepository _toolRepository;
    private readonly IToolDownloadService _toolDownloadService;
    private readonly LocalizationService _localizationService;
    private readonly IThemeService _themeService;
    private bool _disposed;

    [ObservableProperty]
    private string _apktoolPath;

    [ObservableProperty]
    private string _ubersignPath;

    [ObservableProperty]
    private bool _isDownloadingTools;
    
    [ObservableProperty]
    private LanguageItem _selectedLanguage;

    [ObservableProperty]
    private ThemeModeItem _selectedThemeMode = null!;

    [ObservableProperty]
    private BackgroundColorItem _selectedBackgroundColor = null!;

    [ObservableProperty]
    private double _objectScale = 1.0;
    
    public List<LanguageItem> AvailableLanguages => _localizationService.AvailableLanguages;
    private List<ThemeModeItem> _availableThemeModes = [];
    private List<BackgroundColorItem> _availableBackgroundColors = [];

    public List<ThemeModeItem> AvailableThemeModes
    {
        get => _availableThemeModes;
        private set => SetProperty(ref _availableThemeModes, value);
    }

    public List<BackgroundColorItem> AvailableBackgroundColors
    {
        get => _availableBackgroundColors;
        private set => SetProperty(ref _availableBackgroundColors, value);
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
        _objectScale = ClampObjectScale(_settingsService.Settings.ObjectScale);

        RefreshThemeModes(_settingsService.Settings.ThemeMode);
        RefreshBackgroundColors(_settingsService.Settings.BackgroundColor);
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
        ApplyVisualPreferences();
    }

    partial void OnSelectedBackgroundColorChanged(BackgroundColorItem value)
    {
        if (value is null)
        {
            return;
        }

        _settingsService.Settings.BackgroundColor = value.ColorValue;
        _settingsService.Save();
        ApplyVisualPreferences();
    }

    partial void OnObjectScaleChanged(double value)
    {
        var normalized = ClampObjectScale(value);
        if (Math.Abs(normalized - value) > 0.001)
        {
            ObjectScale = normalized;
            return;
        }

        _settingsService.Settings.ObjectScale = normalized;
        _settingsService.Save();
        ApplyVisualPreferences();
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
            Properties.Resources.ResourceManager.GetString("ToolNameApktool") ?? "Apktool");
    }

    [RelayCommand]
    private async Task DownloadUbersigner()
    {
        await DownloadToolAsync(
            () => _toolDownloadService.DownloadUbersignerAsync(),
            path => UbersignPath = path,
            Properties.Resources.ResourceManager.GetString("ToolNameUbersigner") ?? "Ubersigner");
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
                var successTemplate = Properties.Resources.ResourceManager.GetString("ToolDownloadedSuccessfullyMessage")
                    ?? "{0} downloaded successfully.";
                await _dialogService.ShowInfoAsync(string.Format(successTemplate, toolDisplayName), Properties.Resources.SettingsHeader);
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
            RefreshBackgroundColors(SelectedBackgroundColor?.ColorValue);
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

    private void RefreshBackgroundColors(string? selectedColor)
    {
        AvailableBackgroundColors =
        [
            new BackgroundColorItem("#121212", "Charcoal"),
            new BackgroundColorItem("#1E3A8A", "Blue"),
            new BackgroundColorItem("#14532D", "Green"),
            new BackgroundColorItem("#581C87", "Purple"),
            new BackgroundColorItem("#7F1D1D", "Red"),
            new BackgroundColorItem("#78350F", "Amber"),
            new BackgroundColorItem("#134E4A", "Teal"),
            new BackgroundColorItem("#4A044E", "Magenta"),
            new BackgroundColorItem("#111827", "Slate"),
            new BackgroundColorItem("#701A75", "Fuchsia")
        ];

        SelectedBackgroundColor = AvailableBackgroundColors.FirstOrDefault(color =>
                                      string.Equals(color.ColorValue, selectedColor, StringComparison.OrdinalIgnoreCase))
                                  ?? AvailableBackgroundColors[0];
    }

    private void ApplyVisualPreferences()
    {
        _themeService.ApplyVisualPreferences(SelectedThemeMode?.Key, SelectedBackgroundColor?.ColorValue, ObjectScale);
    }

    private static double ClampObjectScale(double value)
    {
        return Math.Clamp(value, 0.75, 1.5);
    }

    private ThemeModeItem ResolveThemeMode(string? themeMode)
    {
        return AvailableThemeModes.FirstOrDefault(mode =>
                   string.Equals(mode.Key, themeMode, StringComparison.OrdinalIgnoreCase))
               ?? AvailableThemeModes[0];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _localizationService.PropertyChanged -= OnLocalizationChanged;
        _disposed = true;
    }
}

public sealed record ThemeModeItem(string Key, string Name);

public sealed record BackgroundColorItem(string ColorValue, string Name);
