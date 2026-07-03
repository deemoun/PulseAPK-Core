using System;
using System.IO;
using System.Text;

namespace PulseAPK.Core.Services;

public sealed class AppLogService : IAppLogService
{
    private const string AppName = "PulseAPK";
    private const string LogsFolderName = "logs";
    private const string LogFileName = "pulseapk.log";
    private readonly object _writeLock = new();

    public AppLogService()
        : this(ResolveDefaultLogFilePath())
    {
    }

    internal AppLogService(string logFilePath)
    {
        LogFilePath = logFilePath;
    }

    public string LogFilePath { get; }

    public void LogInfo(string category, string message) => Write("INFO", category, message, null);

    public void LogError(string category, string message, Exception? exception = null) => Write("ERROR", category, message, exception);

    private void Write(string level, string category, string message, Exception? exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var builder = new StringBuilder();
            builder.Append(DateTimeOffset.UtcNow.ToString("O"));
            builder.Append(" [");
            builder.Append(level);
            builder.Append("] ");
            builder.Append(SanitizeSingleLine(category));
            builder.Append(" - ");
            builder.AppendLine(message ?? string.Empty);

            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            lock (_writeLock)
            {
                File.AppendAllText(LogFilePath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostic logging must never block or crash app workflows.
        }
    }

    private static string ResolveDefaultLogFilePath()
    {
        var appDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appDataDirectory))
        {
            appDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        if (string.IsNullOrWhiteSpace(appDataDirectory))
        {
            appDataDirectory = Environment.CurrentDirectory;
        }

        return Path.Combine(appDataDirectory, AppName, LogsFolderName, LogFileName);
    }

    private static string SanitizeSingleLine(string value) => (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
}
