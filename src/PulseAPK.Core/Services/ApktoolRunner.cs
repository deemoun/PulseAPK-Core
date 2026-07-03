using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using PulseAPK.Core.Models;

namespace PulseAPK.Core.Services
{
    public class ApktoolRunner
    {
        private const int ProcessTailLineCount = 20;
        private const int ProcessTailMaxCharacters = 8000;

        private readonly ISettingsService _settingsService;
        private readonly IAppLogService? _appLogService;

        public event Action<string>? OutputDataReceived;

        public ApktoolRunner()
            : this(new SettingsService())
        {
        }

        public ApktoolRunner(ISettingsService settingsService, IAppLogService? appLogService = null)
        {
            _settingsService = settingsService;
            _appLogService = appLogService;
        }

        public async Task<ApktoolRunResult> RunDecompileAsync(string apkPath, string outputDir, bool decodeResources, bool decodeSources, bool keepOriginalManifest, bool forceOverwrite = false, CancellationToken cancellationToken = default)
        {
            var sanitizedApkPath = SanitizePathArgument(apkPath);
            var sanitizedOutputDir = SanitizePathArgument(outputDir);

            var args = new List<string> { "d", sanitizedApkPath, "-o", sanitizedOutputDir };

            if (!decodeResources) args.Add("-r");
            if (!decodeSources) args.Add("-s");
            if (keepOriginalManifest) args.Add("-m");

            if (forceOverwrite)
            {
                args.Add("-f"); // Force overwrite
            }

            return await RunProcessAsync(args, cancellationToken);
        }

        public async Task<ApktoolRunResult> RunBuildAsync(string projectPath, string outputApk, bool useAapt2, CancellationToken cancellationToken = default)
        {
            var sanitizedProjectPath = SanitizePathArgument(projectPath);
            var sanitizedOutputApk = SanitizePathArgument(outputApk);

            var args = new List<string> { "b", sanitizedProjectPath, "-o", sanitizedOutputApk };

            if (useAapt2) args.Add("--use-aapt2");

            return await RunProcessAsync(args, cancellationToken);
        }

        private async Task<ApktoolRunResult> RunProcessAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            var apktoolPath = ResolveConfiguredApktoolPath(_settingsService.Settings.ApktoolPath, _settingsService.SettingsDirectory);

            var executableMode = GetExecutableMode(apktoolPath);
            var startInfo = CreateStartInfo(apktoolPath, arguments, _settingsService.SettingsDirectory);
            var stdoutLines = new List<string>();
            var stderrLines = new List<string>();
            var lineSyncRoot = new object();

            using var process = new Process { StartInfo = startInfo };
            _appLogService?.LogInfo("ApktoolRunner", $"Starting apktool process. mode={executableMode}; argumentCount={arguments.Count}; timestamp={DateTimeOffset.UtcNow:O}");

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    lock (lineSyncRoot)
                    {
                        stdoutLines.Add(e.Data);
                    }

                    OutputDataReceived?.Invoke(e.Data);
                    Debug.WriteLine($"[INFO] {e.Data}");
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    lock (lineSyncRoot)
                    {
                        stderrLines.Add(e.Data);
                    }

                    OutputDataReceived?.Invoke(e.Data);
                    Debug.WriteLine($"[ERROR] {e.Data}");
                }
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                    }

                    LogProcessCompletion(executableMode, process.HasExited ? process.ExitCode : null, canceled: true, stdoutLines, stderrLines);
                    throw;
                }

                LogProcessCompletion(executableMode, process.ExitCode, canceled: false, stdoutLines, stderrLines);
                return new ApktoolRunResult(process.ExitCode, stdoutLines.ToArray(), stderrLines.ToArray());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _appLogService?.LogError("ApktoolRunner", $"Apktool process failed. mode={executableMode}; stdoutTail={FormatTail(stdoutLines)}; stderrTail={FormatTail(stderrLines)}", ex);
                throw;
            }
        }


        private void LogProcessCompletion(string executableMode, int? exitCode, bool canceled, IReadOnlyList<string> stdoutLines, IReadOnlyList<string> stderrLines)
        {
            _appLogService?.LogInfo(
                "ApktoolRunner",
                $"Apktool process completed. mode={executableMode}; exitCode={(exitCode.HasValue ? exitCode.Value.ToString() : "unknown")}; canceled={canceled}; stdoutTail={FormatTail(stdoutLines)}; stderrTail={FormatTail(stderrLines)}");
        }

        private static string GetExecutableMode(string apktoolPath)
        {
            var extension = Path.GetExtension(apktoolPath);
            if (string.Equals(extension, ".jar", StringComparison.OrdinalIgnoreCase))
            {
                return "java -jar";
            }

            if ((string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase))
                && OperatingSystem.IsWindows())
            {
                return ".bat/.cmd";
            }

            return "direct executable";
        }

        private static string FormatTail(IReadOnlyList<string> lines)
        {
            if (lines.Count == 0)
            {
                return "<empty>";
            }

            var tail = string.Join("\n", lines.Skip(Math.Max(0, lines.Count - ProcessTailLineCount)));
            return tail.Length <= ProcessTailMaxCharacters
                ? tail
                : tail[^ProcessTailMaxCharacters..];
        }

        private static ProcessStartInfo CreateStartInfo(string apktoolPath, IReadOnlyList<string> arguments, string settingsDirectory)
        {
            var workingDirectory = ResolveProcessWorkingDirectory(settingsDirectory);
            var extension = Path.GetExtension(apktoolPath);
            var isJar = string.Equals(extension, ".jar", StringComparison.OrdinalIgnoreCase);
            var isBatchFile = string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase);

            if (isJar)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "java",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory
                };

                startInfo.ArgumentList.Add("-jar");
                startInfo.ArgumentList.Add(apktoolPath);

                foreach (var argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                return startInfo;
            }

            if (isBatchFile && OperatingSystem.IsWindows())
            {
                return new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/d /s /c \"\"{EscapeForCmd(apktoolPath)}\" {JoinArgumentsForCmd(arguments)}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory
                };
            }

            var defaultStartInfo = new ProcessStartInfo
            {
                FileName = apktoolPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };

            foreach (var argument in arguments)
            {
                defaultStartInfo.ArgumentList.Add(argument);
            }

            return defaultStartInfo;
        }

        public static string ResolveConfiguredApktoolPath(string? configuredPath, string settingsDirectory)
        {
            var apktoolPath = SanitizePathArgument(configuredPath);

            if (string.IsNullOrWhiteSpace(apktoolPath))
            {
                throw new FileNotFoundException("Apktool path has not been configured.");
            }

            foreach (var candidate in EnumerateApktoolPathCandidates(apktoolPath, settingsDirectory))
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            throw new FileNotFoundException($"Apktool path '{apktoolPath}' does not exist.");
        }

        private static IEnumerable<string> EnumerateApktoolPathCandidates(string apktoolPath, string settingsDirectory)
        {
            if (Path.IsPathFullyQualified(apktoolPath))
            {
                yield return apktoolPath;
                yield break;
            }

            yield return Path.Combine(Environment.CurrentDirectory, apktoolPath);
            yield return Path.Combine(AppContext.BaseDirectory, apktoolPath);

            if (!string.IsNullOrWhiteSpace(settingsDirectory))
            {
                yield return Path.Combine(settingsDirectory, apktoolPath);
                yield return Path.Combine(settingsDirectory, "tools", apktoolPath);
            }
        }

        private static string ResolveProcessWorkingDirectory(string settingsDirectory)
        {
            if (!string.IsNullOrWhiteSpace(settingsDirectory))
            {
                Directory.CreateDirectory(settingsDirectory);
                return settingsDirectory;
            }

            return Path.GetTempPath();
        }

        private static string SanitizePathArgument(string? path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Trim('"');
        }

        private static string JoinArguments(IEnumerable<string> arguments)
        {
            return string.Join(" ", arguments.Select(QuoteArgument));
        }

        private static string JoinArgumentsForCmd(IEnumerable<string> arguments)
        {
            return string.Join(" ", arguments.Select(argument => QuoteArgument(EscapeForCmd(argument))));
        }

        private static string QuoteArgument(string argument)
        {
            return $"\"{argument}\"";
        }

        private static string EscapeForCmd(string argument)
        {
            return argument.Replace("\"", "\"\"");
        }
    }
}
