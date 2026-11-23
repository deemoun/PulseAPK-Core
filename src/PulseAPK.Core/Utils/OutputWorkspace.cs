namespace PulseAPK.Core.Utils;

internal sealed class OutputWorkspace : IDisposable
{
    public string DirectoryPath { get; }

    public OutputWorkspace(string destination)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        Directory.CreateDirectory(parent);
        DirectoryPath = Path.Combine(parent, $".pulse-job-{Guid.NewGuid():N}");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(DirectoryPath);
        else Directory.CreateDirectory(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static void PublishFile(string source, string destination, bool overwrite)
    {
        if (overwrite) File.Move(source, destination, overwrite: true);
        else if (!AtomicOutput.TryPublish(source, destination))
            throw new IOException($"The destination is already in use: {destination}");
    }

    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
}
