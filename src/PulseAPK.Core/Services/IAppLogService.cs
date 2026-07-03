using System;

namespace PulseAPK.Core.Services;

public interface IAppLogService
{
    string LogFilePath { get; }
    void LogInfo(string category, string message);
    void LogError(string category, string message, Exception? exception = null);
}
