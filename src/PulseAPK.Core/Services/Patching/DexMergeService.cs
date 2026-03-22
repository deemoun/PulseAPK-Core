using System.IO.Compression;
using PulseAPK.Core.Abstractions.Patching;

namespace PulseAPK.Core.Services.Patching;

public sealed class DexMergeService : IDexMergeService
{
    public Task<(bool Success, string? Error)> PreserveOriginalDexFilesAsync(string originalApkPath, string rebuiltApkPath, DexPreservationMode mode = DexPreservationMode.PreserveUnmodifiedSecondaryDexFiles, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(originalApkPath) || !File.Exists(rebuiltApkPath))
        {
            return Task.FromResult<(bool Success, string? Error)>((false, "Original or rebuilt APK path does not exist."));
        }

        using var original = ZipFile.OpenRead(originalApkPath);
        using var rebuilt = ZipFile.Open(rebuiltApkPath, ZipArchiveMode.Update);

        var sourceDexEntries = original.Entries
            .Where(entry => entry.FullName.StartsWith("classes", StringComparison.OrdinalIgnoreCase) && entry.FullName.EndsWith(".dex", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        switch (mode)
        {
            case DexPreservationMode.Disabled:
                return Task.FromResult<(bool Success, string? Error)>((true, null));
            case DexPreservationMode.ReplaceAllDexFiles:
                ReplaceAllDexFiles(rebuilt, sourceDexEntries);
                break;
            case DexPreservationMode.PreserveUnmodifiedSecondaryDexFiles:
                PreserveUnmodifiedSecondaryDexFiles(rebuilt, sourceDexEntries);
                break;
            default:
                return Task.FromResult<(bool Success, string? Error)>((false, $"Unsupported dex preservation mode: {mode}."));
        }

        return Task.FromResult<(bool Success, string? Error)>((true, null));
    }


    private static void ReplaceAllDexFiles(ZipArchive rebuilt, IReadOnlyCollection<ZipArchiveEntry> sourceDexEntries)
    {
        var rebuiltDex = rebuilt.Entries
            .Where(entry => entry.FullName.StartsWith("classes", StringComparison.OrdinalIgnoreCase) && entry.FullName.EndsWith(".dex", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var dex in rebuiltDex)
        {
            dex.Delete();
        }

        foreach (var source in sourceDexEntries)
        {
            var target = rebuilt.CreateEntry(source.FullName, CompressionLevel.Optimal);
            using var input = source.Open();
            using var output = target.Open();
            input.CopyTo(output);
        }
    }

    private static void PreserveUnmodifiedSecondaryDexFiles(ZipArchive rebuilt, IReadOnlyCollection<ZipArchiveEntry> sourceDexEntries)
    {
        foreach (var source in sourceDexEntries)
        {
            if (string.Equals(source.FullName, "classes.dex", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var existing = rebuilt.GetEntry(source.FullName);
            if (existing is not null)
            {
                continue;
            }

            var target = rebuilt.CreateEntry(source.FullName, CompressionLevel.Optimal);
            using var input = source.Open();
            using var output = target.Open();
            input.CopyTo(output);
        }
    }

}
