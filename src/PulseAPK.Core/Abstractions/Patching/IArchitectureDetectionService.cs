using PulseAPK.Core.Models;

namespace PulseAPK.Core.Abstractions.Patching;

public interface IArchitectureDetectionService
{
    Task<(string? Architecture, string? Error, string? Warning)> ResolveAsync(PatchRequest request, CancellationToken cancellationToken = default);
}
