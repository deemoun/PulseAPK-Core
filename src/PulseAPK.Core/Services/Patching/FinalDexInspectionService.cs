using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using PulseAPK.Core.Abstractions.Patching;

namespace PulseAPK.Core.Services.Patching;

public sealed class FinalDexInspectionService : IFinalDexInspectionService
{
    public async Task<bool> ContainsMethodReferenceAsync(string apkPath, string methodReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apkPath) || !File.Exists(apkPath))
        {
            return false;
        }

        if (!TryParseMethodReference(methodReference, out var classDescriptor, out var methodName, out var signature))
        {
            return false;
        }

        using var stream = File.OpenRead(apkPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        var dexEntries = archive.Entries
            .Where(entry => entry.FullName.StartsWith("classes", StringComparison.OrdinalIgnoreCase) &&
                            entry.FullName.EndsWith(".dex", StringComparison.OrdinalIgnoreCase));

        foreach (var dexEntry in dexEntries)
        {
            await using var dexStream = dexEntry.Open();
            using var buffer = new MemoryStream();
            await dexStream.CopyToAsync(buffer, cancellationToken);

            var dexText = Encoding.Latin1.GetString(buffer.ToArray());

            // DEX stores method components (class descriptor, name, proto/signature) as separate strings.
            // Searching for the full smali reference literal is unreliable in compiled binaries.
            var hasClassDescriptor = dexText.Contains(classDescriptor, StringComparison.Ordinal);
            var hasMethodName = dexText.Contains(methodName, StringComparison.Ordinal);
            var hasSignature = dexText.Contains(signature, StringComparison.Ordinal);

            if (hasClassDescriptor && hasMethodName && hasSignature)
            {
                return true;
            }
        }

        return false;
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
