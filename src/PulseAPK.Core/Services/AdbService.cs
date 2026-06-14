using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using PulseAPK.Core.Models;

namespace PulseAPK.Core.Services;

public sealed class AdbService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly Regex PackageRegex = new(@"package:\s+name='([^']+)'", RegexOptions.Compiled);
    private static readonly Regex LaunchableActivityRegex = new(@"launchable-activity:\s+name='([^']+)'", RegexOptions.Compiled);

    private readonly ISettingsService _settingsService;

    public AdbService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<string?> ResolveAdbPathAsync(CancellationToken cancellationToken = default)
    {
        var candidates = GetAdbCandidates();

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (IsExplicitPath(candidate) && !File.Exists(ExpandHome(candidate)))
            {
                continue;
            }

            var result = await RunProcessAsync(ExpandHome(candidate), ["version"], TimeSpan.FromSeconds(5), cancellationToken);
            if (result.ExitCode == 0)
            {
                return ExpandHome(candidate);
            }
        }

        return null;
    }

    public async Task<AdbCommandResult> RunAdbAsync(string adbPath, IEnumerable<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(adbPath))
        {
            return new AdbCommandResult("adb", arguments.ToArray(), string.Empty, "ADB was not found.", -1, false);
        }

        return await RunProcessAsync(adbPath, arguments.ToArray(), timeout ?? DefaultTimeout, cancellationToken);
    }

    public async Task<IReadOnlyList<AdbDevice>> GetDevicesAsync(string adbPath, CancellationToken cancellationToken = default)
    {
        var result = await RunAdbAsync(adbPath, ["devices", "-l"], DefaultTimeout, cancellationToken);
        if (result.ExitCode != 0)
        {
            return [];
        }

        return ParseDevices(result.StandardOutput);
    }

    public async Task<(string? PackageName, string? LaunchableActivity, AdbCommandResult? CommandResult)> DetectPackageFromApkAsync(
        string apkPath,
        CancellationToken cancellationToken = default)
    {
        var aaptPath = await ResolveAaptPathAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(aaptPath))
        {
            return (null, null, null);
        }

        var result = await RunProcessAsync(aaptPath, ["dump", "badging", apkPath], DefaultTimeout, cancellationToken);
        if (result.ExitCode != 0)
        {
            return (null, null, result);
        }

        var packageName = PackageRegex.Match(result.StandardOutput).Groups.Cast<Group>().ElementAtOrDefault(1)?.Value;
        var launchableActivity = LaunchableActivityRegex.Match(result.StandardOutput).Groups.Cast<Group>().ElementAtOrDefault(1)?.Value;
        return (packageName, launchableActivity, result);
    }

    public static IReadOnlyList<AdbDevice> ParseDevices(string output)
    {
        var devices = new List<AdbDevice>();

        foreach (var rawLine in output.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)
                || line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var model = parts
                .FirstOrDefault(part => part.StartsWith("model:", StringComparison.OrdinalIgnoreCase))
                ?.Substring("model:".Length) ?? string.Empty;

            devices.Add(new AdbDevice(parts[0], parts[1], model));
        }

        return devices;
    }

    public static IReadOnlyList<string> SplitCommandLine(string command)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var inSingleQuotes = false;
        var inDoubleQuotes = false;
        var escaping = false;

        foreach (var ch in command)
        {
            if (escaping)
            {
                current.Append(ch);
                escaping = false;
                continue;
            }

            if (ch == '\\' && !inSingleQuotes)
            {
                escaping = true;
                continue;
            }

            if (ch == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
                continue;
            }

            if (ch == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inSingleQuotes && !inDoubleQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (escaping)
        {
            current.Append('\\');
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args;
    }

    private async Task<string?> ResolveAaptPathAsync(CancellationToken cancellationToken)
    {
        var sdkRoots = new[]
        {
            Environment.GetEnvironmentVariable("ANDROID_HOME"),
            Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Android", "Sdk")
        };

        var candidates = new List<string> { "aapt" };
        foreach (var sdkRoot in sdkRoots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            var buildTools = Path.Combine(sdkRoot!, "build-tools");
            if (Directory.Exists(buildTools))
            {
                candidates.AddRange(Directory
                    .EnumerateDirectories(buildTools)
                    .OrderByDescending(Path.GetFileName)
                    .Select(directory => Path.Combine(directory, OperatingSystem.IsWindows() ? "aapt.exe" : "aapt")));
            }
        }

        foreach (var candidate in candidates)
        {
            if (IsExplicitPath(candidate) && !File.Exists(candidate))
            {
                continue;
            }

            var result = await RunProcessAsync(candidate, ["version"], TimeSpan.FromSeconds(5), cancellationToken);
            if (result.ExitCode == 0)
            {
                return candidate;
            }
        }

        return null;
    }

    private IEnumerable<string> GetAdbCandidates()
    {
        var executableName = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        var configuredPath = _settingsService.Settings.AdbPath?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath;
        }

        var envPath = Environment.GetEnvironmentVariable("ADB_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            yield return envPath;
        }

        var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
        if (!string.IsNullOrWhiteSpace(androidHome))
        {
            yield return Path.Combine(androidHome, "platform-tools", executableName);
        }

        var sdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (!string.IsNullOrWhiteSpace(sdkRoot))
        {
            yield return Path.Combine(sdkRoot, "platform-tools", executableName);
        }

        yield return Path.Combine("~", "Android", "Sdk", "platform-tools", executableName);
        yield return "adb";
    }

    private static async Task<AdbCommandResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                return new AdbCommandResult(executable, arguments, stdout, stderr, process.ExitCode, false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                var stdout = await ReadCompletedOrEmptyAsync(stdoutTask);
                var stderr = await ReadCompletedOrEmptyAsync(stderrTask);
                return new AdbCommandResult(executable, arguments, stdout, stderr + $"{Environment.NewLine}Command timed out.", -1, true);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new AdbCommandResult(executable, arguments, string.Empty, ex.Message, -1, false);
        }
    }

    private static async Task<string> ReadCompletedOrEmptyAsync(Task<string> task)
    {
        try
        {
            return task.IsCompletedSuccessfully ? await task : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort timeout cleanup.
        }
    }

    private static bool IsExplicitPath(string path)
    {
        return path.Contains(Path.DirectorySeparatorChar)
            || path.Contains(Path.AltDirectorySeparatorChar)
            || path.StartsWith("~", StringComparison.Ordinal);
    }

    private static string ExpandHome(string path)
    {
        if (!path.StartsWith("~", StringComparison.Ordinal))
        {
            return path;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, path.TrimStart('~', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}
