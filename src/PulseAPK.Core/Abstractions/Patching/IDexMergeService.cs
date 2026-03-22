namespace PulseAPK.Core.Abstractions.Patching;

public enum DexPreservationMode
{
    Disabled,
    ReplaceAllDexFiles,
    PreserveUnmodifiedSecondaryDexFiles
}

public interface IDexMergeService
{
    Task<(bool Success, string? Error)> PreserveOriginalDexFilesAsync(
        string originalApkPath,
        string rebuiltApkPath,
        DexPreservationMode mode = DexPreservationMode.PreserveUnmodifiedSecondaryDexFiles,
        CancellationToken cancellationToken = default);
}
