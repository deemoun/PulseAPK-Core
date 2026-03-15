using System.IO.Compression;
using PulseAPK.Core.Abstractions.Patching;

namespace PulseAPK.Core.Services.Patching;

public sealed class DexMergeService : IDexMergeService
{
    public Task<(bool Success, string? Error)> PreserveOriginalDexFilesAsync(string originalApkPath, string rebuiltApkPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(originalApkPath) || !File.Exists(rebuiltApkPath))
        {
            return Task.FromResult<(bool Success, string? Error)>((false, "Original or rebuilt APK path does not exist."));
        }

        using var original = ZipFile.OpenRead(originalApkPath);
        using var rebuilt = ZipFile.Open(rebuiltApkPath, ZipArchiveMode.Update);

        var rebuiltDex = rebuilt.Entries.Where(entry => entry.FullName.StartsWith("classes", StringComparison.OrdinalIgnoreCase) && entry.FullName.EndsWith(".dex", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var dex in rebuiltDex)
        {
            dex.Delete();
        }

        var sourceDexEntries = original.Entries
            .Where(entry => entry.FullName.StartsWith("classes", StringComparison.OrdinalIgnoreCase) && entry.FullName.EndsWith(".dex", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var source in sourceDexEntries)
        {
            var target = rebuilt.CreateEntry(source.FullName, CompressionLevel.Optimal);
            using var input = source.Open();
            using var output = target.Open();
            input.CopyTo(output);
        }

        return Task.FromResult<(bool Success, string? Error)>((true, null));
    }
}
