using System.IO.Compression;
using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Models;

namespace PulseAPK.Core.Services.Patching;

public sealed class ArchitectureDetectionService : IArchitectureDetectionService
{
    private static readonly HashSet<string> SupportedArchitectures = new(StringComparer.OrdinalIgnoreCase)
    {
        "arm64-v8a", "armeabi-v7a", "x86", "x86_64"
    };

    public Task<(string? Architecture, string? Error, string? Warning)> ResolveAsync(PatchRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.SelectedArchitecture))
        {
            return Task.FromResult(SupportedArchitectures.Contains(request.SelectedArchitecture)
                ? (request.SelectedArchitecture, null, null)
                : (null, $"Unsupported architecture: {request.SelectedArchitecture}", null));
        }

        if (!string.IsNullOrWhiteSpace(request.DeviceAbi) && SupportedArchitectures.Contains(request.DeviceAbi))
        {
            return Task.FromResult<(string?, string?, string?)>((request.DeviceAbi, null, null));
        }

        if (!File.Exists(request.InputApkPath))
        {
            return Task.FromResult<(string?, string?, string?)>((null, "Input APK was not found for architecture scanning.", null));
        }

        using var archive = ZipFile.OpenRead(request.InputApkPath);
        var found = archive.Entries
            .Select(entry => entry.FullName)
            .Where(path => path.StartsWith("lib/", StringComparison.OrdinalIgnoreCase))
            .Select(path => path.Split('/'))
            .Where(parts => parts.Length >= 3)
            .Select(parts => parts[1])
            .FirstOrDefault(abi => SupportedArchitectures.Contains(abi));

        if (!string.IsNullOrWhiteSpace(found))
        {
            return Task.FromResult<(string?, string?, string?)>((found, null, null));
        }

        return Task.FromResult<(string?, string?, string?)>(("arm64-v8a", null, "No lib/<abi>/ entries were found in the APK. Falling back to arm64-v8a."));
    }
}
