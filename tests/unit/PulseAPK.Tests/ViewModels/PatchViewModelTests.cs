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
    public void Constructor_EnablesInjectAllArchitecturesAndCustomScriptByDefault()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.InjectLibForAllArchitectures);
        Assert.True(viewModel.AddCustomScript);
    }

    [Fact]
    public void Constructor_SelectsFirstScriptInjectionOptionByDefault()
    {
        var viewModel = CreateViewModel();

        Assert.Same(viewModel.ScriptInjectionOptions[0], viewModel.SelectedScriptInjectionOption);
        Assert.Equal(ScriptInjectionProfile.FridaGadget, viewModel.SelectedScriptInjectionOption.Profile);
    }

    [Fact]
    public void SelectingSampleInjection_DisablesCustomScriptAndCanAddCustomScript()
    {
        var viewModel = CreateViewModel();
        viewModel.AddCustomScript = true;

        viewModel.SelectedScriptInjectionOption = viewModel.ScriptInjectionOptions.Single(option => option.Profile == ScriptInjectionProfile.SampleInjection);

        Assert.False(viewModel.CanAddCustomScript);
        Assert.False(viewModel.AddCustomScript);
        Assert.True(viewModel.IsSampleInjectionProfileSelected);
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
