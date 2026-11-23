using System;
using System.Diagnostics;
using System.IO;
using PulseAPK.Core.Utils;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PulseAPK.Core.Services
{
    public class UbersignRunner
    {
        private readonly ISettingsService _settingsService;

        public UbersignRunner()
            : this(new SettingsService())
        {
        }

        public UbersignRunner(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public event Action<string>? OutputDataReceived;

        public Task<int> RunSigningAsync(string inputApk, string signedOutputApk, CancellationToken cancellationToken = default)
            => RunSigningAsync(inputApk, signedOutputApk, File.Exists(signedOutputApk), cancellationToken);

        internal async Task<int> RunSigningAsync(string inputApk, string signedOutputApk, bool overwrite, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(inputApk))
            {
                throw new ArgumentException("Input APK path cannot be null or empty.", nameof(inputApk));
            }

            if (string.IsNullOrWhiteSpace(signedOutputApk))
            {
                throw new ArgumentException("Signed output APK path cannot be null or empty.", nameof(signedOutputApk));
            }

            if (!File.Exists(inputApk))
            {
                throw new FileNotFoundException($"Input APK '{inputApk}' was not found.");
            }

            var (ubersignPath, isJar) = GetUbersignPath();
            if (!File.Exists(ubersignPath))
            {
                throw new FileNotFoundException($"Could not find 'ubersign.jar' (or executable) at '{ubersignPath}'.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var workspace = new OutputWorkspace(signedOutputApk);
            var outputDirectory = workspace.DirectoryPath;
            var startInfo = new ProcessStartInfo(isJar ? "java" : ubersignPath)
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            if (isJar)
            {
                startInfo.ArgumentList.Add("-jar");
                startInfo.ArgumentList.Add(ubersignPath);
            }
            foreach (var argument in new[] { "-a", inputApk, "-o", outputDirectory, "--allowResign" })
                startInfo.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    OutputDataReceived?.Invoke(e.Data);
                    Debug.WriteLine($"[UBERSIGN INFO] {e.Data}");
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    OutputDataReceived?.Invoke(e.Data);
                    Debug.WriteLine($"[UBERSIGN ERROR] {e.Data}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await ProcessLifetime.WaitAsync(process, cancellationToken);

            if (process.ExitCode != 0)
            {
                return process.ExitCode;
            }

            var createdFile = FindSignedApk(inputApk, outputDirectory);

            if (string.IsNullOrWhiteSpace(createdFile))
            {
                OutputDataReceived?.Invoke("Signing completed but no signed APK was produced in the output directory.");
                return 1;
            }

            cancellationToken.ThrowIfCancellationRequested();
            OutputWorkspace.PublishFile(createdFile, signedOutputApk, overwrite);
            return process.ExitCode;
        }

        private (string Path, bool IsJar) GetUbersignPath()
        {
            var root = string.IsNullOrWhiteSpace(AppDomain.CurrentDomain.BaseDirectory)
                ? Directory.GetCurrentDirectory()
                : AppDomain.CurrentDomain.BaseDirectory;

            var configuredPath = _settingsService.Settings.UbersignPath?.Trim();
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                var resolvedPath = Path.IsPathRooted(configuredPath)
                    ? configuredPath
                    : Path.Combine(root, configuredPath);

                if (File.Exists(resolvedPath))
                {
                    var isJar = string.Equals(Path.GetExtension(resolvedPath), ".jar", StringComparison.OrdinalIgnoreCase);
                    return (resolvedPath, isJar);
                }
            }

            var jarPath = Path.Combine(root, "ubersign.jar");
            if (File.Exists(jarPath)) return (jarPath, true);

            var ubersignPath = Path.Combine(root, "ubersign");
            if (File.Exists(ubersignPath)) return (ubersignPath, false);

            var windowsExecutable = Path.Combine(root, "ubersign.exe");
            return File.Exists(windowsExecutable) ? (windowsExecutable, false) : (jarPath, true);
        }

        private static string? FindSignedApk(string inputApk, string outputDirectory)
        {
            var inputFileName = Path.GetFileNameWithoutExtension(inputApk);
            var expectedNames = new[]
            {
                $"{inputFileName}-aligned-signed.apk",
                $"{inputFileName}-signed.apk",
                $"{inputFileName}-aligned-debugSigned.apk",
                $"{inputFileName}-debugSigned.apk"
            };

            var matchedFile = expectedNames
                .Select(name => Path.Combine(outputDirectory, name))
                .FirstOrDefault(File.Exists);

            if (matchedFile is not null)
            {
                return matchedFile;
            }

            return Directory.EnumerateFiles(outputDirectory, "*.apk")
                .Where(path =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(path);
                    return fileName.StartsWith(inputFileName, StringComparison.OrdinalIgnoreCase)
                           && fileName.Contains("signed", StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
    }
}
