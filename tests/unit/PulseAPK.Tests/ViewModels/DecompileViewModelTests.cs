using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Services;
using PulseAPK.Core.ViewModels;
using Xunit;

namespace PulseAPK.Tests.ViewModels;

public class DecompileViewModelTests
{
    [Fact]
    public void RunCommand_IsDisabled_WithoutApkPathOrApktoolPath()
    {
        var viewModel = CreateViewModel(apktoolPath: string.Empty);

        Assert.False(viewModel.RunDecompileCommand.CanExecute(null));
    }

    [Fact]
    public void RunCommand_DisablesWhileRunning_WithExistingApk()
    {
        var viewModel = CreateViewModel(apktoolPath: string.Empty);

        var apkPath = Path.GetTempFileName();
        try
        {
            viewModel.ApkPath = apkPath;
            viewModel.IsRunning = true;
            Assert.False(viewModel.RunDecompileCommand.CanExecute(null));
            viewModel.IsRunning = false;
            Assert.True(viewModel.RunDecompileCommand.CanExecute(null));
        }
        finally
        {
            File.Delete(apkPath);
        }
    }

    [Fact]
    public async Task RunCommand_RemainsEnabled_AfterValidationFailure()
    {
        var dialogService = new TestDialogService();
        var viewModel = CreateViewModel(apktoolPath: string.Empty, dialogService: dialogService);

        var apkPath = Path.GetTempFileName();
        try
        {
            viewModel.ApkPath = apkPath;
            await viewModel.RunDecompileCommand.ExecuteAsync(null);
            Assert.True(viewModel.RunDecompileCommand.CanExecute(null));
            Assert.Contains(dialogService.Warnings, warning => warning.message.Contains("apktool", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(apkPath);
        }
    }


    [Fact]
    public async Task RunCommand_ShowsError_WhenOutputFolderIsInvalid()
    {
        var apktoolPath = Path.GetTempFileName();
        var apkPath = Path.GetTempFileName();

        try
        {
            var dialogService = new TestDialogService();
            var viewModel = CreateViewModel(apktoolPath: apktoolPath, dialogService: dialogService);
            viewModel.ApkPath = apkPath;
            viewModel.OutputFolder = "invalid\0folder";

            await viewModel.RunDecompileCommand.ExecuteAsync(null);

            Assert.True(viewModel.RunDecompileCommand.CanExecute(null));
            Assert.Contains(dialogService.Errors, error => error.title == "Invalid output folder");
        }
        finally
        {
            File.Delete(apktoolPath);
            File.Delete(apkPath);
        }
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 7, false)]
    [InlineData(0, -1, false)]
    [InlineData(0, 0, true)]
    [InlineData(8, 0, false)]
    public async Task BuildPublishesOnlyThisRunsCompletedOutputs(int buildExit, int signExit, bool produceSigned)
    {
        var root = Directory.CreateTempSubdirectory("pulse-build-").FullName;
        try
        {
            var tool = MakeTool(root, "builder", buildExit.ToString());
            var signer = MakeTool(root, "signer", produceSigned ? "signed" : signExit.ToString());
            if (signExit == -1) File.WriteAllBytes(signer, [0]);
            var settings = new TestSettingsService(tool);
            settings.Settings.UbersignPath = signer;
            var dialogs = new TestDialogService { Confirm = true };
            var model = new BuildViewModel(new TestFilePickerService(), settings, new ApktoolRunner(settings),
                new UbersignRunner(settings), dialogs, new ImmediateDispatcherService())
            {
                ProjectPath = root, OutputFolderPath = root, OutputApkName = "app.apk"
            };
            var unsigned = Path.Combine(root, "app.apk");
            var signed = Path.Combine(root, "app_signed.apk");
            File.WriteAllText(unsigned, "old build");
            File.WriteAllText(signed, "old signed");
            File.WriteAllText(Path.Combine(root, "app-signed.apk"), "stale signer output");
            await model.RunBuildCommand.ExecuteAsync(null);
            Assert.Equal(buildExit == 0 ? "fresh" : "old build", File.ReadAllText(unsigned));
            Assert.Equal(produceSigned ? "signed" : "old signed", File.ReadAllText(signed));
            Assert.Equal(1, dialogs.Errors.Count + dialogs.Infos.Count);
            if (produceSigned) Assert.Contains(signed, Assert.Single(dialogs.Infos));
            else if (buildExit == 0) Assert.Contains(unsigned, Assert.Single(dialogs.Errors).message);
            else Assert.Single(dialogs.Errors);
            Assert.Empty(Directory.GetDirectories(root, ".pulse-job-*"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecompilePreservesEditsOnFailureAndCancellation(bool cancel)
    {
        var root = Directory.CreateTempSubdirectory("pulse-decode-").FullName;
        try
        {
            var tool = MakeTool(root, "decoder", cancel ? "decode-wait" : "decode-fail");
            var input = Path.Combine(root, "input.apk");
            File.WriteAllText(input, "fixture");
            var output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
            File.WriteAllText(Path.Combine(output, "edit.txt"), "original");
            var dialogs = new TestDialogService { Confirm = true };
            using var model = CreateViewModel(tool, dialogs);
            model.ApkPath = input;
            model.ExtractToApkFolder = false;
            model.OutputFolder = output;
            var run = model.RunDecompileCommand.ExecuteAsync(null);
            if (cancel)
            {
                await WaitForFile(input + ".ready");
                model.CancelDecompileCommand.Execute(null);
            }
            await run.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("original", File.ReadAllText(Assert.Single(Directory.GetFiles(output))));
            Assert.Empty(Directory.GetDirectories(root, ".pulse-job-*"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelledSignerAndAdbExitBeforeReturning(bool sign)
    {
        var root = Directory.CreateTempSubdirectory("pulse-cancel-").FullName;
        try
        {
            var input = Path.Combine(root, "input.apk");
            File.WriteAllText(input, "fixture");
            var tool = MakeTool(root, "tool", "wait");
            var settings = new TestSettingsService(tool);
            settings.Settings.UbersignPath = tool;
            using var stop = new CancellationTokenSource();
            Task run = sign ? new UbersignRunner(settings).RunSigningAsync(input, Path.Combine(root, "signed.apk"), stop.Token)
                : new AdbService(settings).RunAdbAsync(tool, ["test", input], cancellationToken: stop.Token);
            await WaitForFile(input + ".pid");
            var pid = int.Parse(File.ReadAllText(input + ".pid"));
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(process.HasExited);
            Assert.False(File.Exists(Path.Combine(root, "signed.apk")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EditWhileToolRunsPublishesSeparateResult(bool build)
    {
        var root = Directory.CreateTempSubdirectory("pulse-mid-edit-").FullName;
        try
        {
            var tool = MakeTool(root, "worker", build ? "build-edited" : "decode-edited");
            var settings = new TestSettingsService(tool);
            var dialogs = new TestDialogService { Confirm = true };
            var output = build ? Path.Combine(root, "app.apk") : Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
            var original = build ? output : Path.Combine(output, "result.txt");
            File.WriteAllText(original, "before");
            using var decoder = CreateViewModel(tool, dialogs);
            Task run;
            if (build)
            {
                var model = new BuildViewModel(new TestFilePickerService(), settings, new ApktoolRunner(settings),
                    new UbersignRunner(settings), dialogs, new ImmediateDispatcherService())
                { ProjectPath = root, OutputFolderPath = root, OutputApkName = "app.apk", SignApk = false };
                run = model.RunBuildCommand.ExecuteAsync(null);
            }
            else
            {
                decoder.ApkPath = Path.Combine(root, "input.apk");
                File.WriteAllText(decoder.ApkPath, "fixture");
                decoder.ExtractToApkFolder = false;
                decoder.OutputFolder = output;
                run = decoder.RunDecompileCommand.ExecuteAsync(null);
            }
            var worker = Path.GetDirectoryName(tool)!;
            await WaitForFile(Path.Combine(worker, "ready"));
            File.WriteAllText(original, "edited");
            File.WriteAllText(Path.Combine(worker, "resume"), "go");
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("edited", File.ReadAllText(original));
            var alternate = Path.Combine(root, build ? "app-1.apk" : "project-1");
            Assert.Contains(alternate, Assert.Single(dialogs.Infos));
            Assert.Equal(build ? "rebuilt" : "decoded", File.ReadAllText(build ? alternate : Path.Combine(alternate, "result.txt")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DecompileDrainsBothOutputStreamsThroughTheLastLine()
    {
        var root = Directory.CreateTempSubdirectory("pulse-output-").FullName;
        try
        {
            var runner = new ApktoolRunner(new TestSettingsService(MakeTool(root, "writer", "decode-output")));
            int notifications = 0;
            runner.OutputDataReceived += _ => Interlocked.Increment(ref notifications);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var result = await runner.RunDecompileAsync(Path.Combine(root, "input.apk"), root,
                true, true, false, cancellationToken: timeout.Token);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(4097, result.Stdout.Count);
            Assert.Equal(4097, result.Stderr.Count);
            Assert.Equal(8194, notifications);
            Assert.Equal("Decoded resource 4095 — ✓", result.Stdout[^2]);
            Assert.Equal("last output", result.Stdout[^1]);
            Assert.Equal("last diagnostic", result.Stderr[^1]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SigningDrainsBothPipesBeforeReportingSuccess()
    {
        var root = Directory.CreateTempSubdirectory("pulse-signer-output-").FullName;
        try
        {
            var settings = new TestSettingsService("");
            settings.Settings.UbersignPath = MakeTool(root, "writer", "sign-output");
            var runner = new UbersignRunner(settings);
            var messages = new System.Collections.Concurrent.ConcurrentBag<string>();
            runner.OutputDataReceived += messages.Add;
            var input = Path.Combine(root, "app.apk");
            var signed = Path.Combine(root, "result.apk");
            File.WriteAllText(input, "fixture");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Assert.Equal(0, await runner.RunSigningAsync(input, signed, timeout.Token));
            Assert.Equal("signed", File.ReadAllText(signed));
            Assert.Equal(8194, messages.Count);
            Assert.Contains("last output", messages);
            Assert.Contains("last diagnostic", messages);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task WaitForFile(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!File.Exists(path) || new FileInfo(path).Length == 0) await Task.Delay(10, timeout.Token);
    }

    private static string MakeTool(string root, string name, string commands)
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ProcessFixture")))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
        File.WriteAllText(Path.Combine(dir, "mode"), commands);
        var path = Path.Combine(dir, OperatingSystem.IsWindows() ? "PulseFixture.exe" : "PulseFixture");
        return path;
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
        public string SettingsDirectory => Environment.CurrentDirectory;
        public event EventHandler? SettingsChanged;
        public void Save() => SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class TestDialogService : IDialogService
    {
        public List<(string message, string? title)> Warnings { get; } = new();
        public List<(string message, string? title)> Errors { get; } = new();

        public List<string> Infos { get; } = new();
        public bool Confirm { get; init; }
        public Task ShowInfoAsync(string message, string? title = null)
        {
            Infos.Add(message);
            return Task.CompletedTask;
        }

        public Task ShowWarningAsync(string message, string? title = null)
        {
            Warnings.Add((message, title));
            return Task.CompletedTask;
        }

        public Task ShowErrorAsync(string message, string? title = null)
        {
            Errors.Add((message, title));
            return Task.CompletedTask;
        }

        public Task<bool> ShowQuestionAsync(string message, string? title = null) => Task.FromResult(Confirm);
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
