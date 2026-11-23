using System;
using System.IO;
using System.Threading.Tasks;
using PulseAPK.Core.Utils;

namespace PulseAPK.Core.Services
{
    public class ReportService
    {
        private readonly Func<string> _reportsDirectory;
        private readonly Func<DateTime> _now;

        public ReportService() : this(EnsureReportsDirectory, () => DateTime.Now) { }

        internal ReportService(Func<string> reportsDirectory, Func<DateTime> now)
        {
            _reportsDirectory = reportsDirectory;
            _now = now;
        }

        public Task<string> SaveAnalysisAsync(AnalysisResult result, string folderName)
        {
            var findings = result.Findings.OrderBy(f => f.RelativePath, StringComparer.Ordinal)
                .ThenBy(f => f.LineNumber).ThenBy(f => f.Category, StringComparer.Ordinal)
                .Select(f => new { path = f.RelativePath.Replace(Path.DirectorySeparatorChar, '/'), line = f.LineNumber,
                    category = f.Category, severity = f.Severity, description = f.Description,
                    evidence = f.Category.Equals("hardcoded_creds", StringComparison.OrdinalIgnoreCase) ? "[masked]" : null });
            var report = new { project = folderName, status = result.Status,
                coverage = new { scanned = result.Scanned, excluded = result.Excluded, unreadable = result.Unreadable,
                    inaccessibleDirectories = result.Inaccessible, timedOutPatterns = result.Timeouts, ruleWarnings = result.RuleWarnings.Count },
                excludedLibraryPaths = result.LibraryPaths, findings };
            var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            return SaveAsync(json, folderName, ".json");
        }

        public Task<string> SaveReportAsync(string reportContent, string folderName) => SaveAsync(reportContent, folderName, ".txt");

        private async Task<string> SaveAsync(string reportContent, string folderName, string extension)
        {
            var directory = _reportsDirectory();
            var stem = $"{_now():yyyy-MM-dd-HH-mm-ss}-{GetSafeFilename(folderName)}";
            return await AtomicOutput.WriteAsync(Path.Combine(directory, stem + extension), async output =>
            {
                using var writer = new StreamWriter(output, leaveOpen: true);
                await writer.WriteAsync(reportContent);
            });
        }

        private static string EnsureReportsDirectory()
        {
            var preferredReportsDir = PathUtils.GetDefaultReportsPath();
            if (TryEnsureDirectory(preferredReportsDir, out var ensuredReportsDir))
            {
                return ensuredReportsDir;
            }

            var fallbackReportsDir = Path.Combine(GetApplicationRootPath(), "reports");
            if (TryEnsureDirectory(fallbackReportsDir, out var ensuredFallbackDir))
            {
                return ensuredFallbackDir;
            }

            return Directory.GetCurrentDirectory();
        }

        private static bool TryEnsureDirectory(string path, out string ensuredPath)
        {
            ensuredPath = path;

            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetApplicationRootPath()
        {
            return string.IsNullOrWhiteSpace(AppDomain.CurrentDomain.BaseDirectory)
                ? Directory.GetCurrentDirectory()
                : AppDomain.CurrentDomain.BaseDirectory;
        }

        private string GetSafeFilename(string filename)
        {
            return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
        }
    }
}
