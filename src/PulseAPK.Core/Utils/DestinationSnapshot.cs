using System.Security.Cryptography;
using System.Text;

namespace PulseAPK.Core.Utils;

internal sealed record DestinationSnapshot(string? Fingerprint)
{
    public static Task<DestinationSnapshot> CaptureAsync(string path, CancellationToken token) =>
        Task.Run(() => Capture(path, token), token);

    private static DestinationSnapshot Capture(string path, CancellationToken token)
    {
        if (!Path.Exists(path)) return new(Fingerprint: null);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Field(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
        void Visit(string entry)
        {
            token.ThrowIfCancellationRequested();
            var attrs = File.GetAttributes(entry);
            Field(Path.GetRelativePath(path, entry));
            Field((attrs & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.ReadOnly)).ToString());
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                Field(new FileInfo(entry).LinkTarget ?? throw new IOException($"Cannot inspect link: {entry}"));
            }
            else if ((attrs & FileAttributes.Directory) != 0)
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(entry).Order(StringComparer.Ordinal)) Visit(child);
            }
            else
            {
                using var input = File.OpenRead(entry);
                hash.AppendData(SHA256.HashData(input));
            }
        }
        Visit(path);
        return new(Convert.ToHexString(hash.GetHashAndReset()));
    }

    public async Task<string> PublishAsync(string staged, string destination, bool directory,
        Func<string, Task<bool>> offerCopy, CancellationToken token)
    {
        void Move(string from, string to)
        {
            if (directory) Directory.Move(from, to);
            else
            {
                OutputWorkspace.PublishFile(from, to, false);
                if (OperatingSystem.IsLinux()) File.Delete(from);
            }
        }

        // Compare the displaced original before discarding it. Keep the backup outside the disposable job.
        if (Fingerprint != null && Path.Exists(destination) && Directory.Exists(destination) == directory
            && this == await CaptureAsync(destination, token))
        {
            var backup = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!, $".pulse-backup-{Guid.NewGuid():N}");
            Move(destination, backup);
            try
            {
                if (this == await CaptureAsync(backup, token))
                {
                    token.ThrowIfCancellationRequested();
                    Move(staged, destination);
                    if (directory) Directory.Delete(backup, true); else File.Delete(backup);
                    return destination;
                }
            }
            finally
            {
                if (Path.Exists(backup))
                {
                    if (Path.Exists(destination)) throw new IOException($"Original output is preserved at {backup}; the destination is occupied.");
                    Move(backup, destination);
                }
            }
        }
        else if (Fingerprint == null && !Path.Exists(destination))
        {
            token.ThrowIfCancellationRequested();
            try { Move(staged, destination); return destination; }
            catch (IOException) when (Path.Exists(destination)) { }
        }
        if (!await offerCopy($"The destination changed during this run. Save the completed output beside '{destination}' under a new name?"))
            throw new IOException("Output was not published. The changed destination was preserved.");
        var parent = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        var stem = directory ? Path.GetFileName(destination) : Path.GetFileNameWithoutExtension(destination);
        var extension = directory ? "" : Path.GetExtension(destination);
        for (int i = 1; ; i++)
        {
            token.ThrowIfCancellationRequested();
            var copy = Path.Combine(parent, $"{stem}-{i}{extension}");
            try { Move(staged, copy); return copy; }
            catch (IOException) when (Path.Exists(copy)) { }
        }
    }
}
