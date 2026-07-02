using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core;
using PulseAPK.Core.Services;
using Properties = PulseAPK.Core.Properties;

namespace PulseAPK.Core.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private const string SupportWorkUrl = "https://yarygintech.com/support-work/";

    private readonly LocalizationService _localizationService;
    private readonly ISystemService _systemService;
    private readonly ISettingsService _settingsService;
    private bool _disposed;

    [ObservableProperty]
    private object _currentView = null!;

    [ObservableProperty]
    private string _windowTitle = Properties.Resources.AppTitle;

    [ObservableProperty]
    private string _selectedMenu = "Decompile";

    public string MenuDecompileLabel => _localizationService["MenuDecompile"];
    public string MenuBuildLabel => _localizationService["MenuBuild"];
    public string MenuPatchLabel => _localizationService["MenuPatch"];
    public string MenuAnalyserLabel => _localizationService["MenuAnalyser"];
    public string MenuDeviceToolsLabel => _localizationService["MenuDeviceTools"];
    public string MenuSettingsLabel => _localizationService["MenuSettings"];
    public string MenuAboutLabel => _localizationService["MenuAbout"];

    public bool IsSupportWorkVisible => true;

    public bool IsDecompileSelected => SelectedMenu == "Decompile";
    public bool IsBuildSelected => SelectedMenu == "Build";
    public bool IsPatchSelected => SelectedMenu == "Patch";
    public bool IsAnalyserSelected => SelectedMenu == "Analyser";
    public bool IsDeviceToolsSelected => SelectedMenu == "DeviceTools";
    public bool IsSettingsSelected => SelectedMenu == "Settings";
    public bool IsAboutSelected => SelectedMenu == "About";

    public bool IsDecompileEnabled => FeatureFlags.Decompile;
    public bool IsBuildEnabled => FeatureFlags.BuildApk;
    public bool IsPatchEnabled => FeatureFlags.PatchApk;
    public bool IsAnalyserEnabled => FeatureFlags.ApkAnalyser;
    public bool IsDeviceToolsEnabled => FeatureFlags.DeviceTools && _settingsService.Settings.IsDeviceToolsEnabled;
    public bool IsSettingsEnabled => FeatureFlags.Settings;
    public bool IsAboutEnabled => FeatureFlags.About;

    public MainViewModel(
        IServiceProvider serviceProvider,
        LocalizationService localizationService,
        ISystemService systemService,
        ISettingsService settingsService)
    {
        _serviceProvider = serviceProvider;
        _localizationService = localizationService;
        _systemService = systemService;
        _settingsService = settingsService;
        WindowTitle = _localizationService["AppTitle"];
        _localizationService.PropertyChanged += HandleLocalizationChanged;
        _settingsService.SettingsChanged += HandleSettingsChanged;
        SetInitialView();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToDecompile))]
    private void NavigateToDecompile()
    {
        if (!CanNavigateToDecompile()) return;
        SetCurrentView(Resolve<DecompileViewModel>());
        SelectedMenu = "Decompile";
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToSettings))]
    private void NavigateToSettings()
    {
        if (!CanNavigateToSettings()) return;
        SetCurrentView(Resolve<SettingsViewModel>());
        SelectedMenu = "Settings";
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToBuild))]
    private void NavigateToBuild()
    {
        if (!CanNavigateToBuild()) return;
        SetCurrentView(Resolve<BuildViewModel>());
        SelectedMenu = "Build";
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToPatch))]
    private void NavigateToPatch()
    {
        if (!CanNavigateToPatch()) return;
        SetCurrentView(Resolve<PatchViewModel>());
        SelectedMenu = "Patch";
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToAnalyser))]
    private void NavigateToAnalyser()
    {
        if (!CanNavigateToAnalyser()) return;
        SetCurrentView(Resolve<AnalyserViewModel>());
        SelectedMenu = "Analyser";
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToDeviceTools))]
    private void NavigateToDeviceTools()
    {
        if (!CanNavigateToDeviceTools()) return;
        var viewModel = Resolve<DeviceToolsViewModel>();
        SetCurrentView(viewModel);
        SelectedMenu = "DeviceTools";
        _ = viewModel.RefreshDevicesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToAbout))]
    private void NavigateToAbout()
    {
        if (!CanNavigateToAbout()) return;
        SetCurrentView(Resolve<AboutViewModel>());
        SelectedMenu = "About";
    }

    [RelayCommand]
    private void OpenSupportWork()
    {
        _systemService.OpenUrl(SupportWorkUrl);
    }

    partial void OnSelectedMenuChanged(string value)
    {
        OnPropertyChanged(nameof(IsDecompileSelected));
        OnPropertyChanged(nameof(IsBuildSelected));
        OnPropertyChanged(nameof(IsPatchSelected));
        OnPropertyChanged(nameof(IsAnalyserSelected));
        OnPropertyChanged(nameof(IsDeviceToolsSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
        OnPropertyChanged(nameof(IsAboutSelected));
    }

    private bool CanNavigateToDecompile() => FeatureFlags.Decompile;
    private bool CanNavigateToBuild() => FeatureFlags.BuildApk;
    private bool CanNavigateToPatch() => FeatureFlags.PatchApk;
    private bool CanNavigateToAnalyser() => FeatureFlags.ApkAnalyser;
    private bool CanNavigateToDeviceTools() => IsDeviceToolsEnabled;
    private bool CanNavigateToSettings() => FeatureFlags.Settings;
    private bool CanNavigateToAbout() => FeatureFlags.About;


    private void HandleSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsDeviceToolsEnabled));
        NavigateToDeviceToolsCommand.NotifyCanExecuteChanged();

        if (IsDeviceToolsSelected && !CanNavigateToDeviceTools())
        {
            NavigateToFirstAvailableEnabledTab();
        }
    }

    private void NavigateToFirstAvailableEnabledTab()
    {
        if (CanNavigateToDecompile())
        {
            NavigateToDecompile();
        }
        else if (CanNavigateToBuild())
        {
            NavigateToBuild();
        }
        else if (CanNavigateToPatch())
        {
            NavigateToPatch();
        }
        else if (CanNavigateToAnalyser())
        {
            NavigateToAnalyser();
        }
        else if (CanNavigateToSettings())
        {
            NavigateToSettings();
        }
        else if (CanNavigateToAbout())
        {
            NavigateToAbout();
        }
        else
        {
            CurrentView = "No features are enabled.";
            SelectedMenu = string.Empty;
        }
    }

    private void SetInitialView()
    {
        if (CanNavigateToDecompile())
        {
            NavigateToDecompile();
        }
        else if (CanNavigateToBuild())
        {
            NavigateToBuild();
        }
        else if (CanNavigateToPatch())
        {
            NavigateToPatch();
        }
        else if (CanNavigateToAnalyser())
        {
            NavigateToAnalyser();
        }
        else if (CanNavigateToDeviceTools())
        {
            NavigateToDeviceTools();
        }
        else if (CanNavigateToSettings())
        {
            NavigateToSettings();
        }
        else if (CanNavigateToAbout())
        {
            NavigateToAbout();
        }
        else
        {
            CurrentView = "No features are enabled.";
            SelectedMenu = string.Empty;
        }
    }

    private void SetCurrentView(object nextView)
    {
        if (ReferenceEquals(CurrentView, nextView))
        {
            return;
        }

        if (CurrentView is IDisposable disposable)
        {
            disposable.Dispose();
        }

        CurrentView = nextView;
    }

    private T Resolve<T>() where T : notnull
    {
        var service = _serviceProvider.GetService(typeof(T));
        if (service == null)
            throw new InvalidOperationException($"Could not resolve service of type {typeof(T).Name}");
        return (T)service;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _localizationService.PropertyChanged -= HandleLocalizationChanged;
        _settingsService.SettingsChanged -= HandleSettingsChanged;
        _disposed = true;
    }

    private void HandleLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "Item[]")
        {
            return;
        }

        WindowTitle = _localizationService["AppTitle"];
        OnPropertyChanged(nameof(MenuDecompileLabel));
        OnPropertyChanged(nameof(MenuBuildLabel));
        OnPropertyChanged(nameof(MenuPatchLabel));
        OnPropertyChanged(nameof(MenuAnalyserLabel));
        OnPropertyChanged(nameof(MenuDeviceToolsLabel));
        OnPropertyChanged(nameof(MenuSettingsLabel));
        OnPropertyChanged(nameof(MenuAboutLabel));
    }
}
