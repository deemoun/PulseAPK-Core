using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PulseAPK.Core.Services;
using Xunit;

namespace PulseAPK.Tests.Services;

public class ApktoolRunnerTests
{
    [Fact]
    public async Task RunBuildAsync_StripsWrappingQuotesFromConfiguredAndArgumentPaths()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"pulseapk-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var fakeJavaPath = Path.Combine(tempRoot, "java");
        var capturedArgsPath = Path.Combine(tempRoot, "captured-args.txt");
        var fakeApktoolPath = Path.Combine(tempRoot, "apktool.jar");
        var projectDir = Path.Combine(tempRoot, "decompiled");
        var outputApk = Path.Combine(tempRoot, "compiled", "decompiled.apk");

        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(Path.GetDirectoryName(outputApk)!);
        File.WriteAllText(fakeApktoolPath, "fake apktool");

        var escapedCapturePath = capturedArgsPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var script = $"#!/usr/bin/env bash\nprintf '%s\n' \"$@\" > \"{escapedCapturePath}\"\nexit 0\n";
        File.WriteAllText(fakeJavaPath, script);
        MakeExecutable(fakeJavaPath);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", $"{tempRoot}{Path.PathSeparator}{originalPath}");

        try
        {
            var settings = new TestSettingsService($"\"{fakeApktoolPath}\"");
            var runner = new ApktoolRunner(settings);

            var exitCode = await runner.RunBuildAsync($"\"{projectDir}\"", $"\"{outputApk}\"", useAapt2: false);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(capturedArgsPath));

            var args = File.ReadAllLines(capturedArgsPath);

            Assert.Equal(new[] { "-jar", fakeApktoolPath, "b", projectDir, "-o", outputApk }, args);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunBuildAsync_UsesExecutableDirectlyWhenApktoolPathIsNotJar()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"pulseapk-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var fakeApktoolPath = Path.Combine(tempRoot, "apktool");
        var capturedArgsPath = Path.Combine(tempRoot, "captured-args.txt");
        var projectDir = Path.Combine(tempRoot, "decompiled");
        var outputApk = Path.Combine(tempRoot, "compiled", "decompiled.apk");

        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(Path.GetDirectoryName(outputApk)!);

        var escapedCapturePath = capturedArgsPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var script = $"#!/usr/bin/env bash\nprintf '%s\n' \"$@\" > \"{escapedCapturePath}\"\nexit 0\n";
        File.WriteAllText(fakeApktoolPath, script);
        MakeExecutable(fakeApktoolPath);

        try
        {
            var settings = new TestSettingsService(fakeApktoolPath);
            var runner = new ApktoolRunner(settings);

            var exitCode = await runner.RunBuildAsync(projectDir, outputApk, useAapt2: true);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(capturedArgsPath));

            var args = File.ReadAllLines(capturedArgsPath);

            Assert.Equal(new[] { "b", projectDir, "-o", outputApk, "--use-aapt2" }, args);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void MakeExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        mode |= UnixFileMode.UserExecute;
        mode |= UnixFileMode.GroupExecute;
        mode |= UnixFileMode.OtherExecute;
        File.SetUnixFileMode(path, mode);
    }

    private sealed class TestSettingsService : ISettingsService
    {
        public TestSettingsService(string apktoolPath)
        {
            Settings = new AppSettings
            {
                ApktoolPath = apktoolPath
            };
        }

        public AppSettings Settings { get; }

        public void Save()
        {
        }
    }
}
