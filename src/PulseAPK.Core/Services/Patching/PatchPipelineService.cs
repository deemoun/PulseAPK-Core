using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Models;
using System.Text.RegularExpressions;

namespace PulseAPK.Core.Services.Patching;

public sealed class PatchPipelineService : IPatchPipelineService
{
    private static readonly HashSet<string> SupportedArchitectures = new(StringComparer.OrdinalIgnoreCase)
    {
        "arm64-v8a",
        "armeabi-v7a",
        "x86",
        "x86_64"
    };

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

            var injectionArchitectures = ResolveInjectionArchitectures(decompiledDirectory, architecture, request.InjectForAllArchitectures);
            foreach (var targetArchitecture in injectionArchitectures)
            {
                var gadgetResolution = await _fridaArtifactService.ResolveGadgetAsync(request, targetArchitecture, cancellationToken);
                if (gadgetResolution.Error is not null || gadgetResolution.GadgetPath is null)
                {
                    result.Errors.Add(gadgetResolution.Error ?? "Unable to resolve Frida gadget artifact.");
                    result.StageSummaries.Add(new PatchStageSummary("artifact-resolution", false, result.Errors.Last()));
                    return result;
                }

                var gadgetInject = await _gadgetInjectionService.InjectAsync(
                    decompiledDirectory,
                    request,
                    targetArchitecture,
                    gadgetResolution.GadgetPath,
                    cancellationToken);
                if (!gadgetInject.Success)
                {
                    result.Errors.Add(gadgetInject.Error ?? "Gadget injection failed.");
                    result.StageSummaries.Add(new PatchStageSummary("gadget-injection", false, result.Errors.Last()));
                    result.Warnings.Add($"Optional script status: {gadgetInject.ScriptStatus.Status} - {gadgetInject.ScriptStatus.Detail}");
                    result.Warnings.Add($"Optional config status: {gadgetInject.ConfigStatus.Status} - {gadgetInject.ConfigStatus.Detail}");
                    return result;
                }

                result.Warnings.Add($"Optional script status: {gadgetInject.ScriptStatus.Status} - {gadgetInject.ScriptStatus.Detail}");
                result.Warnings.Add($"Optional config status: {gadgetInject.ConfigStatus.Status} - {gadgetInject.ConfigStatus.Detail}");
                result.StageSummaries.Add(new PatchStageSummary(
                    "gadget-assets",
                    true,
                    $"ABI '{targetArchitecture}' script={gadgetInject.ScriptStatus.Status}, config={gadgetInject.ConfigStatus.Status}."));
            }

            var injectionMessage = injectionArchitectures.Count == 1
                ? $"Frida gadget injected for ABI '{injectionArchitectures[0]}'."
                : $"Frida gadget injected for ABIs: {string.Join(", ", injectionArchitectures)}.";
            result.StageSummaries.Add(new PatchStageSummary("gadget-injection", true, injectionMessage));

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

            if (smaliInjectionApplied && request.SkipDexValidation)
            {
                const string skippedDexValidationMessage = "Final DEX verification was skipped by user request.";
                result.Warnings.Add(skippedDexValidationMessage);
                result.StageSummaries.Add(new PatchStageSummary("dex-verification", true, skippedDexValidationMessage));
            }
            else if (smaliInjectionApplied)
            {
                var classDescriptor = ToClassDescriptor(activityName);
                var helperMethodName = request.UseDelayedLoad
                    ? "loadFridaGadgetIfNeeded"
                    : "loadFridaGadget";
                var methodReference = $"{classDescriptor}->{helperMethodName}()V";
                var inspection = await _finalDexInspectionService.ContainsMethodReferenceAsync(finalArtifactPath, methodReference, cancellationToken);
                var diagnosticSummary = SummarizeDexDiagnostics(inspection.Diagnostics);
                result.Warnings.Add($"DEX verification target: '{methodReference}' in '{finalArtifactPath}'.");
                result.Warnings.Add(
                    $"DEX verification diagnostics: {inspection.Diagnostics} " +
                    $"(parsed dex entries: {diagnosticSummary.ParsedDexEntries}, failed dex entries: {diagnosticSummary.FailedDexEntries}).");

                if (!inspection.Found)
                {
                    if (diagnosticSummary.ParsedDexEntries > 0 && diagnosticSummary.FailedDexEntries > 0)
                    {
                        const string inconclusiveMessage = "Final DEX verification inconclusive due to dex parse errors.";
                        result.Errors.Add(
                            $"{inconclusiveMessage} Unable to reliably verify '{methodReference}'. {inspection.Diagnostics}");
                        result.StageSummaries.Add(
                            new PatchStageSummary("dex-verification", false, $"{inconclusiveMessage} {inspection.Diagnostics}"));
                    }
                    else if (diagnosticSummary.ParsedDexEntries > 0 && diagnosticSummary.TupleSearchCompleted)
                    {
                        const string guidance = "Smali helper missing in final dex artifact. Ensure smali mutation runs after any transform that regenerates classes.dex, or disable that transform for patched classes.";
                        result.Errors.Add($"Final DEX verification failed: '{methodReference}' was not found. {inspection.Diagnostics} {guidance}");
                        result.StageSummaries.Add(new PatchStageSummary("dex-verification", false, $"{inspection.Diagnostics} {guidance}"));
                    }
                    else
                    {
                        result.Errors.Add(
                            $"Final DEX verification failed before tuple search completed for '{methodReference}'. {inspection.Diagnostics}");
                        result.StageSummaries.Add(new PatchStageSummary("dex-verification", false, inspection.Diagnostics));
                    }

                    result.OutputApkPath = finalArtifactPath;
                    result.SelectedArchitecture = architecture;
                    result.UsedSigning = request.SignOutput;
                    return result;
                }

                result.StageSummaries.Add(new PatchStageSummary("dex-verification", true, $"Confirmed '{methodReference}' in final APK dex. {inspection.Diagnostics}"));
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

    private static List<string> ResolveInjectionArchitectures(string decompiledDirectory, string selectedArchitecture, bool injectForAllArchitectures)
    {
        if (!injectForAllArchitectures)
        {
            return [selectedArchitecture];
        }

        var architectures = new List<string>();

        if (Directory.Exists(Path.Combine(decompiledDirectory, "lib")))
        {
            foreach (var abiPath in Directory.EnumerateDirectories(Path.Combine(decompiledDirectory, "lib")))
            {
                var abi = Path.GetFileName(abiPath);
                if (!string.IsNullOrWhiteSpace(abi) &&
                    SupportedArchitectures.Contains(abi) &&
                    !architectures.Contains(abi, StringComparer.OrdinalIgnoreCase))
                {
                    architectures.Add(abi);
                }
            }
        }

        if (!architectures.Contains(selectedArchitecture, StringComparer.OrdinalIgnoreCase))
        {
            architectures.Insert(0, selectedArchitecture);
        }

        return architectures;
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

    private static DexDiagnosticsSummary SummarizeDexDiagnostics(string diagnostics)
    {
        var summary = new DexDiagnosticsSummary();
        if (string.IsNullOrWhiteSpace(diagnostics))
        {
            return summary;
        }

        var tupleSearchMatch = Regex.Match(diagnostics, @"Method tuple not found in any of the (\d+) dex entries\.", RegexOptions.CultureInvariant);
        if (tupleSearchMatch.Success &&
            int.TryParse(tupleSearchMatch.Groups[1].Value, out var tupleSearchTotalDexEntries))
        {
            var failedDexEntries = Regex.Matches(diagnostics, "warning '", RegexOptions.CultureInvariant).Count;
            failedDexEntries = Math.Clamp(failedDexEntries, 0, tupleSearchTotalDexEntries);
            return new DexDiagnosticsSummary(
                ParsedDexEntries: tupleSearchTotalDexEntries - failedDexEntries,
                FailedDexEntries: failedDexEntries,
                TupleSearchCompleted: true);
        }

        var inspectionFailedMatch = Regex.Match(diagnostics, @"Inspection failed for all (\d+) dex entries\.", RegexOptions.CultureInvariant);
        if (inspectionFailedMatch.Success &&
            int.TryParse(inspectionFailedMatch.Groups[1].Value, out var failedAllDexEntries))
        {
            return new DexDiagnosticsSummary(ParsedDexEntries: 0, FailedDexEntries: failedAllDexEntries, TupleSearchCompleted: false);
        }

        return summary;
    }

    private readonly record struct DexDiagnosticsSummary(int ParsedDexEntries = 0, int FailedDexEntries = 0, bool TupleSearchCompleted = false);
}
