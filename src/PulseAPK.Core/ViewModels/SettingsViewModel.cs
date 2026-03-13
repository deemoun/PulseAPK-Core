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
    private readonly LocalizationService _localizationService;

    [ObservableProperty]
    private string _apktoolPath;

    [ObservableProperty]
    private string _ubersignPath;
    
    [ObservableProperty]
    private LanguageItem _selectedLanguage;
    
    public List<LanguageItem> AvailableLanguages => _localizationService.AvailableLanguages;

    public SettingsViewModel(
        ISettingsService settingsService, 
        IFilePickerService filePickerService,
        LocalizationService localizationService)
    {
        _settingsService = settingsService;
        _filePickerService = filePickerService;
        _localizationService = localizationService;

        _apktoolPath = _settingsService.Settings.ApktoolPath;
        _ubersignPath = _settingsService.Settings.UbersignPath;
        _selectedLanguage = _localizationService.CurrentLanguage;
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
}
