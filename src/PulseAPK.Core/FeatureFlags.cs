namespace PulseAPK.Core;

/// <summary>
/// Controls which top-level features are available in the application shell.
/// Set a flag to false to hide its left-menu button and prevent startup/navigation
/// from opening that feature.
/// </summary>
public static class FeatureFlags
{
    public const bool Decompile = true;
    public const bool BuildApk = true;
    public const bool PatchApk = true;
    public const bool ApkAnalyser = true;
    public const bool DeviceTools = true;
    public const bool Settings = true;
    public const bool About = true;
}
