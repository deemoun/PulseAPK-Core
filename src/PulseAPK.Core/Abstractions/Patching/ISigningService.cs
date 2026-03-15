namespace PulseAPK.Core.Abstractions.Patching;

public interface ISigningService
{
    Task<(bool Success, string? SignedApkPath, string? Error)> SignAsync(string inputApkPath, string outputApkPath, CancellationToken cancellationToken = default);
}
