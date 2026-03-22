using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Models;

namespace PulseAPK.Core.Services.Patching;

public sealed class GadgetInjectionService : IGadgetInjectionService
{
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
            File.Copy(gadgetSourcePath, Path.Combine(libDirectory, "libfrida-gadget.so"), overwrite: true);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new GadgetInjectionResult(
                Success: false,
                Error: $"Failed to copy gadget binary: {ex.Message}",
                ScriptStatus: new OptionalAssetCopyResult(OptionalAssetCopyStatus.Skipped, "Script copy skipped because gadget copy failed."),
                ConfigStatus: new OptionalAssetCopyResult(OptionalAssetCopyStatus.Skipped, "Config copy skipped because gadget copy failed.")));
        }

        var configStatus = EnsureOptionalAsset(request.ConfigFilePath, decompiledDirectory, "frida-gadget.config", "config");
        var scriptStatus = EnsureOptionalAsset(request.ScriptFilePath, decompiledDirectory, "frida-script.js", "script");

        var hasAssetCopyError = configStatus.Status == OptionalAssetCopyStatus.Error || scriptStatus.Status == OptionalAssetCopyStatus.Error;
        var error = hasAssetCopyError
            ? $"Optional asset copy failed. Script: {scriptStatus.Detail}; Config: {configStatus.Detail}"
            : null;

        return Task.FromResult(new GadgetInjectionResult(
            Success: !hasAssetCopyError,
            Error: error,
            ScriptStatus: scriptStatus,
            ConfigStatus: configStatus));
    }

    private static OptionalAssetCopyResult EnsureOptionalAsset(string? sourceFile, string decompiledDirectory, string outputName, string assetLabel)
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
            var assetsDirectory = Path.Combine(decompiledDirectory, "assets");
            Directory.CreateDirectory(assetsDirectory);
            var destination = Path.Combine(assetsDirectory, outputName);
            File.Copy(sourceFile, destination, overwrite: true);
            return new OptionalAssetCopyResult(OptionalAssetCopyStatus.Copied, $"{assetLabel} copied to '{destination}'.");
        }
        catch (Exception ex)
        {
            return new OptionalAssetCopyResult(OptionalAssetCopyStatus.Error, $"{assetLabel} copy failed: {ex.Message}");
        }
    }
}
