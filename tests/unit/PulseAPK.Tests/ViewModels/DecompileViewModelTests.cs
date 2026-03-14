using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Services;
using PulseAPK.Core.ViewModels;
using Xunit;

namespace PulseAPK.Tests.ViewModels;

public class DecompileViewModelTests
{
    [Fact]
    public void RunCommand_CanExecute_WithoutApkPathOrApktoolPath()
    {
        var viewModel = CreateViewModel(apktoolPath: string.Empty);

        Assert.True(viewModel.RunDecompileCommand.CanExecute(null));
    }

    [Fact]
    public void RunCommand_CanExecute_OnlyDependsOnIsRunning()
    {
        var viewModel = CreateViewModel(apktoolPath: string.Empty);

        viewModel.IsRunning = true;
        Assert.False(viewModel.RunDecompileCommand.CanExecute(null));

        viewModel.IsRunning = false;
        Assert.True(viewModel.RunDecompileCommand.CanExecute(null));
    }

    [Fact]
    public async Task RunCommand_RemainsEnabled_AfterValidationFailure()
    {
        var dialogService = new TestDialogService();
        var viewModel = CreateViewModel(apktoolPath: string.Empty, dialogService: dialogService);

        await viewModel.RunDecompileCommand.ExecuteAsync(null);

        Assert.True(viewModel.RunDecompileCommand.CanExecute(null));
        Assert.Contains(dialogService.Warnings, warning => warning.message.Contains("apktool", StringComparison.OrdinalIgnoreCase));
    }

    private static DecompileViewModel CreateViewModel(string apktoolPath, TestDialogService? dialogService = null)
    {
        return new DecompileViewModel(
            new TestFilePickerService(),
            new TestSettingsService(apktoolPath),
            new ApktoolRunner(new TestSettingsService(apktoolPath)),
            dialogService ?? new TestDialogService(),
            new ImmediateDispatcherService(),
            new TestSystemService());
    }

    private sealed class TestFilePickerService : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string filter) => Task.FromResult<string?>(null);
        public Task<string?> OpenFolderAsync(string? initialDirectory = null) => Task.FromResult<string?>(null);
    }

    private sealed class TestSettingsService : ISettingsService
    {
        public TestSettingsService(string apktoolPath)
        {
            Settings = new AppSettings { ApktoolPath = apktoolPath };
        }

        public AppSettings Settings { get; }
        public void Save() { }
    }

    private sealed class TestDialogService : IDialogService
    {
        public List<(string message, string? title)> Warnings { get; } = new();

        public Task ShowInfoAsync(string message, string? title = null) => Task.CompletedTask;

        public Task ShowWarningAsync(string message, string? title = null)
        {
            Warnings.Add((message, title));
            return Task.CompletedTask;
        }

        public Task ShowErrorAsync(string message, string? title = null) => Task.CompletedTask;

        public Task<bool> ShowQuestionAsync(string message, string? title = null) => Task.FromResult(false);
    }

    private sealed class ImmediateDispatcherService : IDispatcherService
    {
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());

        public bool CheckAccess() => true;
    }

    private sealed class TestSystemService : ISystemService
    {
        public void OpenFolder(string folderPath) { }
        public void OpenUrl(string url) { }
    }
}
