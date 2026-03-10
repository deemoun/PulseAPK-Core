using System;
using System.IO;
using System.Threading.Tasks;
using PulseAPK.Core.Utils;

namespace PulseAPK.Core.Services
{
    public class ReportService
    {
        public async Task<string> SaveReportAsync(string reportContent, string folderName)
        {
            try
            {
                var reportsDirectory = EnsureReportsDirectory();

                // Format filename: [date]-[time]-[folder name].txt
                // Using a safe date format for filenames
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
                string safeFolderName = GetSafeFilename(folderName);
                string filename = $"{timestamp}-{safeFolderName}.txt";
                string filePath = Path.Combine(reportsDirectory, filename);

                // Write content to file
                await File.WriteAllTextAsync(filePath, reportContent);

                return filePath;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save report: {ex.Message}", ex);
            }
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
