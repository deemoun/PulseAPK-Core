using PulseAPK.Core.Models;

namespace PulseAPK.Core.Abstractions.Patching;

public interface IGadgetInjectionService
{
    Task<(bool Success, string? Error)> InjectAsync(string decompiledDirectory, PatchRequest request, string architecture, string gadgetSourcePath, CancellationToken cancellationToken = default);
}
