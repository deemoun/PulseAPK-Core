using System;
using System.IO;
using System.Text.Json;

namespace PulseAPK.Core.Services
{
    public class AppSettings
    {
        public string ApktoolPath { get; set; } = "apktool.jar";
        public string UbersignPath { get; set; } = string.Empty;
        public string SelectedLanguage { get; set; } = "en-US";
        public string ThemeMode { get; set; } = "dark_mode";
    }

    public interface ISettingsService
    {
        AppSettings Settings { get; }
        string SettingsDirectory { get; }
        void Save();
    }

    public class SettingsService : ISettingsService
    {
        private const string SettingsFileName = "settings.json";
        private const string AppName = "PulseAPK";

        private readonly string _settingsFilePath;
        private readonly string _legacySettingsFilePath;

        public AppSettings Settings { get; private set; }
        public string SettingsDirectory { get; }

        public SettingsService()
        {
            var baseDirectory = AppContext.BaseDirectory;
            var settingsFolder = ResolveSettingsFolder(baseDirectory);
            SettingsDirectory = settingsFolder;
            _settingsFilePath = Path.Combine(settingsFolder, SettingsFileName);
            _legacySettingsFilePath = Path.Combine(baseDirectory, SettingsFileName);
            Settings = LoadSettings();
        }

        private static string ResolveSettingsFolder(string baseDirectory)
        {
            var appDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(appDataDirectory))
            {
                appDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            if (string.IsNullOrWhiteSpace(appDataDirectory))
            {
                appDataDirectory = Environment.CurrentDirectory;
            }

            var settingsDirectory = Path.Combine(appDataDirectory, AppName);
            Directory.CreateDirectory(settingsDirectory);
            return settingsDirectory;
        }

        private AppSettings LoadSettings()
        {
            if (TryLoadSettings(_settingsFilePath, out var settings))
            {
                return settings;
            }

            // If we moved from base directory to app-data fallback, keep reading older file once.
            if (!string.Equals(_legacySettingsFilePath, _settingsFilePath, StringComparison.OrdinalIgnoreCase)
                && TryLoadSettings(_legacySettingsFilePath, out settings))
            {
                Save(settings);
                return settings;
            }

            return new AppSettings();
        }

        private static bool TryLoadSettings(string settingsPath, out AppSettings settings)
        {
            settings = null!;
            if (!File.Exists(settingsPath))
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(settingsPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Save()
        {
            Save(Settings);
        }

        private void Save(AppSettings settings)
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
    }
}
