using System;
using System.IO;

namespace PulseAPK.Core.Utils
{
    public static class PathUtils
    {
        private const string AppDirectoryName = "PulseAPK";

        public static string GetWritableAppDataRoot()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                return Path.Combine(localAppData, AppDirectoryName);
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                return Path.Combine(userProfile, ".pulseapk");
            }

            return Path.Combine(Path.GetTempPath(), AppDirectoryName);
        }

        public static string GetDefaultWorkspaceRoot()
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (TryResolveWritableDirectory(documents, AppDirectoryName, out var documentsWorkspace))
            {
                return documentsWorkspace;
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (TryResolveWritableDirectory(userProfile, AppDirectoryName, out var profileWorkspace))
            {
                return profileWorkspace;
            }

            var appDataRoot = GetWritableAppDataRoot();
            if (TryResolveWritableDirectory(appDataRoot, "workspace", out var appDataWorkspace))
            {
                return appDataWorkspace;
            }

            return Path.Combine(Path.GetTempPath(), AppDirectoryName, "workspace");
        }

        public static string GetDefaultDecompilePath()
        {
            var workspaceRoot = GetDefaultWorkspaceRoot();
            return Path.Combine(workspaceRoot, "decompiled");
        }

        public static string GetDefaultCompiledPath()
        {
            var workspaceRoot = GetDefaultWorkspaceRoot();
            return Path.Combine(workspaceRoot, "compiled");
        }

        public static string GetDefaultReportsPath()
        {
            var writableRoot = GetWritableAppDataRoot();
            return Path.Combine(writableRoot, "reports");
        }

        public static string GetDefaultScriptsPath()
        {
            var writableRoot = GetWritableAppDataRoot();
            return Path.Combine(writableRoot, "scripts");
        }

        private static bool TryResolveWritableDirectory(string? parentDirectory, string childDirectory, out string path)
        {
            path = string.Empty;

            if (string.IsNullOrWhiteSpace(parentDirectory))
            {
                return false;
            }

            try
            {
                path = Path.Combine(parentDirectory, childDirectory);
                Directory.CreateDirectory(path);
                return true;
            }
            catch
            {
                path = string.Empty;
                return false;
            }
        }
    }
}
