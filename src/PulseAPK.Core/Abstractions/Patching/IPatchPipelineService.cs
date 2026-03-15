using PulseAPK.Core.Models;

namespace PulseAPK.Core.Abstractions.Patching;

public interface IPatchPipelineService
{
    Task<PatchResult> RunAsync(PatchRequest request, CancellationToken cancellationToken = default);
}
