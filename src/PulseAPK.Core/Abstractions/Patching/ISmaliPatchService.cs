using PulseAPK.Core.Models;

namespace PulseAPK.Core.Abstractions.Patching;

public interface ISmaliPatchService
{
    Task<(bool Success, string? Error)> PatchAsync(
        string decompiledDirectory,
        string activityName,
        ScriptInjectionProfile profile,
        bool useDelayedLoad,
        CancellationToken cancellationToken = default);
}
