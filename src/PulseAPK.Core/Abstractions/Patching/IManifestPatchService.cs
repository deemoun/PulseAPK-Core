using PulseAPK.Core.Models;

namespace PulseAPK.Core.Abstractions.Patching;

public interface IManifestPatchService
{
    Task<(bool Success, string? Error)> PatchAsync(string manifestPath, PatchRequest request, CancellationToken cancellationToken = default);
}
