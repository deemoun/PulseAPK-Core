using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

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

            var args = new StringBuilder("d");
            args.Append($" \"{sanitizedApkPath}\"");
            args.Append($" -o \"{sanitizedOutputDir}\"");

            if (!decodeResources) args.Append(" -r");
            if (!decodeSources) args.Append(" -s");
            if (keepOriginalManifest) args.Append(" -m");

            if (forceOverwrite)
            {
                args.Append(" -f"); // Force overwrite
            }

            return await RunProcessAsync(args.ToString(), cancellationToken);
        }

        public async Task<int> RunBuildAsync(string projectPath, string outputApk, bool useAapt2, CancellationToken cancellationToken = default)
        {
            var sanitizedProjectPath = SanitizePathArgument(projectPath);
            var sanitizedOutputApk = SanitizePathArgument(outputApk);

            var args = new StringBuilder("b");
            args.Append($" \"{sanitizedProjectPath}\"");
            args.Append($" -o \"{sanitizedOutputApk}\"");

            if (useAapt2) args.Append(" --use-aapt2");

            return await RunProcessAsync(args.ToString(), cancellationToken);
        }

        private async Task<int> RunProcessAsync(string arguments, CancellationToken cancellationToken)
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

            var startInfo = new ProcessStartInfo
            {
                FileName = "java",
                Arguments = $"-jar \"{apktoolPath}\" {arguments}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

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

        private static string SanitizePathArgument(string? path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Trim('"');
        }
    }
}
