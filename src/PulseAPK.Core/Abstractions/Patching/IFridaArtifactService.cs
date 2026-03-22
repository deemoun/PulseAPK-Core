using PulseAPK.Core.Models;

namespace PulseAPK.Core.Abstractions.Patching;

public interface IFridaArtifactService
{
    Task<(string? GadgetPath, string? Error)> ResolveGadgetAsync(PatchRequest request, string architecture, CancellationToken cancellationToken = default);
}
