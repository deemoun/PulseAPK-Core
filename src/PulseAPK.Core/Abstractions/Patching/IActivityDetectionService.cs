namespace PulseAPK.Core.Abstractions.Patching;

public interface IActivityDetectionService
{
    Task<(string? ActivityName, string? Warning, string? Error)> DetectMainActivityAsync(string decompiledDirectory, CancellationToken cancellationToken = default);
}
