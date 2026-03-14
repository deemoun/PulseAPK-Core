using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

namespace PulseAPK.Core.Services
{
    public class ApktoolRunner
    {
        private readonly ISettingsService _settingsService;

        public event Action<string>? OutputDataReceived;

        public ApktoolRunner()
            : this(new SettingsService())
        {
        }

        public ApktoolRunner(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public async Task<int> RunDecompileAsync(string apkPath, string outputDir, bool decodeResources, bool decodeSources, bool keepOriginalManifest, bool forceOverwrite = false, CancellationToken cancellationToken = default)
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

        public async Task<int> RunBuildAsync(string projectPath, string outputApk, bool useAapt2, CancellationToken cancellationToken = default)
        {
            var sanitizedProjectPath = SanitizePathArgument(projectPath);
            var sanitizedOutputApk = SanitizePathArgument(outputApk);

            var args = new List<string> { "b", sanitizedProjectPath, "-o", sanitizedOutputApk };

            if (useAapt2) args.Add("--use-aapt2");

            return await RunProcessAsync(args, cancellationToken);
        }

        private async Task<int> RunProcessAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            var apktoolPath = SanitizePathArgument(_settingsService.Settings.ApktoolPath);

            if (string.IsNullOrWhiteSpace(apktoolPath))
            {
                throw new FileNotFoundException("Apktool path has not been configured.");
            }

            if (!File.Exists(apktoolPath))
            {
                throw new FileNotFoundException($"Apktool path '{apktoolPath}' does not exist.");
            }

            var startInfo = CreateStartInfo(apktoolPath, arguments);

            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    OutputDataReceived?.Invoke(e.Data);
                    Debug.WriteLine($"[INFO] {e.Data}");
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    OutputDataReceived?.Invoke(e.Data);
                    Debug.WriteLine($"[ERROR] {e.Data}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            return process.ExitCode;
        }

        private static ProcessStartInfo CreateStartInfo(string apktoolPath, IReadOnlyList<string> arguments)
        {
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
                    CreateNoWindow = true
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
                    CreateNoWindow = true
                };
            }

            var defaultStartInfo = new ProcessStartInfo
            {
                FileName = apktoolPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                defaultStartInfo.ArgumentList.Add(argument);
            }

            return defaultStartInfo;
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
