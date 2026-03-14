using PulseAPK.Core.Abstractions;

namespace PulseAPK.Core.Services;

public sealed class ToolRepository : IToolRepository
{
    public string ToolsDirectory { get; }

    public ToolRepository(ISettingsService settingsService)
    {
        ToolsDirectory = Path.Combine(settingsService.SettingsDirectory, "tools");
    }

    public string GetToolPath(string fileName)
    {
        EnsureToolsDirectory();
        return Path.Combine(ToolsDirectory, fileName);
    }

    public bool TryGetCachedToolPath(string fileName, out string path)
    {
        path = Path.Combine(ToolsDirectory, fileName);
        return File.Exists(path);
    }

    private void EnsureToolsDirectory()
    {
        Directory.CreateDirectory(ToolsDirectory);
    }
}
