using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PulseAPK.Avalonia.Services;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Services;
using PulseAPK.Core.Services.Patching;
using PulseAPK.Core.ViewModels;
using System;

namespace PulseAPK.Avalonia;

public partial class App : Application
{
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Initialize settings/localization
        var settingsService = Services.GetRequiredService<ISettingsService>();
        LocalizationService.Instance.Initialize(settingsService);
        var themeService = Services.GetRequiredService<IThemeService>();
        themeService.ApplyTheme(settingsService.Settings.ThemeMode);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = Services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Core Services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IToolRepository, ToolRepository>();
        services.AddHttpClient<IToolDownloadService, ToolDownloadService>();
        services.AddSingleton(LocalizationService.Instance);
        services.AddTransient<ApktoolRunner>();
        services.AddTransient<UbersignRunner>();
        services.AddTransient<SmaliAnalyserService>();
        services.AddTransient<ReportService>();
        services.AddSingleton<ISystemService, PulseAPK.Core.Services.SystemService>();

        // Patching services
        services.AddTransient<PatchRequestValidatorService>();
        services.AddTransient<IArchitectureDetectionService, ArchitectureDetectionService>();
        services.AddTransient<IFridaArtifactService, FridaArtifactService>();
        services.AddTransient<IApktoolService, ApktoolServiceAdapter>();
        services.AddTransient<IActivityDetectionService, ActivityDetectionService>();
        services.AddTransient<IManifestPatchService, ManifestPatchService>();
        services.AddTransient<IGadgetInjectionService, GadgetInjectionService>();
        services.AddTransient<ISmaliPatchService, SmaliPatchService>();
        services.AddTransient<IDexMergeService, DexMergeService>();
        services.AddTransient<ISigningService, SigningService>();
        services.AddTransient<IPatchPipelineService, PatchPipelineService>();

        // Avalonia Services
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        services.AddSingleton<IDispatcherService, AvaloniaDispatcherService>();
        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        services.AddSingleton<IThemeService, AvaloniaThemeService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<DecompileViewModel>();
        services.AddTransient<BuildViewModel>();
        services.AddTransient<PatchViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AnalyserViewModel>();
        services.AddTransient<AboutViewModel>();

        // Windows
        services.AddTransient<MainWindow>();
    }
}
