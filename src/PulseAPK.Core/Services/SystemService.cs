using System.Diagnostics;
using System.Runtime.InteropServices;
using PulseAPK.Core.Abstractions;

namespace PulseAPK.Core.Services;

public class SystemService : ISystemService
{
    public void OpenFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return;

        OpenPath(folderPath);
    }

    public void OpenUrl(string url)
    {
        OpenPath(url);
    }

    private void OpenPath(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                StartDetached("explorer.exe", path);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                StartDetached("xdg-open", path, suppressStandardError: true);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                StartDetached("open", path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open path '{path}': {ex.Message}");
        }
    }

    private static void StartDetached(string fileName, string argument, bool suppressStandardError = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add(argument);

        if (suppressStandardError)
        {
            startInfo.RedirectStandardError = true;
        }

        var process = Process.Start(startInfo);
        if (process is null)
            return;

        if (!suppressStandardError)
        {
            process.Dispose();
            return;
        }

        process.EnableRaisingEvents = true;
        process.ErrorDataReceived += static (_, _) => { };
        process.Exited += static (sender, _) => ((Process)sender!).Dispose();
        process.BeginErrorReadLine();
    }
}
