namespace PulseAPK.Core.Abstractions;

public interface IToolRepository
{
    string ToolsDirectory { get; }
    string GetToolPath(string fileName);
    bool TryGetCachedToolPath(string fileName, out string path);
}
