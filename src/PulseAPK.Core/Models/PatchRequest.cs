using PulseAPK.Core.Abstractions.Patching;

namespace PulseAPK.Core.Models;

public sealed record PatchRequest
{
    public string InputApkPath { get; init; } = string.Empty;
    public string OutputApkPath { get; init; } = string.Empty;
    public ScriptInjectionProfile ScriptInjectionProfile { get; init; } = ScriptInjectionProfile.FridaGadget;
    public string? SelectedArchitecture { get; init; }
    public bool SignOutput { get; init; }
    public string? ConfigFilePath { get; init; }
    public string? ScriptFilePath { get; init; }
    public bool UseDelayedLoad { get; init; }
    public string? WorkingDirectory { get; init; }
    public bool KeepIntermediateFiles { get; init; }
    public bool PreserveOriginalDexFiles { get; init; } = true;
    public DexPreservationMode DexPreservationMode { get; init; } = DexPreservationMode.Disabled;
    public bool ConfirmDangerousDexReplacement { get; init; }
    public bool EnsureInternetPermission { get; init; } = true;
    public bool EnsureExtractNativeLibs { get; init; } = true;
    public string? PreferredActivityName { get; init; }
    public string? DeviceAbi { get; init; }
    public string? CustomGadgetBinaryPath { get; init; }
    public bool DecodeResources { get; init; } = true;
    public bool DecodeSources { get; init; } = true;
    public bool UseAapt2ForBuild { get; init; }
    public bool InjectForAllArchitectures { get; init; }
    public bool SkipDexValidation { get; init; }
}
