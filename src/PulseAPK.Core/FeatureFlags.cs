namespace PulseAPK.Core;

/// <summary>
/// Controls which top-level features are available in the application shell.
/// Set a flag to false to hide its left-menu button and prevent startup/navigation
/// from opening that feature.
/// </summary>
public static class FeatureFlags
{
    public static readonly bool Decompile = true;
    public static readonly bool BuildApk = true;
    public static readonly bool PatchApk = true;
    public static readonly bool ApkAnalyser = true;
    public static readonly bool DeviceTools = true;
    public static readonly bool Settings = true;
    public static readonly bool About = true;
}
