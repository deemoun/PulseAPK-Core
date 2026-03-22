using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Models;

namespace PulseAPK.Core.Services.Patching;

public sealed class GadgetInjectionService : IGadgetInjectionService
{
    public Task<(bool Success, string? Error)> InjectAsync(string decompiledDirectory, PatchRequest request, string architecture, string gadgetSourcePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(gadgetSourcePath))
        {
            return Task.FromResult<(bool Success, string? Error)>((false, $"Resolved gadget source '{gadgetSourcePath}' does not exist."));
        }

        var libDirectory = Path.Combine(decompiledDirectory, "lib", architecture);
        Directory.CreateDirectory(libDirectory);
        File.Copy(gadgetSourcePath, Path.Combine(libDirectory, "libfrida-gadget.so"), overwrite: true);

        EnsureOptionalAsset(request.ConfigFilePath, decompiledDirectory, "frida-gadget.config");
        EnsureOptionalAsset(request.ScriptFilePath, decompiledDirectory, "frida-script.js");

        return Task.FromResult<(bool Success, string? Error)>((true, null));
    }

    private static void EnsureOptionalAsset(string? sourceFile, string decompiledDirectory, string outputName)
    {
        if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
        {
            return;
        }

        var assetsDirectory = Path.Combine(decompiledDirectory, "assets");
        Directory.CreateDirectory(assetsDirectory);
        File.Copy(sourceFile, Path.Combine(assetsDirectory, outputName), overwrite: true);
    }
}
