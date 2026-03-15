using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Services;
using Properties = PulseAPK.Core.Properties;

namespace PulseAPK.Core.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LocalizationService _localizationService;

    [ObservableProperty]
    private object _currentView;

    [ObservableProperty]
    private string _windowTitle = Properties.Resources.AppTitle;

    [ObservableProperty]
    private string _selectedMenu = "Decompile";

    public string MenuDecompileLabel => _localizationService["MenuDecompile"];
    public string MenuBuildLabel => _localizationService["MenuBuild"];
    public string MenuPatchLabel => "Patch APK";
    public string MenuAnalyserLabel => _localizationService["MenuAnalyser"];
    public string MenuSettingsLabel => _localizationService["MenuSettings"];
    public string MenuAboutLabel => _localizationService["MenuAbout"];

    public MainViewModel(IServiceProvider serviceProvider, LocalizationService localizationService)
    {
        _serviceProvider = serviceProvider;
        _localizationService = localizationService;
        WindowTitle = _localizationService["AppTitle"];
        _localizationService.PropertyChanged += HandleLocalizationChanged;
        // Initial view
        SetCurrentView(Resolve<DecompileViewModel>());
    }

    [RelayCommand]
    private void NavigateToDecompile()
    {
        SetCurrentView(Resolve<DecompileViewModel>());
        SelectedMenu = "Decompile";
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        SetCurrentView(Resolve<SettingsViewModel>());
        SelectedMenu = "Settings";
    }

    [RelayCommand]
    private void NavigateToBuild()
    {
        SetCurrentView(Resolve<BuildViewModel>());
        SelectedMenu = "Build";
    }

    [RelayCommand]
    private void NavigateToPatch()
    {
        SetCurrentView(Resolve<PatchViewModel>());
        SelectedMenu = "Patch";
    }

    [RelayCommand]
    private void NavigateToAnalyser()
    {
        SetCurrentView(Resolve<AnalyserViewModel>());
        SelectedMenu = "Analyser";
    }

    [RelayCommand]
    private void NavigateToAbout()
    {
        SetCurrentView(Resolve<AboutViewModel>());
        SelectedMenu = "About";
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

    private void HandleLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "Item[]")
        {
            return;
        }

        WindowTitle = _localizationService["AppTitle"];
        OnPropertyChanged(nameof(MenuDecompileLabel));
        OnPropertyChanged(nameof(MenuBuildLabel));
        OnPropertyChanged(nameof(MenuAnalyserLabel));
        OnPropertyChanged(nameof(MenuSettingsLabel));
        OnPropertyChanged(nameof(MenuAboutLabel));
    }
}
