namespace PulseAPK.Core.Abstractions.Patching;

public interface ISmaliPatchService
{
    Task<(bool Success, string? Error)> PatchAsync(string decompiledDirectory, string activityName, bool useDelayedLoad, CancellationToken cancellationToken = default);
}
