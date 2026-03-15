using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Models;
using PulseAPK.Core.Services.Patching;

namespace PulseAPK.Tests.Services.Patching;

public class PatchPipelineServiceTests
{
    [Fact]
    public async Task RunAsync_ReturnsFailure_WhenValidationFails()
    {
        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new PatchRequest());

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task RunAsync_ReturnsSuccess_WhenAllStagesPass()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = false
        });

        Assert.True(result.Success);
        Assert.Equal(outputApk, result.OutputApkPath);
    }

    private static PatchPipelineService CreatePipeline()
    {
        return new PatchPipelineService(
            new PatchRequestValidatorService(),
            new FakeArchitectureService(),
            new FakeArtifactService(),
            new FakeApktoolService(),
            new FakeActivityDetectionService(),
            new FakeManifestPatchService(),
            new FakeGadgetInjectionService(),
            new FakeSmaliPatchService(),
            new FakeDexMergeService(),
            new FakeSigningService());
    }

    private sealed class FakeArchitectureService : IArchitectureDetectionService
    {
        public Task<(string? Architecture, string? Error, string? Warning)> ResolveAsync(PatchRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<(string?, string?, string?)>(("arm64-v8a", null, null));
    }

    private sealed class FakeArtifactService : IFridaArtifactService
    {
        public Task<(string? GadgetPath, string? Error)> ResolveGadgetAsync(PatchRequest request, string architecture, CancellationToken cancellationToken = default)
            => Task.FromResult<(string?, string?)>((request.InputApkPath, null));
    }

    private sealed class FakeApktoolService : IApktoolService
    {
        public Task<int> DecompileAsync(string apkPath, string outputDirectory, bool decodeResources, bool decodeSources, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "AndroidManifest.xml"), "<manifest xmlns:android='http://schemas.android.com/apk/res/android'><application><activity android:name='com.example.MainActivity' /></application></manifest>");
            Directory.CreateDirectory(Path.Combine(outputDirectory, "smali", "com", "example"));
            File.WriteAllText(Path.Combine(outputDirectory, "smali", "com", "example", "MainActivity.smali"), ".class public Lcom/example/MainActivity;\n.super Landroid/app/Activity;\n\n.end class");
            return Task.FromResult(0);
        }

        public Task<int> BuildAsync(string decompiledDirectory, string outputApkPath, bool useAapt2, CancellationToken cancellationToken = default)
        {
            File.WriteAllText(outputApkPath, "built");
            return Task.FromResult(0);
        }
    }

    private sealed class FakeActivityDetectionService : IActivityDetectionService
    {
        public Task<(string? ActivityName, string? Warning, string? Error)> DetectMainActivityAsync(string decompiledDirectory, CancellationToken cancellationToken = default)
            => Task.FromResult<(string?, string?, string?)>(("com.example.MainActivity", null, null));
    }

    private sealed class FakeManifestPatchService : IManifestPatchService
    {
        public Task<(bool Success, string? Error)> PatchAsync(string manifestPath, PatchRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult((true, (string?)null));
    }

    private sealed class FakeGadgetInjectionService : IGadgetInjectionService
    {
        public Task<(bool Success, string? Error)> InjectAsync(string decompiledDirectory, PatchRequest request, string architecture, string gadgetSourcePath, CancellationToken cancellationToken = default)
            => Task.FromResult((true, (string?)null));
    }

    private sealed class FakeSmaliPatchService : ISmaliPatchService
    {
        public Task<(bool Success, string? Error)> PatchAsync(string decompiledDirectory, string activityName, bool useDelayedLoad, CancellationToken cancellationToken = default)
            => Task.FromResult((true, (string?)null));
    }

    private sealed class FakeDexMergeService : IDexMergeService
    {
        public Task<(bool Success, string? Error)> PreserveOriginalDexFilesAsync(string originalApkPath, string rebuiltApkPath, CancellationToken cancellationToken = default)
            => Task.FromResult((true, (string?)null));
    }

    private sealed class FakeSigningService : ISigningService
    {
        public Task<(bool Success, string? SignedApkPath, string? Error)> SignAsync(string inputApkPath, string outputApkPath, CancellationToken cancellationToken = default)
            => Task.FromResult((true, outputApkPath, (string?)null));
    }
}
