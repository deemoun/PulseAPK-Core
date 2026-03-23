using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Models;
using PulseAPK.Core.Services;
using PulseAPK.Core.ViewModels;
using Xunit;

namespace PulseAPK.Tests.ViewModels;

public class PatchViewModelTests
{
    [Fact]
    public void Constructor_EnablesInjectAllArchitecturesByDefault()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.InjectLibForAllArchitectures);
    }

    [Fact]
    public void Constructor_SelectsFirstScriptInjectionOptionByDefault()
    {
        var viewModel = CreateViewModel();

        Assert.Same(viewModel.ScriptInjectionOptions[0], viewModel.SelectedScriptInjectionOption);
        Assert.Equal(ScriptInjectionProfile.FridaGadget, viewModel.SelectedScriptInjectionOption.Profile);
    }

    [Fact]
    public void SelectingSampleInjection_SetsSampleInjectionState()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedScriptInjectionOption = viewModel.ScriptInjectionOptions.Single(option => option.Profile == ScriptInjectionProfile.SampleInjection);

        Assert.True(viewModel.IsSampleInjectionProfileSelected);
    }

    [Fact]
    public void ScriptInjectionOptions_IncludeFridaListenerProfile()
    {
        var viewModel = CreateViewModel();

        Assert.Contains(viewModel.ScriptInjectionOptions, option => option.Profile == ScriptInjectionProfile.FridaListener && option.Label == "Inject gadget listener");
    }

    private static PatchViewModel CreateViewModel()
    {
        return new PatchViewModel(
            new TestFilePickerService(),
            new TestSettingsService(),
            new TestPatchPipelineService(),
            new TestDialogService(),
            LocalizationService.Instance,
            new TestSystemService());
    }

    private sealed class TestPatchPipelineService : IPatchPipelineService
    {
        public Task<PatchResult> RunAsync(PatchRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PatchResult { Success = true, OutputApkPath = request.OutputApkPath });
    }

    private sealed class TestFilePickerService : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string filter) => Task.FromResult<string?>(null);
        public Task<string?> OpenFolderAsync(string? initialDirectory = null) => Task.FromResult<string?>(null);
    }

    private sealed class TestSettingsService : ISettingsService
    {
        public AppSettings Settings { get; } = new() { ApktoolPath = string.Empty };
        public string SettingsDirectory => Environment.CurrentDirectory;
        public void Save() { }
    }

    private sealed class TestDialogService : IDialogService
    {
        public Task ShowInfoAsync(string message, string? title = null) => Task.CompletedTask;
        public Task ShowWarningAsync(string message, string? title = null) => Task.CompletedTask;
        public Task ShowErrorAsync(string message, string? title = null) => Task.CompletedTask;
        public Task<bool> ShowQuestionAsync(string message, string? title = null) => Task.FromResult(false);
    }

    private sealed class TestSystemService : ISystemService
    {
        public void OpenFolder(string folderPath) { }
        public void OpenUrl(string url) { }
    }
}
