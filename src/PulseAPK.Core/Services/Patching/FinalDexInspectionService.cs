using System.IO.Compression;
using System.Text.RegularExpressions;
using PulseAPK.Core.Abstractions.Patching;

namespace PulseAPK.Core.Services.Patching;

public sealed class FinalDexInspectionService : IFinalDexInspectionService
{
    private readonly IDexMethodLookupService _dexMethodLookupService;

    public FinalDexInspectionService()
        : this(new DexMethodLookupService())
    {
    }

    public FinalDexInspectionService(IDexMethodLookupService dexMethodLookupService)
    {
        ArgumentNullException.ThrowIfNull(dexMethodLookupService);
        _dexMethodLookupService = dexMethodLookupService;
    }

    public async Task<(bool Found, string Diagnostics)> ContainsMethodReferenceAsync(string apkPath, string methodReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apkPath) || !File.Exists(apkPath))
        {
            return (false, $"APK path is missing or file does not exist: '{apkPath}'.");
        }

        if (!TryParseMethodReference(methodReference, out var classDescriptor, out var methodName, out var signature))
        {
            return (false, $"Method reference '{methodReference}' is invalid.");
        }

        using var stream = File.OpenRead(apkPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        var dexEntries = archive.Entries
            .Where(entry => entry.FullName.StartsWith("classes", StringComparison.OrdinalIgnoreCase) &&
                            entry.FullName.EndsWith(".dex", StringComparison.OrdinalIgnoreCase));

        var totalDexEntries = 0;
        var successfulDexEntries = 0;
        var parseFailures = new List<string>();
        foreach (var dexEntry in dexEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            totalDexEntries++;
            await using var dexStream = dexEntry.Open();
            using var buffer = new MemoryStream();
            await dexStream.CopyToAsync(buffer, cancellationToken);

            var dexData = buffer.ToArray();
            try
            {
                var found = _dexMethodLookupService.ContainsMethodReference(dexData, classDescriptor, methodName, signature);
                if (found)
                {
                    return (true, $"Found in '{dexEntry.FullName}' ({dexData.Length} bytes).");
                }

                successfulDexEntries++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                parseFailures.Add($"warning '{dexEntry.FullName}': {ex.Message}");
            }
        }

        if (totalDexEntries == 0)
        {
            return (false, "No classes*.dex entries were found in the APK.");
        }

        if (successfulDexEntries > 0)
        {
            if (parseFailures.Count == 0)
            {
                return (false, $"Method tuple not found in any of the {totalDexEntries} dex entries.");
            }

            return (false,
                $"Method tuple not found in any of the {totalDexEntries} dex entries. " +
                $"Non-fatal parse failures: {string.Join("; ", parseFailures)}");
        }

        return (false,
            $"Inspection failed for all {totalDexEntries} dex entries. " +
            $"Parse failures: {string.Join("; ", parseFailures)}");
    }

    private static bool TryParseMethodReference(string methodReference, out string classDescriptor, out string methodName, out string signature)
    {
        classDescriptor = string.Empty;
        methodName = string.Empty;
        signature = string.Empty;

        if (string.IsNullOrWhiteSpace(methodReference))
        {
            return false;
        }

        var match = Regex.Match(methodReference, "^(L[^;]+;)->([^(]+)(\\(.*\\).+)$");
        if (!match.Success)
        {
            return false;
        }

        classDescriptor = match.Groups[1].Value;
        methodName = match.Groups[2].Value;
        signature = match.Groups[3].Value;

        return true;
    }
}
