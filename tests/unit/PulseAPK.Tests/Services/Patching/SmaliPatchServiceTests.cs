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


    [Fact]
    public async Task PatchAsync_SelectsSmaliFileMatchingExpectedClassDescriptor_WhenDuplicateFileNamesExist()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smali-patch-duplicate-name-{Guid.NewGuid():N}");
        var expectedDir = Path.Combine(root, "smali", "com", "app", "damnvulnerablebank");
        var duplicateDir = Path.Combine(root, "smali_classes2", "com", "example");
        Directory.CreateDirectory(expectedDir);
        Directory.CreateDirectory(duplicateDir);

        var expectedFile = Path.Combine(expectedDir, "SplashScreen.smali");
        var duplicateFile = Path.Combine(duplicateDir, "SplashScreen.smali");

        await File.WriteAllTextAsync(expectedFile, @".class public Lcom/app/damnvulnerablebank/SplashScreen;
.super Landroid/app/Activity;

.method protected onCreate(Landroid/os/Bundle;)V
    .locals 0
    invoke-super {p0, p1}, Landroid/app/Activity;->onCreate(Landroid/os/Bundle;)V
    return-void
.end method

.end class");

        await File.WriteAllTextAsync(duplicateFile, @".class public Lcom/example/SplashScreen;
.super Landroid/app/Activity;

.method protected onCreate(Landroid/os/Bundle;)V
    .locals 0
    invoke-super {p0, p1}, Landroid/app/Activity;->onCreate(Landroid/os/Bundle;)V
    return-void
.end method

.end class");

        var service = new SmaliPatchService();

        var result = await service.PatchAsync(root, "com.app.damnvulnerablebank.SplashScreen", useDelayedLoad: false);
        var expectedOutput = await File.ReadAllTextAsync(expectedFile);
        var duplicateOutput = await File.ReadAllTextAsync(duplicateFile);

        Assert.True(result.Success);
        Assert.Contains("invoke-static {}, Lcom/app/damnvulnerablebank/SplashScreen;->loadFridaGadget()V", expectedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("loadFridaGadget", duplicateOutput, StringComparison.Ordinal);
    }


    [Fact]
    public async Task PatchAsync_InsertsHelperMethod_WhenEndClassDirectiveIsIndented()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smali-patch-indented-end-class-{Guid.NewGuid():N}");
        var smaliPath = Path.Combine(root, "smali", "com", "example");
        Directory.CreateDirectory(smaliPath);

        var file = Path.Combine(smaliPath, "SplashScreen.smali");
        await File.WriteAllTextAsync(file, @".class public Lcom/example/SplashScreen;
.super Landroid/app/Activity;

.method protected onCreate(Landroid/os/Bundle;)V
    .locals 0
    invoke-super {p0, p1}, Landroid/app/Activity;->onCreate(Landroid/os/Bundle;)V
    return-void
.end method

    .end class");

        var service = new SmaliPatchService();
        var result = await service.PatchAsync(root, "com.example.SplashScreen", useDelayedLoad: false);
        var output = await File.ReadAllTextAsync(file);

        Assert.True(result.Success);
        Assert.Contains(".method private static loadFridaGadget()V", output, StringComparison.Ordinal);
        Assert.Contains("invoke-static {}, Lcom/example/SplashScreen;->loadFridaGadget()V", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatchAsync_AddsMissingHelperMethodEvenWhenLibraryStringAlreadyExists()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smali-patch-existing-lib-string-{Guid.NewGuid():N}");
        var smaliPath = Path.Combine(root, "smali", "com", "example");
        Directory.CreateDirectory(smaliPath);

        var file = Path.Combine(smaliPath, "SplashScreen.smali");
        await File.WriteAllTextAsync(file, @".class public Lcom/example/SplashScreen;
.super Landroid/app/Activity;

.method protected onCreate(Landroid/os/Bundle;)V
    .locals 1
    invoke-super {p0, p1}, Landroid/app/Activity;->onCreate(Landroid/os/Bundle;)V
    const-string v0, ""frida-gadget""
    return-void
.end method

.end class");

        var service = new SmaliPatchService();
        var result = await service.PatchAsync(root, "com.example.SplashScreen", useDelayedLoad: false);
        var output = await File.ReadAllTextAsync(file);

        Assert.True(result.Success);
        Assert.Contains(".method private static loadFridaGadget()V", output, StringComparison.Ordinal);
        Assert.Contains("invoke-static {}, Lcom/example/SplashScreen;->loadFridaGadget()V", output, StringComparison.Ordinal);
    }
    [Fact]
    public async Task PatchAsync_DelayedMode_PreservesExistingImmediateInvocationByAddingHelperMethod()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smali-patch-delayed-existing-immediate-call-{Guid.NewGuid():N}");
        var smaliPath = Path.Combine(root, "smali", "com", "app", "damnvulnerablebank");
        Directory.CreateDirectory(smaliPath);

        var file = Path.Combine(smaliPath, "SplashScreen.smali");
        await File.WriteAllTextAsync(file, @".class public Lcom/app/damnvulnerablebank/SplashScreen;
.super Landroid/app/Activity;

.method protected onCreate(Landroid/os/Bundle;)V
    .locals 0
    invoke-super {p0, p1}, Landroid/app/Activity;->onCreate(Landroid/os/Bundle;)V
    invoke-static {}, Lcom/app/damnvulnerablebank/SplashScreen;->loadFridaGadget()V
    return-void
.end method

.end class");

        var service = new SmaliPatchService();
        var result = await service.PatchAsync(root, "com.app.damnvulnerablebank.SplashScreen", useDelayedLoad: true);
        var output = await File.ReadAllTextAsync(file);

        Assert.True(result.Success);
        Assert.Contains("invoke-static {}, Lcom/app/damnvulnerablebank/SplashScreen;->loadFridaGadget()V", output, StringComparison.Ordinal);
        Assert.Contains(".method private static loadFridaGadget()V", output, StringComparison.Ordinal);
        Assert.Contains(".method private static loadFridaGadgetIfNeeded()V", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatchAsync_UpgradesExistingInstanceHelperToStatic_WhenImmediateCallIsInjected()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smali-patch-instance-helper-{Guid.NewGuid():N}");
        var smaliPath = Path.Combine(root, "smali", "com", "app", "damnvulnerablebank");
        Directory.CreateDirectory(smaliPath);

        var file = Path.Combine(smaliPath, "SplashScreen.smali");
        await File.WriteAllTextAsync(file, @".class public Lcom/app/damnvulnerablebank/SplashScreen;
.super Landroid/app/Activity;

.method protected onCreate(Landroid/os/Bundle;)V
    .locals 0
    invoke-super {p0, p1}, Landroid/app/Activity;->onCreate(Landroid/os/Bundle;)V
    return-void
.end method

.method private loadFridaGadget()V
    .locals 1
    const-string v0, ""frida-gadget""
    invoke-static {v0}, Ljava/lang/System;->loadLibrary(Ljava/lang/String;)V
    return-void
.end method

.end class");

        var service = new SmaliPatchService();
        var result = await service.PatchAsync(root, "com.app.damnvulnerablebank.SplashScreen", useDelayedLoad: false);
        var output = await File.ReadAllTextAsync(file);

        Assert.True(result.Success);
        Assert.Contains("invoke-static {}, Lcom/app/damnvulnerablebank/SplashScreen;->loadFridaGadget()V", output, StringComparison.Ordinal);
        Assert.Contains(".method private static loadFridaGadget()V", output, StringComparison.Ordinal);
        Assert.DoesNotContain(".method private loadFridaGadget()V", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatchAsync_UpgradesExistingInstanceHelperWithSpacedSignatureToStatic()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smali-patch-instance-helper-spaced-signature-{Guid.NewGuid():N}");
        var smaliPath = Path.Combine(root, "smali", "com", "app", "damnvulnerablebank");
        Directory.CreateDirectory(smaliPath);

        var file = Path.Combine(smaliPath, "SplashScreen.smali");
        await File.WriteAllTextAsync(file, @".class public Lcom/app/damnvulnerablebank/SplashScreen;
.super Landroid/app/Activity;

.method protected onCreate(Landroid/os/Bundle;)V
    .locals 0
    invoke-super {p0, p1}, Landroid/app/Activity;->onCreate(Landroid/os/Bundle;)V
    return-void
.end method

    .method private loadFridaGadget ()V
    .locals 1
    const-string v0, ""frida-gadget""
    invoke-static {v0}, Ljava/lang/System;->loadLibrary(Ljava/lang/String;)V
    return-void
.end method

.end class");

        var service = new SmaliPatchService();
        var result = await service.PatchAsync(root, "com.app.damnvulnerablebank.SplashScreen", useDelayedLoad: false);
        var output = await File.ReadAllTextAsync(file);

        Assert.True(result.Success);
        Assert.Contains("invoke-static {}, Lcom/app/damnvulnerablebank/SplashScreen;->loadFridaGadget()V", output, StringComparison.Ordinal);
        Assert.Contains(".method private static loadFridaGadget()V", output, StringComparison.Ordinal);
        Assert.DoesNotContain(".method private loadFridaGadget ()V", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatchAsync_NormalizesExistingStaticHelperSignature_WhenInvokeStaticIsUsedInOnCreate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smali-patch-static-helper-spaced-signature-{Guid.NewGuid():N}");
        var smaliPath = Path.Combine(root, "smali", "zed", "rainxch", "githubstore");
        Directory.CreateDirectory(smaliPath);

        var file = Path.Combine(smaliPath, "MainActivity.smali");
        await File.WriteAllTextAsync(file, @".class public Lzed/rainxch/githubstore/MainActivity;
.super Landroid/app/Activity;

.method protected onCreate(Landroid/os/Bundle;)V
    .locals 0
    invoke-super {p0, p1}, Landroid/app/Activity;->onCreate(Landroid/os/Bundle;)V
    invoke-static {}, Lzed/rainxch/githubstore/MainActivity;->loadFridaGadget()V
    return-void
.end method

.method private static loadFridaGadget ()V
    .locals 1
    const-string v0, ""frida-gadget""
    invoke-static {v0}, Ljava/lang/System;->loadLibrary(Ljava/lang/String;)V
    return-void
.end method

.end class");

        var service = new SmaliPatchService();
        var result = await service.PatchAsync(root, "zed.rainxch.githubstore.MainActivity", useDelayedLoad: false);
        var output = await File.ReadAllTextAsync(file);

        Assert.True(result.Success);
        Assert.Contains("invoke-static {}, Lzed/rainxch/githubstore/MainActivity;->loadFridaGadget()V", output, StringComparison.Ordinal);
        Assert.Contains(".method private static loadFridaGadget()V", output, StringComparison.Ordinal);
        Assert.DoesNotContain(".method private static loadFridaGadget ()V", output, StringComparison.Ordinal);
    }


    [Fact]
    public async Task PatchAsync_AcceptsTabIndentedEndClassAndExistingStaticHelper()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smali-patch-tab-indented-end-class-{Guid.NewGuid():N}");
        var smaliPath = Path.Combine(root, "smali", "com", "app", "damnvulnerablebank");
        Directory.CreateDirectory(smaliPath);

        var file = Path.Combine(smaliPath, "SplashScreen.smali");
        await File.WriteAllTextAsync(file, ".class public Lcom/app/damnvulnerablebank/SplashScreen;
.super Landroid/app/Activity;

.method protected onCreate(Landroid/os/Bundle;)V
    .locals 0
    invoke-super {p0, p1}, Landroid/app/Activity;->onCreate(Landroid/os/Bundle;)V
    invoke-static {}, Lcom/app/damnvulnerablebank/SplashScreen;->loadFridaGadget()V
    return-void
.end method

	.method private static loadFridaGadget()V
    .locals 1
    const-string v0, "frida-gadget"
    invoke-static {v0}, Ljava/lang/System;->loadLibrary(Ljava/lang/String;)V
    return-void
.end method

	.end class");

        var service = new SmaliPatchService();
        var result = await service.PatchAsync(root, "com.app.damnvulnerablebank.SplashScreen", useDelayedLoad: false);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task PatchAsync_Fails_WhenLifecycleCallIsInjectedButStaticHelperCannotBeAdded()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smali-patch-missing-end-class-{Guid.NewGuid():N}");
        var smaliPath = Path.Combine(root, "smali", "com", "app", "damnvulnerablebank");
        Directory.CreateDirectory(smaliPath);

        var file = Path.Combine(smaliPath, "SplashScreen.smali");
        await File.WriteAllTextAsync(file, @".class public Lcom/app/damnvulnerablebank/SplashScreen;
.super Landroid/app/Activity;

.method protected onCreate(Landroid/os/Bundle;)V
    .locals 0
    invoke-super {p0, p1}, Landroid/app/Activity;->onCreate(Landroid/os/Bundle;)V
    return-void
.end method");

        var service = new SmaliPatchService();
        var result = await service.PatchAsync(root, "com.app.damnvulnerablebank.SplashScreen", useDelayedLoad: false);

        Assert.False(result.Success);
        Assert.Equal("Patched smali references Frida helper methods that are missing static definitions.", result.Error);
    }

}
