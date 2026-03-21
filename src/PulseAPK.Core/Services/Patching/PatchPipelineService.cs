using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Models;

namespace PulseAPK.Core.Services.Patching;

public sealed class PatchPipelineService : IPatchPipelineService
{
    private readonly PatchRequestValidatorService _requestValidator;
    private readonly IArchitectureDetectionService _architectureDetectionService;
    private readonly IFridaArtifactService _fridaArtifactService;
    private readonly IApktoolService _apktoolService;
    private readonly IActivityDetectionService _activityDetectionService;
    private readonly IManifestPatchService _manifestPatchService;
    private readonly IGadgetInjectionService _gadgetInjectionService;
    private readonly ISmaliPatchService _smaliPatchService;
    private readonly IDexMergeService _dexMergeService;
    private readonly ISigningService _signingService;
    private readonly IFinalDexInspectionService _finalDexInspectionService;

    public PatchPipelineService(
        PatchRequestValidatorService requestValidator,
        IArchitectureDetectionService architectureDetectionService,
        IFridaArtifactService fridaArtifactService,
        IApktoolService apktoolService,
        IActivityDetectionService activityDetectionService,
        IManifestPatchService manifestPatchService,
        IGadgetInjectionService gadgetInjectionService,
        ISmaliPatchService smaliPatchService,
        IDexMergeService dexMergeService,
        ISigningService signingService,
        IFinalDexInspectionService finalDexInspectionService)
    {
        _requestValidator = requestValidator;
        _architectureDetectionService = architectureDetectionService;
        _fridaArtifactService = fridaArtifactService;
        _apktoolService = apktoolService;
        _activityDetectionService = activityDetectionService;
        _manifestPatchService = manifestPatchService;
        _gadgetInjectionService = gadgetInjectionService;
        _smaliPatchService = smaliPatchService;
        _dexMergeService = dexMergeService;
        _signingService = signingService;
        _finalDexInspectionService = finalDexInspectionService;
    }

    public async Task<PatchResult> RunAsync(PatchRequest request, CancellationToken cancellationToken = default)
    {
        var result = new PatchResult();
        var validationErrors = _requestValidator.Validate(request);
        if (validationErrors.Count > 0)
        {
            result.Errors.AddRange(validationErrors);
            result.StageSummaries.Add(new PatchStageSummary("validation", false, string.Join("; ", validationErrors)));
            return result;
        }

        var architectureResolution = await _architectureDetectionService.ResolveAsync(request, cancellationToken);
        if (architectureResolution.Warning is not null)
        {
            result.Warnings.Add(architectureResolution.Warning);
        }

        if (architectureResolution.Error is not null || architectureResolution.Architecture is null)
        {
            result.Errors.Add(architectureResolution.Error ?? "Could not resolve architecture.");
            result.StageSummaries.Add(new PatchStageSummary("architecture", false, result.Errors.Last()));
            return result;
        }

        var architecture = architectureResolution.Architecture;
        result.StageSummaries.Add(new PatchStageSummary("architecture", true, $"Using architecture '{architecture}'."));

        var gadgetResolution = await _fridaArtifactService.ResolveGadgetAsync(request, architecture, cancellationToken);
        if (gadgetResolution.Error is not null || gadgetResolution.GadgetPath is null)
        {
            result.Errors.Add(gadgetResolution.Error ?? "Unable to resolve Frida gadget artifact.");
            result.StageSummaries.Add(new PatchStageSummary("artifact-resolution", false, result.Errors.Last()));
            return result;
        }

        var decompiledDirectory = PrepareWorkingDirectory(request);
        var cleanupDirectory = !request.KeepIntermediateFiles ? decompiledDirectory : null;

        var smaliInjectionApplied = false;

        try
        {
            var decompileCode = await _apktoolService.DecompileAsync(request.InputApkPath, decompiledDirectory, request.DecodeResources, request.DecodeSources, cancellationToken);
            if (decompileCode != 0)
            {
                result.Errors.Add($"Decompile failed with exit code {decompileCode}.");
                result.StageSummaries.Add(new PatchStageSummary("decompile", false, result.Errors.Last()));
                return result;
            }

            result.StageSummaries.Add(new PatchStageSummary("decompile", true, "APK decompiled successfully."));

            var activityResult = await _activityDetectionService.DetectMainActivityAsync(decompiledDirectory, cancellationToken);
            if (activityResult.Warning is not null)
            {
                result.Warnings.Add(activityResult.Warning);
            }

            if (activityResult.Error is not null || activityResult.ActivityName is null)
            {
                result.Errors.Add(activityResult.Error ?? "Unable to detect main activity.");
                result.StageSummaries.Add(new PatchStageSummary("activity-detection", false, result.Errors.Last()));
                return result;
            }

            var activityName = activityResult.ActivityName;
            result.PatchedActivity = activityName;
            result.StageSummaries.Add(new PatchStageSummary("activity-detection", true, $"Selected activity '{activityName}'."));

            var manifestPath = Path.Combine(decompiledDirectory, "AndroidManifest.xml");
            var manifestPatch = await _manifestPatchService.PatchAsync(manifestPath, request, cancellationToken);
            if (!manifestPatch.Success)
            {
                result.Errors.Add(manifestPatch.Error ?? "Manifest patch failed.");
                result.StageSummaries.Add(new PatchStageSummary("manifest-patch", false, result.Errors.Last()));
                return result;
            }

            result.StageSummaries.Add(new PatchStageSummary("manifest-patch", true, "Manifest patched."));

            var gadgetInject = await _gadgetInjectionService.InjectAsync(decompiledDirectory, request, architecture, gadgetResolution.GadgetPath, cancellationToken);
            if (!gadgetInject.Success)
            {
                result.Errors.Add(gadgetInject.Error ?? "Gadget injection failed.");
                result.StageSummaries.Add(new PatchStageSummary("gadget-injection", false, result.Errors.Last()));
                return result;
            }

            result.StageSummaries.Add(new PatchStageSummary("gadget-injection", true, "Frida gadget injected."));

            if (!request.DecodeSources)
            {
                const string smaliSkipMessage = "Smali patch skipped because source decoding is disabled.";
                result.Warnings.Add(smaliSkipMessage);
                result.StageSummaries.Add(new PatchStageSummary("smali-patch", true, smaliSkipMessage));
            }
            else
            {
                var smaliPatch = await _smaliPatchService.PatchAsync(decompiledDirectory, activityName, request.UseDelayedLoad, cancellationToken);
                if (!smaliPatch.Success)
                {
                    result.Errors.Add(smaliPatch.Error ?? "Smali patch failed.");
                    result.StageSummaries.Add(new PatchStageSummary("smali-patch", false, result.Errors.Last()));
                    return result;
                }

                result.StageSummaries.Add(new PatchStageSummary("smali-patch", true, "Smali patched."));
                smaliInjectionApplied = true;
            }

            var buildCode = await _apktoolService.BuildAsync(decompiledDirectory, request.OutputApkPath, request.UseAapt2ForBuild, cancellationToken);
            if (buildCode != 0)
            {
                result.Errors.Add($"Build failed with exit code {buildCode}.");
                result.StageSummaries.Add(new PatchStageSummary("build", false, result.Errors.Last()));
                return result;
            }

            result.StageSummaries.Add(new PatchStageSummary("build", true, "APK rebuilt successfully."));

            var dexMode = request.DexPreservationMode;
            if (dexMode == DexPreservationMode.Disabled && request.PreserveOriginalDexFiles)
            {
                // Backward compatibility for callers still setting PreserveOriginalDexFiles.
                dexMode = DexPreservationMode.PreserveUnmodifiedSecondaryDexFiles;
            }

            if (dexMode != DexPreservationMode.Disabled)
            {
                if (dexMode == DexPreservationMode.ReplaceAllDexFiles && smaliInjectionApplied && !request.ConfirmDangerousDexReplacement)
                {
                    const string dangerousModeError = "Dex replacement mode 'replace all dex' is blocked because smali injection was applied. Confirm dangerous mode explicitly to continue; replacing all dex may discard injected smali changes.";
                    result.Errors.Add(dangerousModeError);
                    result.StageSummaries.Add(new PatchStageSummary("dex-preservation", false, dangerousModeError));
                    return result;
                }

                var dexResult = await _dexMergeService.PreserveOriginalDexFilesAsync(
                    request.InputApkPath,
                    request.OutputApkPath,
                    dexMode,
                    cancellationToken);
                if (!dexResult.Success)
                {
                    result.Errors.Add(dexResult.Error ?? "DEX merge failed.");
                    result.StageSummaries.Add(new PatchStageSummary("dex-preservation", false, result.Errors.Last()));
                    return result;
                }

                var dexMessage = dexMode == DexPreservationMode.ReplaceAllDexFiles
                    ? "All dex files were replaced from the original APK (dangerous mode)."
                    : "Original non-modified secondary dex files preserved.";
                result.StageSummaries.Add(new PatchStageSummary("dex-preservation", true, dexMessage));
            }

            var finalArtifactPath = request.OutputApkPath;

            if (request.SignOutput)
            {
                var signedPath = GetSignedPath(request.OutputApkPath);
                var signResult = await _signingService.SignAsync(request.OutputApkPath, signedPath, cancellationToken);
                if (!signResult.Success)
                {
                    result.Errors.Add(signResult.Error ?? "Signing failed.");
                    result.Warnings.Add("Unsigned rebuilt APK was preserved.");
                    result.StageSummaries.Add(new PatchStageSummary("signing", false, result.Errors.Last()));
                    result.OutputApkPath = request.OutputApkPath;
                    result.SelectedArchitecture = architecture;
                    return result;
                }

                result.StageSummaries.Add(new PatchStageSummary("signing", true, "Signed APK created."));
                finalArtifactPath = signResult.SignedApkPath!;
            }

            if (smaliInjectionApplied)
            {
                var classDescriptor = ToClassDescriptor(activityName);
                var methodReference = $"{classDescriptor}->loadFridaGadget()V";
                var foundInFinalDex = await _finalDexInspectionService.ContainsMethodReferenceAsync(finalArtifactPath, methodReference, cancellationToken);
                if (!foundInFinalDex)
                {
                    const string guidance = "Smali helper was present during patching but missing in the final DEX artifact. Ensure smali mutation runs after any transform that regenerates classes.dex, or disable that transform for patched classes.";
                    result.Errors.Add($"Final DEX verification failed: '{methodReference}' was not found. {guidance}");
                    result.StageSummaries.Add(new PatchStageSummary("dex-verification", false, guidance));
                    result.OutputApkPath = finalArtifactPath;
                    result.SelectedArchitecture = architecture;
                    result.UsedSigning = request.SignOutput;
                    return result;
                }

                result.StageSummaries.Add(new PatchStageSummary("dex-verification", true, $"Confirmed '{methodReference}' in final APK dex."));
            }

            result.Success = true;
            result.OutputApkPath = finalArtifactPath;
            result.SelectedArchitecture = architecture;
            result.UsedSigning = request.SignOutput;
            return result;
        }
        finally
        {
            if (cleanupDirectory is not null && Directory.Exists(cleanupDirectory))
            {
                Directory.Delete(cleanupDirectory, recursive: true);
            }
        }
    }

    private static string PrepareWorkingDirectory(PatchRequest request)
    {
        var root = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? Path.Combine(Path.GetTempPath(), "pulseapk-patch")
            : request.WorkingDirectory;

        var path = Path.Combine(root!, $"job-{Guid.NewGuid():N}", "decompiled");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetSignedPath(string outputApkPath)
    {
        var directory = Path.GetDirectoryName(outputApkPath) ?? Directory.GetCurrentDirectory();
        var name = Path.GetFileNameWithoutExtension(outputApkPath);
        var extension = Path.GetExtension(outputApkPath);
        return Path.Combine(directory, $"{name}_signed{extension}");
    }

    private static string ToClassDescriptor(string activityName)
        => $"L{activityName.Replace('.', '/')};";
}
