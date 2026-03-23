using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Models;

namespace PulseAPK.Core.Services.Patching;

public sealed class GadgetInjectionService : IGadgetInjectionService
{
    private const string GadgetFileName = "libfrida-gadget.so";
    private const string ConfigFileName = "libfrida-gadget.config.so";
    private const string ScriptTargetDirectory = "assets/frida-gadget";
    private const string ScriptFileName = "script.js";

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

            var configStatus = EnsureOptionalAsset(request.ConfigFilePath, libDirectory, ConfigFileName, "config");
            var scriptOutputPath = Path.Combine(decompiledDirectory, ScriptTargetDirectory, ScriptFileName);
            var scriptStatus = EnsureOptionalAsset(request.ScriptFilePath, scriptOutputPath, "script");

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

    private static OptionalAssetCopyResult EnsureOptionalAsset(string? sourceFile, string abiDirectory, string outputName, string assetLabel)
    {
        var destinationPath = Path.Combine(abiDirectory, outputName);
        return EnsureOptionalAsset(sourceFile, destinationPath, assetLabel);
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
