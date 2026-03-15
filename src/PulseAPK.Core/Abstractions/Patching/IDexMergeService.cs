namespace PulseAPK.Core.Abstractions.Patching;

public interface IDexMergeService
{
    Task<(bool Success, string? Error)> PreserveOriginalDexFilesAsync(string originalApkPath, string rebuiltApkPath, CancellationToken cancellationToken = default);
}
