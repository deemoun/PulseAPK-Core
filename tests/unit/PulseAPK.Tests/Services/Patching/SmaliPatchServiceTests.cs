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

    [Fact]
    public async Task PatchAsync_ProducesDifferentInjection_ForDelayedMode()
    {
        var immediateRoot = Path.Combine(Path.GetTempPath(), $"smali-patch-immediate-{Guid.NewGuid():N}");
        var delayedRoot = Path.Combine(Path.GetTempPath(), $"smali-patch-delayed-{Guid.NewGuid():N}");
        var immediateSmaliPath = Path.Combine(immediateRoot, "smali", "com", "example");
        var delayedSmaliPath = Path.Combine(delayedRoot, "smali", "com", "example");
        Directory.CreateDirectory(immediateSmaliPath);
        Directory.CreateDirectory(delayedSmaliPath);

        var smaliContent = @".class public Lcom/example/MainActivity;
.super Landroid/app/Activity;

.method protected onCreate(Landroid/os/Bundle;)V
    .locals 0
    invoke-super {p0, p1}, Landroid/app/Activity;->onCreate(Landroid/os/Bundle;)V
    return-void
.end method

.end class";

        var immediateFile = Path.Combine(immediateSmaliPath, "MainActivity.smali");
        var delayedFile = Path.Combine(delayedSmaliPath, "MainActivity.smali");
        await File.WriteAllTextAsync(immediateFile, smaliContent);
        await File.WriteAllTextAsync(delayedFile, smaliContent);

        var service = new SmaliPatchService();

        var immediate = await service.PatchAsync(immediateRoot, "com.example.MainActivity", useDelayedLoad: false);
        var delayed = await service.PatchAsync(delayedRoot, "com.example.MainActivity", useDelayedLoad: true);

        var immediateOutput = await File.ReadAllTextAsync(immediateFile);
        var delayedOutput = await File.ReadAllTextAsync(delayedFile);

        Assert.True(immediate.Success);
        Assert.True(delayed.Success);
        Assert.NotEqual(immediateOutput, delayedOutput);
        Assert.Contains("invoke-static {}, Lcom/example/MainActivity;->loadFridaGadget()V", immediateOutput, StringComparison.Ordinal);
        Assert.Contains("invoke-static {}, Lcom/example/MainActivity;->loadFridaGadgetIfNeeded()V", delayedOutput, StringComparison.Ordinal);
        Assert.Contains(".method protected onResume()V", delayedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("loadFridaGadgetIfNeeded", immediateOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatchAsync_GeneratesLifecycleMethodUsingParsedAppCompatSuperclass()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smali-patch-appcompat-{Guid.NewGuid():N}");
        var smaliPath = Path.Combine(root, "smali", "com", "example");
        Directory.CreateDirectory(smaliPath);

        var file = Path.Combine(smaliPath, "MainActivity.smali");
        await File.WriteAllTextAsync(file, @".class public Lcom/example/MainActivity;
.super Landroidx/appcompat/app/AppCompatActivity;

.end class");

        var service = new SmaliPatchService();

        var result = await service.PatchAsync(root, "com.example.MainActivity", useDelayedLoad: false);
        var output = await File.ReadAllTextAsync(file);

        Assert.True(result.Success);
        Assert.Contains("invoke-super {p0, p1}, Landroidx/appcompat/app/AppCompatActivity;->onCreate(Landroid/os/Bundle;)V", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatchAsync_GeneratesLifecycleMethodUsingParsedCustomSuperclass()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smali-patch-custom-super-{Guid.NewGuid():N}");
        var smaliPath = Path.Combine(root, "smali", "com", "example");
        Directory.CreateDirectory(smaliPath);

        var file = Path.Combine(smaliPath, "MainActivity.smali");
        await File.WriteAllTextAsync(file, @".class public Lcom/example/MainActivity;
.super Lcom/example/BaseActivity;

.end class");

        var service = new SmaliPatchService();

        var result = await service.PatchAsync(root, "com.example.MainActivity", useDelayedLoad: true);
        var output = await File.ReadAllTextAsync(file);

        Assert.True(result.Success);
        Assert.Contains("invoke-super {p0}, Lcom/example/BaseActivity;->onResume()V", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatchAsync_FailsWhenSuperclassDescriptorCannotBeParsed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smali-patch-missing-super-{Guid.NewGuid():N}");
        var smaliPath = Path.Combine(root, "smali", "com", "example");
        Directory.CreateDirectory(smaliPath);

        var file = Path.Combine(smaliPath, "MainActivity.smali");
        await File.WriteAllTextAsync(file, @".class public Lcom/example/MainActivity;

.end class");

        var service = new SmaliPatchService();

        var result = await service.PatchAsync(root, "com.example.MainActivity", useDelayedLoad: false);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Unable to determine superclass descriptor", result.Error, StringComparison.Ordinal);
        Assert.Contains(file, result.Error, StringComparison.Ordinal);
    }
}
