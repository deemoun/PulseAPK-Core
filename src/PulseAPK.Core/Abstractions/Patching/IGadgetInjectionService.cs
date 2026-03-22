using PulseAPK.Core.Models;

namespace PulseAPK.Core.Abstractions.Patching;

public interface IGadgetInjectionService
{
    Task<GadgetInjectionResult> InjectAsync(string decompiledDirectory, PatchRequest request, string architecture, string gadgetSourcePath, CancellationToken cancellationToken = default);
}
