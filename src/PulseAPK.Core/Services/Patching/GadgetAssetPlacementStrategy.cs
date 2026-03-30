using PulseAPK.Core.Models;

namespace PulseAPK.Core.Services.Patching;

internal enum GadgetScriptDestination
{
    AbiLibDirectory,
    AssetsDirectory
}

internal sealed record GadgetAssetPlacementPlan(
    GadgetScriptDestination ScriptDestination,
    string InteractionPath,
    string ScriptRelativeOutputPath,
    string ValidationDiagnostic,
    bool RequiresConfigRewrite)
{
    public bool IsSafeModeEnabled => ScriptDestination == GadgetScriptDestination.AssetsDirectory;
}

internal static class GadgetAssetPlacementStrategy
{
    internal const string LibScriptInteractionPath = "./libfrida-gadget.script.so";
    internal const string AssetScriptInteractionPath = "./assets/frida/libfrida-gadget.script.so";

    private const string ScriptFileName = "libfrida-gadget.script.so";

    public static GadgetAssetPlacementPlan Resolve(PatchRequest request)
    {
        var buildStrategy = request.UseAapt2ForBuild ? "apktool + aapt2" : "apktool default build";

        if (string.IsNullOrWhiteSpace(request.ScriptFilePath))
        {
            return new GadgetAssetPlacementPlan(
                ScriptDestination: GadgetScriptDestination.AbiLibDirectory,
                InteractionPath: LibScriptInteractionPath,
                ScriptRelativeOutputPath: Path.Combine("lib", "{abi}", ScriptFileName),
                ValidationDiagnostic: $"No script payload configured. Defaulting to ABI lib strategy for {buildStrategy}.",
                RequiresConfigRewrite: false);
        }

        if (!File.Exists(request.ScriptFilePath))
        {
            return new GadgetAssetPlacementPlan(
                ScriptDestination: GadgetScriptDestination.AbiLibDirectory,
                InteractionPath: LibScriptInteractionPath,
                ScriptRelativeOutputPath: Path.Combine("lib", "{abi}", ScriptFileName),
                ValidationDiagnostic: $"Script payload '{request.ScriptFilePath}' does not exist yet. Assuming ABI lib strategy for {buildStrategy} validation.",
                RequiresConfigRewrite: false);
        }

        if (!LooksLikeElf(request.ScriptFilePath))
        {
            return new GadgetAssetPlacementPlan(
                ScriptDestination: GadgetScriptDestination.AssetsDirectory,
                InteractionPath: AssetScriptInteractionPath,
                ScriptRelativeOutputPath: Path.Combine("assets", "frida", ScriptFileName),
                ValidationDiagnostic: $"Safe mode enabled: script payload is not ELF; using assets placement to avoid non-ELF under lib/<abi>/ during {buildStrategy}.",
                RequiresConfigRewrite: true);
        }

        return new GadgetAssetPlacementPlan(
            ScriptDestination: GadgetScriptDestination.AbiLibDirectory,
            InteractionPath: LibScriptInteractionPath,
            ScriptRelativeOutputPath: Path.Combine("lib", "{abi}", ScriptFileName),
            ValidationDiagnostic: $"Script payload appears to be ELF; ABI lib sidecar strategy is compatible with {buildStrategy}.",
            RequiresConfigRewrite: false);
    }

    public static IReadOnlyList<string> GetValidInteractionPaths(PatchRequest request)
    {
        var plan = Resolve(request);
        if (plan.ScriptDestination == GadgetScriptDestination.AssetsDirectory)
        {
            return [AssetScriptInteractionPath];
        }

        return [LibScriptInteractionPath, AssetScriptInteractionPath];
    }

    private static bool LooksLikeElf(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[4];
            using var stream = File.OpenRead(path);
            if (stream.Read(header) != header.Length)
            {
                return false;
            }

            return header[0] == 0x7F && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F';
        }
        catch
        {
            return false;
        }
    }
}
