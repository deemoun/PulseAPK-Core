using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Models;

namespace PulseAPK.Core.Services.Patching;

public sealed class GadgetInjectionService : IGadgetInjectionService
{
    private const string GadgetFileName = "libfrida-gadget.so";
    private const string ConfigFileName = "libfrida-gadget.config.so";
    private const string ScriptFileName = "libfrida-gadget.script.so";

    public Task<GadgetInjectionResult> InjectAsync(string decompiledDirectory, PatchRequest request, string architecture, string gadgetSourcePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(gadgetSourcePath))
        {
            return Task.FromResult(new GadgetInjectionResult(
                Success: false,
                Error: $"Resolved gadget source '{gadgetSourcePath}' does not exist.",
                ScriptStatus: new OptionalAssetCopyResult(OptionalAssetCopyStatus.Skipped, "Script copy skipped because gadget injection failed."),
                ConfigStatus: new OptionalAssetCopyResult(OptionalAssetCopyStatus.Skipped, "Config copy skipped because gadget injection failed.")));
        }

        try
        {
            var placementPlan = GadgetAssetPlacementStrategy.Resolve(request);
            var libDirectory = Path.Combine(decompiledDirectory, "lib", architecture);
            Directory.CreateDirectory(libDirectory);

            File.Copy(gadgetSourcePath, Path.Combine(libDirectory, GadgetFileName), overwrite: true);

            var configStatus = EnsureConfigAsset(request.ConfigFilePath, libDirectory, placementPlan, request.ScriptInjectionProfile);
            var scriptStatus = EnsureScriptAsset(request.ScriptFilePath, decompiledDirectory, libDirectory, placementPlan);

            var hasAssetCopyError = configStatus.Status == OptionalAssetCopyStatus.Error;
            hasAssetCopyError |= scriptStatus.Status == OptionalAssetCopyStatus.Error;

            var error = hasAssetCopyError
                ? $"Required asset copy failed. Strategy: {placementPlan.ValidationDiagnostic}. Script: {scriptStatus.Detail}; Config: {configStatus.Detail}"
                : null;

            return Task.FromResult(new GadgetInjectionResult(
                Success: !hasAssetCopyError,
                Error: error,
                ScriptStatus: scriptStatus,
                ConfigStatus: configStatus));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new GadgetInjectionResult(
                Success: false,
                Error: $"Failed to copy gadget binary: {ex.Message}",
                ScriptStatus: new OptionalAssetCopyResult(OptionalAssetCopyStatus.Skipped, "Script copy skipped because gadget copy failed."),
                ConfigStatus: new OptionalAssetCopyResult(OptionalAssetCopyStatus.Skipped, "Config copy skipped because gadget copy failed.")));
        }
    }

    private static OptionalAssetCopyResult EnsureScriptAsset(string? sourceFile, string decompiledDirectory, string abiDirectory, GadgetAssetPlacementPlan placementPlan)
    {
        if (placementPlan.ScriptDestination == GadgetScriptDestination.AssetsDirectory)
        {
            var destinationPath = Path.Combine(decompiledDirectory, "assets", "frida", ScriptFileName);
            var copyResult = EnsureOptionalAsset(sourceFile, destinationPath, "script");
            if (copyResult.Status != OptionalAssetCopyStatus.Copied)
            {
                return copyResult;
            }

            return copyResult with
            {
                Detail = $"{copyResult.Detail} {placementPlan.ValidationDiagnostic} interaction.path expects '{placementPlan.InteractionPath}'."
            };
        }

        var destination = Path.Combine(abiDirectory, ScriptFileName);
        var libCopyResult = EnsureOptionalAsset(sourceFile, destination, "script");
        return libCopyResult.Status == OptionalAssetCopyStatus.Copied
            ? libCopyResult with { Detail = $"{libCopyResult.Detail} {placementPlan.ValidationDiagnostic}" }
            : libCopyResult;
    }

    private static OptionalAssetCopyResult EnsureConfigAsset(
        string? sourceFile,
        string abiDirectory,
        GadgetAssetPlacementPlan placementPlan,
        ScriptInjectionProfile profile)
    {
        var destinationPath = Path.Combine(abiDirectory, ConfigFileName);
        if (string.IsNullOrWhiteSpace(sourceFile))
        {
            return new OptionalAssetCopyResult(OptionalAssetCopyStatus.Skipped, "config path not configured.");
        }

        if (!File.Exists(sourceFile))
        {
            return new OptionalAssetCopyResult(OptionalAssetCopyStatus.Missing, $"config source '{sourceFile}' not found.");
        }

        if (placementPlan.RequiresConfigRewrite)
        {
            var rewriteResult = TryRewriteInteractionPath(sourceFile, destinationPath, placementPlan.InteractionPath, profile);
            if (rewriteResult.Status != OptionalAssetCopyStatus.Copied)
            {
                return rewriteResult;
            }

            return rewriteResult with
            {
                Detail = $"{rewriteResult.Detail} {placementPlan.ValidationDiagnostic}"
            };
        }

        return EnsureOptionalAsset(sourceFile, destinationPath, "config");
    }

    private static OptionalAssetCopyResult TryRewriteInteractionPath(string sourceFile, string destinationPath, string interactionPath, ScriptInjectionProfile profile)
    {
        try
        {
            var configBytes = File.ReadAllBytes(sourceFile);
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(configBytes);
            var root = JsonNode.Parse(configBytes) as JsonObject;
            var interaction = root?["interaction"] as JsonObject;
            if (interaction is null)
            {
                return new OptionalAssetCopyResult(
                    OptionalAssetCopyStatus.Missing,
                    $"config is missing interaction.path. Profile '{profile}' requires '{ConfigFileName}' sidecar semantics; safe mode could not rewrite path to '{interactionPath}', which may break apktool build output.");
            }

            interaction["path"] = interactionPath;

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllText(destinationPath, root!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return new OptionalAssetCopyResult(OptionalAssetCopyStatus.Copied, $"config copied to '{destinationPath}' with interaction.path='{interactionPath}'.");
        }
        catch (Exception ex)
        {
            return new OptionalAssetCopyResult(
                OptionalAssetCopyStatus.Missing,
                $"config copy diagnostics: Profile '{profile}' requires '{ConfigFileName}' sidecar semantics but safe mode could not rewrite config for '{interactionPath}'. This optional payload location may break apktool build. {ex.Message}");
        }
    }

    private static OptionalAssetCopyResult EnsureOptionalAsset(string? sourceFile, string destination, string assetLabel)
    {
        if (string.IsNullOrWhiteSpace(sourceFile))
        {
            return new OptionalAssetCopyResult(OptionalAssetCopyStatus.Skipped, $"{assetLabel} path not configured.");
        }

        if (!File.Exists(sourceFile))
        {
            return new OptionalAssetCopyResult(OptionalAssetCopyStatus.Missing, $"{assetLabel} source '{sourceFile}' not found.");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourceFile, destination, overwrite: true);
            return new OptionalAssetCopyResult(OptionalAssetCopyStatus.Copied, $"{assetLabel} copied to '{destination}'.");
        }
        catch (Exception ex)
        {
            return new OptionalAssetCopyResult(OptionalAssetCopyStatus.Error, $"{assetLabel} copy failed: {ex.Message}");
        }
    }
}
