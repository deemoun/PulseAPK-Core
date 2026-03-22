namespace PulseAPK.Core.Models;

public enum OptionalAssetCopyStatus
{
    Copied,
    Skipped,
    Missing,
    Error
}

public sealed record OptionalAssetCopyResult(OptionalAssetCopyStatus Status, string Detail);

public sealed record GadgetInjectionResult(
    bool Success,
    string? Error,
    OptionalAssetCopyResult ScriptStatus,
    OptionalAssetCopyResult ConfigStatus);
