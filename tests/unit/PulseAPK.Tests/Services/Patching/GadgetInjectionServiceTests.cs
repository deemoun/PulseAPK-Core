using PulseAPK.Core.Models;
using PulseAPK.Core.Services.Patching;

namespace PulseAPK.Tests.Services.Patching;

public class GadgetInjectionServiceTests
{
    [Fact]
    public async Task InjectAsync_CopiesGadgetAndConfigToAbiFolder_AndScriptToAssetsDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gadget-injection-{Guid.NewGuid():N}");
        var gadgetPath = Path.Combine(root, "libfrida-gadget.so");
        var configPath = Path.Combine(root, "libfrida-gadget.config.so");
        var scriptPath = Path.Combine(root, "script.js");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(gadgetPath, "gadget");
        await File.WriteAllTextAsync(configPath, "config");
        await File.WriteAllTextAsync(scriptPath, "script");

        var decompiled = Path.Combine(root, "decompiled");
        Directory.CreateDirectory(decompiled);

        var service = new GadgetInjectionService();
        var result = await service.InjectAsync(
            decompiled,
            new PatchRequest
            {
                ConfigFilePath = configPath,
                ScriptFilePath = scriptPath
            },
            "arm64-v8a",
            gadgetPath);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(decompiled, "lib", "arm64-v8a", "libfrida-gadget.so")));
        Assert.True(File.Exists(Path.Combine(decompiled, "lib", "arm64-v8a", "libfrida-gadget.config.so")));
        Assert.True(File.Exists(Path.Combine(decompiled, "assets", "frida-gadget", "script.js")));
    }

    [Fact]
    public async Task InjectAsync_SucceedsWhenOptionalAssetsAreNotConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gadget-injection-{Guid.NewGuid():N}");
        var gadgetPath = Path.Combine(root, "libfrida-gadget.so");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(gadgetPath, "gadget");

        var decompiled = Path.Combine(root, "decompiled");
        Directory.CreateDirectory(decompiled);

        var service = new GadgetInjectionService();
        var result = await service.InjectAsync(
            decompiled,
            new PatchRequest(),
            "arm64-v8a",
            gadgetPath);

        Assert.True(result.Success);
        Assert.Equal(OptionalAssetCopyStatus.Skipped, result.ScriptStatus.Status);
        Assert.Equal(OptionalAssetCopyStatus.Skipped, result.ConfigStatus.Status);
        Assert.True(File.Exists(Path.Combine(decompiled, "lib", "arm64-v8a", "libfrida-gadget.so")));
        Assert.False(File.Exists(Path.Combine(decompiled, "lib", "arm64-v8a", "libfrida-gadget.config.so")));
        Assert.False(File.Exists(Path.Combine(decompiled, "assets", "frida-gadget", "script.js")));
    }
}
