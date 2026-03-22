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
            var libDirectory = Path.Combine(decompiledDirectory, "lib", architecture);
            Directory.CreateDirectory(libDirectory);

            File.Copy(gadgetSourcePath, Path.Combine(libDirectory, GadgetFileName), overwrite: true);

            var configStatus = EnsureRequiredAsset(request.ConfigFilePath, libDirectory, ConfigFileName, "config");
            var scriptStatus = EnsureRequiredAsset(request.ScriptFilePath, libDirectory, ScriptFileName, "script");

            var hasAssetCopyError = configStatus.Status == OptionalAssetCopyStatus.Error;
            hasAssetCopyError |= scriptStatus.Status == OptionalAssetCopyStatus.Error;

            var error = hasAssetCopyError
                ? $"Required asset copy failed. Script: {scriptStatus.Detail}; Config: {configStatus.Detail}"
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

    private static OptionalAssetCopyResult EnsureRequiredAsset(string? sourceFile, string abiDirectory, string outputName, string assetLabel)
    {
        if (string.IsNullOrWhiteSpace(sourceFile))
        {
            return new OptionalAssetCopyResult(OptionalAssetCopyStatus.Error, $"{assetLabel} path not configured.");
        }

        if (!File.Exists(sourceFile))
        {
            return new OptionalAssetCopyResult(OptionalAssetCopyStatus.Error, $"{assetLabel} source '{sourceFile}' not found.");
        }

        try
        {
            var destination = Path.Combine(abiDirectory, outputName);
            File.Copy(sourceFile, destination, overwrite: true);
            return new OptionalAssetCopyResult(OptionalAssetCopyStatus.Copied, $"{assetLabel} copied to '{destination}'.");
        }
        catch (Exception ex)
        {
            return new OptionalAssetCopyResult(OptionalAssetCopyStatus.Error, $"{assetLabel} copy failed: {ex.Message}");
        }
    }
}
