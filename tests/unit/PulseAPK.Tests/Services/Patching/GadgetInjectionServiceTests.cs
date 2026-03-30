using System.Text.Json;
using PulseAPK.Core.Models;
using PulseAPK.Core.Services.Patching;

namespace PulseAPK.Tests.Services.Patching;

public class GadgetInjectionServiceTests
{
    [Fact]
    public async Task InjectAsync_CopiesGadgetConfigAndScriptToAbiFolder_WhenScriptLooksLikeElf()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gadget-injection-{Guid.NewGuid():N}");
        var gadgetPath = Path.Combine(root, "libfrida-gadget.so");
        var configPath = Path.Combine(root, "libfrida-gadget.config.so");
        var scriptPath = Path.Combine(root, "script.so");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(gadgetPath, "gadget");
        await File.WriteAllTextAsync(configPath, """
{
  "interaction": {
    "type": "script",
    "path": "./libfrida-gadget.script.so"
  }
}
""");
        await File.WriteAllBytesAsync(scriptPath, [0x7F, (byte)'E', (byte)'L', (byte)'F', 1, 1, 1, 1]);

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
        Assert.True(File.Exists(Path.Combine(decompiled, "lib", "arm64-v8a", "libfrida-gadget.script.so")));
        Assert.False(File.Exists(Path.Combine(decompiled, "assets", "frida", "libfrida-gadget.script.so")));
    }

    [Fact]
    public async Task InjectAsync_UsesSafeModeForNonElfScriptAndRewritesInteractionPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gadget-injection-{Guid.NewGuid():N}");
        var gadgetPath = Path.Combine(root, "libfrida-gadget.so");
        var configPath = Path.Combine(root, "libfrida-gadget.config.so");
        var scriptPath = Path.Combine(root, "script.js");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(gadgetPath, "gadget");
        await File.WriteAllTextAsync(configPath, """
{
  "interaction": {
    "type": "script",
    "path": "./libfrida-gadget.script.so"
  }
}
""");
        await File.WriteAllTextAsync(scriptPath, "console.log('safe mode');");

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
        Assert.True(File.Exists(Path.Combine(decompiled, "assets", "frida", "libfrida-gadget.script.so")));
        Assert.False(File.Exists(Path.Combine(decompiled, "lib", "arm64-v8a", "libfrida-gadget.script.so")));

        var copiedConfigPath = Path.Combine(decompiled, "lib", "arm64-v8a", "libfrida-gadget.config.so");
        var copiedConfig = JsonDocument.Parse(await File.ReadAllTextAsync(copiedConfigPath));
        Assert.Equal("./assets/frida/libfrida-gadget.script.so", copiedConfig.RootElement.GetProperty("interaction").GetProperty("path").GetString());
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
        Assert.False(File.Exists(Path.Combine(decompiled, "lib", "arm64-v8a", "libfrida-gadget.script.so")));
    }
}
