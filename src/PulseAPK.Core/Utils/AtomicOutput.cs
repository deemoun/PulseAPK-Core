using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PulseAPK.Core.Utils;

internal static class AtomicOutput
{
    internal static async Task<string> WriteAsync(string destination, Func<Stream, Task> write, CancellationToken token = default)
    {
        var path = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".pulse-{Guid.NewGuid():N}.tmp");
        var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            using (output)
            {
                await write(output);
            }

            for (int suffix = 0; ; suffix++)
            {
                token.ThrowIfCancellationRequested();
                var candidate = suffix == 0 ? path : Path.Combine(directory,
                    $"{Path.GetFileNameWithoutExtension(path)}-{suffix}{Path.GetExtension(path)}");
                if (TryPublish(temporary, candidate)) return candidate;
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static bool TryPublish(string temporary, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                File.Move(temporary, destination);
                return true;
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 80 or 183)
            {
                return false;
            }
        }

        // The Unix implementation of File.Move can race another writer between checking and renaming.
        // Both paths share a directory. Exclusive rename or link publishes the complete file atomically.
        int status = OperatingSystem.IsMacOS()
            ? RenameExclusive(temporary, destination, 4 /* RENAME_EXCL */)
            : Link(temporary, destination);
        if (status == 0) return true;
        int error = Marshal.GetLastPInvokeError();
        if (error == 17 /* EEXIST */) return false;
        throw new IOException($"Cannot publish '{destination}': {new Win32Exception(error).Message}");
    }

    [DllImport("libc", EntryPoint = "renamex_np", SetLastError = true)]
    private static extern int RenameExclusive(string source, string destination, uint flags);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string source, string destination);
}
