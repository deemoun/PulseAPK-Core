using System.IO.Compression;
using System.Reflection;
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
        var stringItems = GetObjectArray(dexFile, "StringIdItems")
            ?? throw new InvalidDataException("Dex string pool is missing.");

        return stringItems.Any(item =>
            GetStringMember(item, "StringData", "Value", "Data", "Text") is { } stringData &&
            stringData.Contains(markerLiteral, StringComparison.Ordinal));
    }

    private static object[]? GetObjectArray(object source, string memberName)
    {
        var value = GetMemberValue(source, memberName);
        return value switch
        {
            null => null,
            object[] array => array,
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().ToArray(),
            _ => throw new InvalidDataException($"Member '{memberName}' is not enumerable.")
        };
    }

    private static string? GetStringMember(object source, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var value = GetMemberValue(source, candidate);
            if (value is string stringValue)
            {
                return stringValue;
            }
        }

        return null;
    }

    private static object? GetMemberValue(object source, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var type = source.GetType();
        var property = type.GetProperty(name, flags);
        if (property is not null)
        {
            return property.GetValue(source);
        }

        var field = type.GetField(name, flags);
        if (field is not null)
        {
            return field.GetValue(source);
        }

        return null;
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
