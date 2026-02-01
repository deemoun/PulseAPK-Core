using System.ComponentModel;
using System.Globalization;
using System.Resources;
using PulseAPK.Core.Properties;

namespace PulseAPK.Core.Services;

public class LocalizationService : INotifyPropertyChanged
{
    private static readonly LocalizationService _instance = new();
    public static LocalizationService Instance => _instance;

    private readonly ResourceManager _resourceManager = Resources.ResourceManager;
    private CultureInfo _currentCulture = Thread.CurrentThread.CurrentUICulture;
    private ISettingsService? _settingsService;

    public event PropertyChangedEventHandler? PropertyChanged;

    public List<LanguageItem> AvailableLanguages { get; } = new()
    {
        new LanguageItem("English", "en-US"),
        new LanguageItem("Русский", "ru-RU"),
        new LanguageItem("Українська", "uk-UA"),
        new LanguageItem("Español", "es-ES"),
        new LanguageItem("中文", "zh-CN"),
        new LanguageItem("Deutsch", "de-DE"),
        new LanguageItem("Français", "fr-FR"),
        new LanguageItem("Português", "pt-BR"),
        new LanguageItem("العربية", "ar-SA")
    };

    public LanguageItem CurrentLanguage => AvailableLanguages.FirstOrDefault(l => l.Code == _currentCulture.Name) ?? AvailableLanguages.First();

    public void Initialize(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        
        if (!string.IsNullOrEmpty(_settingsService.Settings.SelectedLanguage))
        {
            try
            {
                var savedCulture = new CultureInfo(_settingsService.Settings.SelectedLanguage);
                CurrentCulture = savedCulture;
            }
            catch
            {
                // Fallback
            }
        }
    }

    public string this[string key]
    {
        get
        {
            var result = _resourceManager.GetString(key, _currentCulture);
            
            if (result == null && !_currentCulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                result = _resourceManager.GetString(key, new CultureInfo("en-US"));
            }
            
            return result ?? $"#{key}#";
        }
    }

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (_currentCulture != value)
            {
                _currentCulture = value;
                Thread.CurrentThread.CurrentUICulture = value;
                Thread.CurrentThread.CurrentCulture = value;
                Resources.Culture = value;
                
                if (_settingsService != null)
                {
                    _settingsService.Settings.SelectedLanguage = value.Name;
                    _settingsService.Save();
                }
                
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
            }
        }
    }

    public void SetLanguage(string languageCode)
    {
        try 
        {
            CurrentCulture = new CultureInfo(languageCode);
        }
        catch
        {
            CurrentCulture = new CultureInfo("en-US");
        }
    }
}

public record LanguageItem(string Name, string Code);
