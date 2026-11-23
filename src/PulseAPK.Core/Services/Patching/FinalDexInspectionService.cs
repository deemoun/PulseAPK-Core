using System.IO.Compression;
using System.Text.RegularExpressions;
using AlphaOmega.Debug;
using AlphaOmega.Debug.Dex;
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

    public async Task<(bool Found, string Diagnostics)> ContainsStringMarkerAsync(string apkPath, string markerLiteral, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apkPath) || !File.Exists(apkPath))
        {
            return (false, $"APK path is missing or file does not exist: '{apkPath}'.");
        }

        if (string.IsNullOrWhiteSpace(markerLiteral))
        {
            return (false, "Marker literal is required.");
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
                var found = ContainsStringInDexStringPool(dexData, markerLiteral);
                if (found)
                {
                    return (true, $"Marker literal '{markerLiteral}' found in '{dexEntry.FullName}' ({dexData.Length} bytes).");
                }

                successfulDexEntries++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (ContainsStringInRawDexBytes(dexData, markerLiteral))
                {
                    return (true,
                        $"Marker literal '{markerLiteral}' found in '{dexEntry.FullName}' ({dexData.Length} bytes) via raw byte scan fallback.");
                }

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
                return (false, $"Marker literal '{markerLiteral}' not found in any of the {totalDexEntries} dex entries.");
            }

            return (false,
                $"Marker literal '{markerLiteral}' not found in any of the {totalDexEntries} dex entries. " +
                $"Non-fatal parse failures: {string.Join("; ", parseFailures)}");
        }

        return (false,
            $"Marker inspection failed for all {totalDexEntries} dex entries. " +
            $"Parse failures: {string.Join("; ", parseFailures)}");
    }

    private static bool ContainsStringInDexStringPool(byte[] dexData, string markerLiteral)
    {
        using var stream = new MemoryStream(dexData, writable: false);
        using var streamLoader = new StreamLoader(stream);
        using var dexFile = new DexFile(streamLoader);
        // DEX stores UTF-16 code units in modified UTF-8, including surrogate pairs.
        using var encoded = new MemoryStream();
        foreach (var c in markerLiteral)
        {
            if (c is > (char)0 and <= (char)0x7f)
            {
                encoded.WriteByte((byte)c);
            }
            else
            {
                if (c > 0x7ff)
                {
                    encoded.WriteByte((byte)(0xe0 | (c >> 12)));
                    encoded.WriteByte((byte)(0x80 | ((c >> 6) & 0x3f)));
                }
                else
                {
                    encoded.WriteByte((byte)(0xc0 | (c >> 6)));
                }
                encoded.WriteByte((byte)(0x80 | (c & 0x3f)));
            }
        }

        var marker = encoded.ToArray();
        foreach (var id in dexFile.STRING_ID_ITEM)
        {
            var offset = checked((int)id.string_data_off);
            var data = dexData.AsSpan(offset);
            // Skip the ULEB128 UTF-16 length before searching the terminated payload.
            var prefix = 0;
            while (prefix < data.Length && prefix < 5 && (data[prefix] & 0x80) != 0)
                prefix++;
            if (prefix >= data.Length || prefix >= 5)
                throw new InvalidDataException("Invalid DEX string length.");
            var payload = data[(prefix + 1)..];
            var end = payload.IndexOf((byte)0);
            if (end < 0)
                throw new InvalidDataException("Unterminated DEX string.");
            if (payload[..end].IndexOf(marker) >= 0)
                return true;
        }

        return false;
    }

    private static bool ContainsStringInRawDexBytes(byte[] dexData, string markerLiteral)
    {
        if (dexData.Length == 0 || string.IsNullOrEmpty(markerLiteral))
        {
            return false;
        }

        var markerBytes = System.Text.Encoding.UTF8.GetBytes(markerLiteral);
        return markerBytes.Length > 0 && dexData.AsSpan().IndexOf(markerBytes) >= 0;
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
