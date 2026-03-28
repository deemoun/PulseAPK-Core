using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Models;
using PulseAPK.Core.Services.Patching;
using System.IO.Compression;
using System.Text;

namespace PulseAPK.Tests.Services.Patching;

public class PatchPipelineServiceTests
{
    private static readonly string[] SuccessfulStagesWithoutSigning =
    [
        "architecture",
        "decompile",
        "activity-detection",
        "manifest-patch",
        "gadget-assets",
        "gadget-injection",
        "smali-patch",
        "build",
        "dex-verification"
    ];

    private static readonly string[] SuccessfulStagesWithSigning =
    [
        "architecture",
        "decompile",
        "activity-detection",
        "manifest-patch",
        "gadget-assets",
        "gadget-injection",
        "smali-patch",
        "build",
        "signing",
        "dex-verification"
    ];

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
        AssertStageSequence(result, SuccessfulStagesWithoutSigning);
    }

    [Fact]
    public async Task RunAsync_RecordsExactStageSequence_WhenSigningEnabled()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = true
        });

        Assert.True(result.Success);
        Assert.True(result.UsedSigning);
        Assert.Equal(GetSignedPath(outputApk), result.OutputApkPath);
        AssertStageSequence(result, SuccessfulStagesWithSigning);
    }


    [Fact]
    public async Task RunAsync_SkipsDexMerge_WhenDexModeDisabled()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        var fakeDexMergeService = new FakeDexMergeService(shouldFail: false);
        var pipeline = CreatePipeline(fakeDexMergeService: fakeDexMergeService);

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = false,
            DexPreservationMode = DexPreservationMode.Disabled
        });

        Assert.True(result.Success);
        Assert.Equal(0, fakeDexMergeService.CallCount);
        Assert.DoesNotContain(result.StageSummaries, static stage => stage.Stage == "dex-preservation");
    }

    [Fact]
    public async Task RunAsync_PreservesSecondaryDex_WhenModeRequested()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        var fakeDexMergeService = new FakeDexMergeService(shouldFail: false);
        var pipeline = CreatePipeline(fakeDexMergeService: fakeDexMergeService);

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = false,
            DexPreservationMode = DexPreservationMode.PreserveUnmodifiedSecondaryDexFiles
        });

        Assert.True(result.Success);
        Assert.Equal(1, fakeDexMergeService.CallCount);
        Assert.Equal(DexPreservationMode.PreserveUnmodifiedSecondaryDexFiles, fakeDexMergeService.LastMode);
        var dexStage = Assert.Single(result.StageSummaries.Where(static s => s.Stage == "dex-preservation"));
        Assert.True(dexStage.Success);
    }

    [Fact]
    public async Task RunAsync_BlocksReplaceAllDex_WhenSmaliInjectionAppliedWithoutDangerousConfirmation()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        var fakeDexMergeService = new FakeDexMergeService(shouldFail: false);
        var pipeline = CreatePipeline(fakeDexMergeService: fakeDexMergeService);

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = false,
            DexPreservationMode = DexPreservationMode.ReplaceAllDexFiles
        });

        Assert.False(result.Success);
        Assert.Equal(0, fakeDexMergeService.CallCount);
        var dexStage = Assert.Single(result.StageSummaries.Where(static s => s.Stage == "dex-preservation"));
        Assert.False(dexStage.Success);
        Assert.Contains("discard injected smali changes", dexStage.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Errors, error => error.Contains("replace all dex", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_AllowsReplaceAllDex_WhenDangerousModeExplicitlyConfirmed()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        var fakeDexMergeService = new FakeDexMergeService(shouldFail: false);
        var pipeline = CreatePipeline(fakeDexMergeService: fakeDexMergeService);

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = false,
            DexPreservationMode = DexPreservationMode.ReplaceAllDexFiles,
            ConfirmDangerousDexReplacement = true
        });

        Assert.True(result.Success);
        Assert.Equal(1, fakeDexMergeService.CallCount);
        Assert.Equal(DexPreservationMode.ReplaceAllDexFiles, fakeDexMergeService.LastMode);
    }

    [Fact]
    public async Task RunAsync_EmitsClearFailure_WhenDexPreserveModeFails()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        var fakeDexMergeService = new FakeDexMergeService(shouldFail: true);
        var pipeline = CreatePipeline(fakeDexMergeService: fakeDexMergeService);

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = false,
            DexPreservationMode = DexPreservationMode.PreserveUnmodifiedSecondaryDexFiles
        });

        Assert.False(result.Success);
        var dexStage = Assert.Single(result.StageSummaries.Where(static s => s.Stage == "dex-preservation"));
        Assert.False(dexStage.Success);
        Assert.Contains("DEX merge failed", dexStage.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task RunAsync_UsesSkipWarningSemantics_WhenDecodeSourcesDisabled()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = false,
            DecodeSources = false
        });

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, warning => warning.Contains("Smali patch skipped", StringComparison.OrdinalIgnoreCase));
        var smaliStage = Assert.Single(result.StageSummaries.Where(static s => s.Stage == "smali-patch"));
        Assert.True(smaliStage.Success);
        Assert.Contains("skipped", smaliStage.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_UsesSampleInjectionStage_WhenSampleProfileSelected()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        var fakeArtifactService = new FakeArtifactService();
        var fakeGadgetInjectionService = new FakeGadgetInjectionService();
        var pipeline = CreatePipeline(
            fakeArtifactService: fakeArtifactService,
            fakeGadgetInjectionService: fakeGadgetInjectionService);

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            ScriptInjectionProfile = ScriptInjectionProfile.SampleInjection,
            SignOutput = false
        });

        Assert.True(result.Success);
        Assert.Empty(fakeArtifactService.ResolvedArchitectures);
        Assert.Empty(fakeGadgetInjectionService.InjectedArchitectures);
        Assert.DoesNotContain(result.StageSummaries, static stage => stage.Stage == "gadget-assets");
        Assert.Contains(result.StageSummaries, static stage =>
            stage.Stage == "sample-injection" &&
            stage.Message.Contains("sample injection", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.StageSummaries, static stage =>
            stage.Stage == "smali-patch" &&
            stage.Message.Contains("sample", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_EmitsAliasFallbackWarning_WhenArchitectureResolverProvidesWarning()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        const string aliasFallbackWarning = "ABI alias fallback used: mapping arm64 to arm64-v8a.";
        var pipeline = CreatePipeline(fakeArchitectureService: new FakeArchitectureService(warning: aliasFallbackWarning));

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = false
        });

        Assert.True(result.Success);
        Assert.Contains(aliasFallbackWarning, result.Warnings);
        AssertStageSequence(result, SuccessfulStagesWithoutSigning);
    }

    [Fact]
    public async Task RunAsync_InsertsGadgetIntoAllDiscoveredAbis()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        var fakeArtifactService = new FakeArtifactService();
        var fakeGadgetInjectionService = new FakeGadgetInjectionService();
        var fakeApktoolService = new FakeApktoolService(libAbis: ["arm64-v8a", "armeabi-v7a"]);
        var pipeline = CreatePipeline(
            fakeArtifactService: fakeArtifactService,
            fakeGadgetInjectionService: fakeGadgetInjectionService,
            fakeApktoolService: fakeApktoolService);

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = false
        });

        Assert.True(result.Success);
        Assert.Equal(["arm64-v8a", "armeabi-v7a"], fakeArtifactService.ResolvedArchitectures);
        Assert.Equal(["arm64-v8a", "armeabi-v7a"], fakeGadgetInjectionService.InjectedArchitectures);
        Assert.Contains(result.StageSummaries, static stage =>
            stage.Stage == "gadget-injection" &&
            stage.Message.Contains("arm64-v8a, armeabi-v7a", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ReturnsFailedStageAndError_WhenDecompileFails()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        var pipeline = CreatePipeline(fakeApktoolService: new FakeApktoolService(decompileExitCode: 7));

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk
        });

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        var stage = Assert.Single(result.StageSummaries.Where(static s => s.Stage == "decompile"));
        Assert.False(stage.Success);
        Assert.Contains("exit code 7", stage.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Errors, error => error.Contains("exit code 7", StringComparison.OrdinalIgnoreCase));
        AssertStageSequence(result, ["architecture", "decompile"]);
    }

    [Fact]
    public async Task RunAsync_ReturnsFailedStageAndError_WhenBuildFails()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        var pipeline = CreatePipeline(fakeApktoolService: new FakeApktoolService(buildExitCode: 9));

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = false
        });

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        var stage = Assert.Single(result.StageSummaries.Where(static s => s.Stage == "build"));
        Assert.False(stage.Success);
        Assert.Contains("exit code 9", stage.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Errors, error => error.Contains("exit code 9", StringComparison.OrdinalIgnoreCase));
        AssertStageSequence(result,
        [
            "architecture",
            "decompile",
            "activity-detection",
            "manifest-patch",
            "gadget-assets",
            "gadget-injection",
            "smali-patch",
            "build"
        ]);
    }

    [Fact]
    public async Task RunAsync_ReturnsFailedStageAndError_WhenSigningFails()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        var pipeline = CreatePipeline(fakeSigningService: new FakeSigningService(shouldFail: true));

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = true
        });

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        var stage = Assert.Single(result.StageSummaries.Where(static s => s.Stage == "signing"));
        Assert.False(stage.Success);
        Assert.Contains("Signing failed", stage.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Errors, error => error.Contains("Signing failed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("Unsigned rebuilt APK", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(outputApk, result.OutputApkPath);
        AssertStageSequence(result,
        [
            "architecture",
            "decompile",
            "activity-detection",
            "manifest-patch",
            "gadget-assets",
            "gadget-injection",
            "smali-patch",
            "build",
            "signing"
        ]);
    }

    [Fact]
    public async Task RunAsync_ReturnsFailure_WhenInjectedMethodMissingInFinalDexArtifact()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        var pipeline = CreatePipeline(fakeFinalDexInspectionService: new FakeFinalDexInspectionService(
            containsMethodReference: false,
            diagnostics: "Method tuple not found in any of the 2 dex entries."));

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = true
        });

        Assert.False(result.Success);
        var stage = Assert.Single(result.StageSummaries.Where(static s => s.Stage == "dex-verification"));
        Assert.False(stage.Success);
        Assert.Contains("regenerates classes.dex", stage.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("helper missing in final dex artifact", stage.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("loadFridaGadget()V", Assert.Single(result.Errors), StringComparison.Ordinal);
        Assert.Equal(GetSignedPath(outputApk), result.OutputApkPath);
    }

    [Fact]
    public async Task RunAsync_UsesDelayedLoadHelperForFinalDexVerification_WhenDelayedLoadIsEnabled()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");
        var fakeFinalDexInspectionService = new FakeFinalDexInspectionService();

        var pipeline = CreatePipeline(fakeFinalDexInspectionService: fakeFinalDexInspectionService);

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = true,
            UseDelayedLoad = true
        });

        Assert.True(result.Success);
        Assert.Equal(
            "Lcom/example/MainActivity;->loadFridaGadgetIfNeeded()V",
            fakeFinalDexInspectionService.LastMethodReference);
        var stage = Assert.Single(result.StageSummaries.Where(static s => s.Stage == "dex-verification"));
        Assert.Contains("loadFridaGadgetIfNeeded()V", stage.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_UsesSampleHelperForFinalDexVerification_WhenSampleProfileIsEnabled()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");
        var fakeFinalDexInspectionService = new FakeFinalDexInspectionService();

        var pipeline = CreatePipeline(fakeFinalDexInspectionService: fakeFinalDexInspectionService);

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            ScriptInjectionProfile = ScriptInjectionProfile.SampleInjection,
            SignOutput = true
        });

        Assert.True(result.Success);
        Assert.Equal(
            "Lcom/example/MainActivity;->logSampleInjectionApplied()V",
            fakeFinalDexInspectionService.LastMethodReference);
        var stage = Assert.Single(result.StageSummaries.Where(static s => s.Stage == "dex-verification"));
        Assert.Contains("logSampleInjectionApplied()V", stage.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReturnsInconclusiveVerification_WhenDexParsingFailsForSubsetOfEntries()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");
        var diagnostics = "Method tuple not found in any of the 3 dex entries. Non-fatal parse failures: warning 'classes2.dex': malformed header";
        var pipeline = CreatePipeline(
            fakeFinalDexInspectionService: new FakeFinalDexInspectionService(containsMethodReference: false, diagnostics: diagnostics));

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk
        });

        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Contains("verification inconclusive due to dex parse errors", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("helper missing in final dex artifact", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Warnings, static warning =>
            warning.Contains("parsed dex entries: 2", StringComparison.OrdinalIgnoreCase) &&
            warning.Contains("failed dex entries: 1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_DoesNotReportHelperMissing_WhenNoDexEntriesParseSuccessfully()
    {
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");
        var diagnostics = "Inspection failed for all 2 dex entries. Parse failures: warning 'classes.dex': malformed; warning 'classes2.dex': malformed";
        var pipeline = CreatePipeline(
            fakeFinalDexInspectionService: new FakeFinalDexInspectionService(containsMethodReference: false, diagnostics: diagnostics));

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk
        });

        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.DoesNotContain("helper missing in final dex artifact", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("verification inconclusive due to dex parse errors", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("before tuple search completed", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Warnings, static warning =>
            warning.Contains("parsed dex entries: 0", StringComparison.OrdinalIgnoreCase) &&
            warning.Contains("failed dex entries: 2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_DowngradesActivityInjectionPointFailureToWarning_WhenApplicationPatchSucceeds()
    {
        const string activityPatchFailurePrefix = "Application smali patch applied, but activity patch failed:";
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");

        var pipeline = CreatePipeline(
            fakeSmaliPatchService: new FakeSmaliPatchService(
                shouldFail: true,
                errorMessage: $"{activityPatchFailurePrefix} Unable to find an injection point in activity smali file."));

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = false,
            ScriptInjectionProfile = ScriptInjectionProfile.FridaGadget
        });

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, static warning =>
            warning.Contains("activity patch failed", StringComparison.OrdinalIgnoreCase) &&
            warning.Contains("injection point", StringComparison.OrdinalIgnoreCase));
        var smaliStage = Assert.Single(result.StageSummaries.Where(static s => s.Stage == "smali-patch"));
        Assert.True(smaliStage.Success);
        Assert.Contains("activity patch failed", smaliStage.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_VerifiesApplicationHelper_WhenActivityPatchFallbackBranchIsUsed()
    {
        const string activityPatchFailurePrefix = "Application smali patch applied, but activity patch failed:";
        var inputApk = Path.Combine(Path.GetTempPath(), $"input-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(inputApk, "apk");
        var outputApk = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}.apk");
        var fakeFinalDexInspectionService = new FakeFinalDexInspectionService();

        var pipeline = CreatePipeline(
            fakeSmaliPatchService: new FakeSmaliPatchService(
                shouldFail: true,
                errorMessage: $"{activityPatchFailurePrefix} Unable to find an injection point in activity smali file."),
            fakeFinalDexInspectionService: fakeFinalDexInspectionService);

        var result = await pipeline.RunAsync(new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            SignOutput = false,
            ScriptInjectionProfile = ScriptInjectionProfile.FridaGadget
        });

        Assert.True(result.Success);
        Assert.Equal(
            "Lcom/pulseapk/generated/PulseFridaApplication;->loadFridaGadgetSafely()V",
            fakeFinalDexInspectionService.LastMethodReference);
        var dexStage = Assert.Single(result.StageSummaries.Where(static s => s.Stage == "dex-verification"));
        Assert.Contains("loadFridaGadgetSafely()V", dexStage.Message, StringComparison.Ordinal);
    }

    private static void AssertStageSequence(PatchResult result, IReadOnlyList<string> expectedStages)
    {
        Assert.Equal(expectedStages, result.StageSummaries.Select(static summary => summary.Stage));
    }

    private static string GetSignedPath(string outputApkPath)
    {
        var directory = Path.GetDirectoryName(outputApkPath) ?? Directory.GetCurrentDirectory();
        var name = Path.GetFileNameWithoutExtension(outputApkPath);
        var extension = Path.GetExtension(outputApkPath);
        return Path.Combine(directory, $"{name}_signed{extension}");
    }

    private static PatchPipelineService CreatePipeline(
        bool dexMergeShouldFail = false,
        FakeDexMergeService? fakeDexMergeService = null,
        FakeArchitectureService? fakeArchitectureService = null,
        FakeArtifactService? fakeArtifactService = null,
        FakeApktoolService? fakeApktoolService = null,
        FakeGadgetInjectionService? fakeGadgetInjectionService = null,
        FakeSmaliPatchService? fakeSmaliPatchService = null,
        FakeSigningService? fakeSigningService = null,
        FakeFinalDexInspectionService? fakeFinalDexInspectionService = null)
    {
        fakeDexMergeService ??= new FakeDexMergeService(dexMergeShouldFail);
        fakeArchitectureService ??= new FakeArchitectureService();
        fakeArtifactService ??= new FakeArtifactService();
        fakeApktoolService ??= new FakeApktoolService();
        fakeGadgetInjectionService ??= new FakeGadgetInjectionService();
        fakeSmaliPatchService ??= new FakeSmaliPatchService();
        fakeSigningService ??= new FakeSigningService();
        fakeFinalDexInspectionService ??= new FakeFinalDexInspectionService();

        return new PatchPipelineService(
            new PatchRequestValidatorService(),
            fakeArchitectureService,
            fakeArtifactService,
            fakeApktoolService,
            new FakeActivityDetectionService(),
            new FakeManifestPatchService(),
            fakeGadgetInjectionService,
            fakeSmaliPatchService,
            fakeDexMergeService,
            fakeSigningService,
            fakeFinalDexInspectionService);
    }

    private sealed class FakeArchitectureService : IArchitectureDetectionService
    {
        private readonly string? _warning;

        public FakeArchitectureService(string? warning = null)
        {
            _warning = warning;
        }

        public Task<(string? Architecture, string? Error, string? Warning)> ResolveAsync(PatchRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<(string?, string?, string?)>(("arm64-v8a", null, _warning));
    }

    private sealed class FakeArtifactService : IFridaArtifactService
    {
        public List<string> ResolvedArchitectures { get; } = [];

        public Task<(string? GadgetPath, string? Error)> ResolveGadgetAsync(PatchRequest request, string architecture, CancellationToken cancellationToken = default)
        {
            ResolvedArchitectures.Add(architecture);
            return Task.FromResult<(string?, string?)>((request.InputApkPath, null));
        }
    }

    private sealed class FakeApktoolService : IApktoolService
    {
        private readonly int _decompileExitCode;
        private readonly int _buildExitCode;
        private readonly IReadOnlyList<string> _libAbis;

        public FakeApktoolService(int decompileExitCode = 0, int buildExitCode = 0, IReadOnlyList<string>? libAbis = null)
        {
            _decompileExitCode = decompileExitCode;
            _buildExitCode = buildExitCode;
            _libAbis = libAbis ?? [];
        }

        public Task<int> DecompileAsync(string apkPath, string outputDirectory, bool decodeResources, bool decodeSources, CancellationToken cancellationToken = default)
        {
            if (_decompileExitCode != 0)
            {
                return Task.FromResult(_decompileExitCode);
            }

            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "AndroidManifest.xml"), "<manifest xmlns:android='http://schemas.android.com/apk/res/android'><application><activity android:name='com.example.MainActivity' /></application></manifest>");
            Directory.CreateDirectory(Path.Combine(outputDirectory, "smali", "com", "example"));
            File.WriteAllText(Path.Combine(outputDirectory, "smali", "com", "example", "MainActivity.smali"), ".class public Lcom/example/MainActivity;\n.super Landroid/app/Activity;\n\n.end class");
            foreach (var abi in _libAbis)
            {
                Directory.CreateDirectory(Path.Combine(outputDirectory, "lib", abi));
            }

            return Task.FromResult(0);
        }

        public Task<int> BuildAsync(string decompiledDirectory, string outputApkPath, bool useAapt2, CancellationToken cancellationToken = default)
        {
            if (_buildExitCode != 0)
            {
                return Task.FromResult(_buildExitCode);
            }

            using var stream = File.Create(outputApkPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
            var dexEntry = archive.CreateEntry("classes.dex");
            using var writer = new BinaryWriter(dexEntry.Open(), Encoding.UTF8, leaveOpen: false);
            writer.Write(Encoding.ASCII.GetBytes("Lcom/example/MainActivity;->loadFridaGadget()V"));
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
        public List<string> InjectedArchitectures { get; } = [];

        public Task<GadgetInjectionResult> InjectAsync(string decompiledDirectory, PatchRequest request, string architecture, string gadgetSourcePath, CancellationToken cancellationToken = default)
        {
            InjectedArchitectures.Add(architecture);
            return Task.FromResult(new GadgetInjectionResult(
                Success: true,
                Error: null,
                ScriptStatus: new OptionalAssetCopyResult(OptionalAssetCopyStatus.Skipped, "No script provided."),
                ConfigStatus: new OptionalAssetCopyResult(OptionalAssetCopyStatus.Skipped, "No config provided.")));
        }
    }

    private sealed class FakeSmaliPatchService : ISmaliPatchService
    {
        private readonly bool _shouldFail;
        private readonly string? _errorMessage;

        public FakeSmaliPatchService(bool shouldFail = false, string? errorMessage = null)
        {
            _shouldFail = shouldFail;
            _errorMessage = errorMessage;
        }

        public Task<(bool Success, string? Error)> PatchAsync(
            string decompiledDirectory,
            string activityName,
            ScriptInjectionProfile profile,
            bool useDelayedLoad,
            CancellationToken cancellationToken = default)
            => _shouldFail
                ? Task.FromResult((false, _errorMessage ?? "Fake smali patch failure."))
                : Task.FromResult((true, (string?)null));
    }

    private sealed class FakeDexMergeService : IDexMergeService
    {
        private readonly bool _shouldFail;

        public FakeDexMergeService(bool shouldFail)
        {
            _shouldFail = shouldFail;
        }

        public int CallCount { get; private set; }

        public DexPreservationMode? LastMode { get; private set; }

        public Task<(bool Success, string? Error)> PreserveOriginalDexFilesAsync(string originalApkPath, string rebuiltApkPath, DexPreservationMode mode = DexPreservationMode.PreserveUnmodifiedSecondaryDexFiles, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastMode = mode;

            if (_shouldFail)
            {
                return Task.FromResult((false, (string?)"DEX merge failed in explicit preserve mode."));
            }

            return Task.FromResult((true, (string?)null));
        }
    }

    private sealed class FakeSigningService : ISigningService
    {
        private readonly bool _shouldFail;

        public FakeSigningService(bool shouldFail = false)
        {
            _shouldFail = shouldFail;
        }

        public Task<(bool Success, string? SignedApkPath, string? Error)> SignAsync(string inputApkPath, string outputApkPath, CancellationToken cancellationToken = default)
        {
            if (_shouldFail)
            {
                return Task.FromResult((false, (string?)null, (string?)"Signing failed in fake service."));
            }

            File.Copy(inputApkPath, outputApkPath, overwrite: true);
            return Task.FromResult((true, (string?)outputApkPath, (string?)null));
        }
    }

    private sealed class FakeFinalDexInspectionService : IFinalDexInspectionService
    {
        private readonly bool _containsMethodReference;
        private readonly string _diagnostics;
        public string? LastMethodReference { get; private set; }

        public FakeFinalDexInspectionService(bool containsMethodReference = true, string? diagnostics = null)
        {
            _containsMethodReference = containsMethodReference;
            _diagnostics = diagnostics ?? (_containsMethodReference ? "Found in fake dex." : "Missing in fake dex.");
        }

        public Task<(bool Found, string Diagnostics)> ContainsMethodReferenceAsync(string apkPath, string methodReference, CancellationToken cancellationToken = default)
        {
            LastMethodReference = methodReference;
            return Task.FromResult((_containsMethodReference, _diagnostics));
        }
    }
}
