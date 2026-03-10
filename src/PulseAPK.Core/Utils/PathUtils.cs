using System;
using System.IO;

namespace PulseAPK.Core.Utils
{
    public static class PathUtils
    {
        public static string GetDefaultDecompilePath()
        {
            var writableRoot = GetWritableAppDataRoot();
            return Path.Combine(writableRoot, "decompiled");
        }

        private static string GetWritableAppDataRoot()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                return Path.Combine(localAppData, "PulseAPK");
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                return Path.Combine(userProfile, ".pulseapk");
            }

            return Path.Combine(Path.GetTempPath(), "PulseAPK");
        }
    }
}
