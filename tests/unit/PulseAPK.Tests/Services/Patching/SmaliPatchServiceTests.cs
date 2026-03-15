using PulseAPK.Core.Services.Patching;

namespace PulseAPK.Tests.Services.Patching;

public class SmaliPatchServiceTests
{
    [Fact]
    public async Task PatchAsync_IsIdempotent_ForRepeatedPatches()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smali-patch-{Guid.NewGuid():N}");
        var smaliPath = Path.Combine(root, "smali", "com", "example");
        Directory.CreateDirectory(smaliPath);

        var file = Path.Combine(smaliPath, "MainActivity.smali");
        await File.WriteAllTextAsync(file, @".class public Lcom/example/MainActivity;
.super Landroid/app/Activity;

.method protected onCreate(Landroid/os/Bundle;)V
    .locals 0
    invoke-super {p0, p1}, Landroid/app/Activity;->onCreate(Landroid/os/Bundle;)V
    return-void
.end method

.end class");

        var service = new SmaliPatchService();

        var first = await service.PatchAsync(root, "com.example.MainActivity", useDelayedLoad: false);
        var firstContent = await File.ReadAllTextAsync(file);
        var second = await service.PatchAsync(root, "com.example.MainActivity", useDelayedLoad: false);
        var secondContent = await File.ReadAllTextAsync(file);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(firstContent, secondContent);
        Assert.Contains("loadFridaGadget", secondContent, StringComparison.Ordinal);
    }
}
