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
        var jobDirectory = Directory.GetParent(decompiledDirectory)?.FullName ?? decompiledDirectory;
        var cleanupDirectory = !request.KeepIntermediateFiles ? jobDirectory : null;

        var smaliInjectionApplied = false;
        var activitySmaliPatchApplied = false;

        try
        {
            var decompileResult = await _apktoolService.DecompileAsync(request.InputApkPath, decompiledDirectory, request.DecodeResources, request.DecodeSources, cancellationToken);
            if (decompileResult.ExitCode != 0)
            {
                var decompileSummary = BuildCompactFailureSummary("Decompile", decompileResult);
                result.Errors.Add(decompileSummary);
                result.StageSummaries.Add(new PatchStageSummary("decompile", false, BuildFailureStageMessage("Decompile", decompileResult)));
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

            if (request.ScriptInjectionProfile == ScriptInjectionProfile.SampleInjection)
            {
                const string sampleInjectionMessage = "Sample injection profile selected; skipping Frida artifact resolution and gadget copy.";
                result.Warnings.Add(sampleInjectionMessage);
                result.StageSummaries.Add(new PatchStageSummary("sample-injection", true, sampleInjectionMessage));

                if (!request.DecodeSources)
                {
                    const string sampleSmaliSkipMessage = "Sample smali patch skipped because source decoding is disabled.";
                    result.Warnings.Add(sampleSmaliSkipMessage);
                    result.StageSummaries.Add(new PatchStageSummary("smali-patch", true, sampleSmaliSkipMessage));
                }
                else
                {
                    var smaliPatch = await _smaliPatchService.PatchAsync(
                        decompiledDirectory,
                        activityName,
                        request.ScriptInjectionProfile,
                        request.UseDelayedLoad,
                        cancellationToken);
                    if (!smaliPatch.Success)
                    {
                        result.Errors.Add(smaliPatch.Error ?? "Sample smali patch failed.");
                        result.StageSummaries.Add(new PatchStageSummary("smali-patch", false, result.Errors.Last()));
                        return result;
                    }

                    result.StageSummaries.Add(new PatchStageSummary("smali-patch", true, "Sample smali patch applied."));
                    smaliInjectionApplied = true;
                    activitySmaliPatchApplied = true;
                }
            }
            else
            {
                var injectionArchitectures = ResolveInjectionArchitectures(decompiledDirectory, architecture, request.InjectForAllArchitectures);
                var optionalAssetWarnings = new HashSet<string>(StringComparer.Ordinal);
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
                        AddOptionalAssetWarning(result, optionalAssetWarnings, gadgetInject.ScriptStatus, "script");
                        AddOptionalAssetWarning(result, optionalAssetWarnings, gadgetInject.ConfigStatus, "config");
                        return result;
                    }

                    AddOptionalAssetWarning(result, optionalAssetWarnings, gadgetInject.ScriptStatus, "script");
                    AddOptionalAssetWarning(result, optionalAssetWarnings, gadgetInject.ConfigStatus, "config");
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
                    var smaliPatch = await _smaliPatchService.PatchAsync(
                        decompiledDirectory,
                        activityName,
                        request.ScriptInjectionProfile,
                        request.UseDelayedLoad,
                        cancellationToken);
                    if (!smaliPatch.Success)
                    {
                        if (IsRecoverableActivityPatchFailure(smaliPatch.Error))
                        {
                            var warningMessage = smaliPatch.Error!;
                            result.Warnings.Add(warningMessage);
                            result.StageSummaries.Add(new PatchStageSummary("smali-patch", true, warningMessage));
                            smaliInjectionApplied = true;
                        }
                        else
                        {
                            result.Errors.Add(smaliPatch.Error ?? "Smali patch failed.");
                            result.StageSummaries.Add(new PatchStageSummary("smali-patch", false, result.Errors.Last()));
                            return result;
                        }
                    }
                    else
                    {
                        result.StageSummaries.Add(new PatchStageSummary("smali-patch", true, "Smali patched."));
                        smaliInjectionApplied = true;
                        activitySmaliPatchApplied = true;
                    }
                }
            }

            var rebuiltApkPath = Path.Combine(jobDirectory, "rebuilt.apk");
            var initialBuildResult = await _apktoolService.BuildAsync(decompiledDirectory, rebuiltApkPath, request.UseAapt2ForBuild, cancellationToken);
            if (initialBuildResult.ExitCode == 0)
            {
                result.StageSummaries.Add(new PatchStageSummary("build", true, $"Build attempt 1 succeeded (useAapt2={request.UseAapt2ForBuild})."));
            }
            else if (!request.UseAapt2ForBuild)
            {
                result.StageSummaries.Add(new PatchStageSummary("build", true, BuildFailureStageMessage("Build attempt 1", initialBuildResult, useAapt2: false)));
                result.StageSummaries.Add(new PatchStageSummary("build", true, "Build fallback attempt 2 started (useAapt2=true)."));

                var fallbackBuildResult = await _apktoolService.BuildAsync(decompiledDirectory, rebuiltApkPath, useAapt2: true, cancellationToken);
                if (fallbackBuildResult.ExitCode != 0)
                {
                    result.StageSummaries.Add(new PatchStageSummary("build", false, BuildFailureStageMessage("Build fallback attempt 2", fallbackBuildResult, useAapt2: true)));
                    var initialSummary = BuildCompactFailureSummary("Build attempt 1", initialBuildResult, useAapt2: false);
                    var fallbackSummary = BuildCompactFailureSummary("Build fallback attempt 2", fallbackBuildResult, useAapt2: true);
                    result.Errors.Add($"{initialSummary} {fallbackSummary}");
                    return result;
                }

                result.StageSummaries.Add(new PatchStageSummary("build", true, "Build fallback attempt 2 succeeded (useAapt2=true)."));
            }
            else
            {
                result.Errors.Add(BuildCompactFailureSummary("Build", initialBuildResult, useAapt2: true));
                result.StageSummaries.Add(new PatchStageSummary("build", false, BuildFailureStageMessage("Build", initialBuildResult, useAapt2: true)));
                return result;
            }

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
                    rebuiltApkPath,
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

            var finalArtifactPath = rebuiltApkPath;
            var finalOutputPath = request.OutputApkPath;

            if (request.SignOutput)
            {
                var signedPath = Path.Combine(jobDirectory, "signed.apk");
                var signResult = await _signingService.SignAsync(rebuiltApkPath, signedPath, cancellationToken);
                if (!signResult.Success)
                {
                    var publishedUnsignedPath = PublishArtifactWithRetry(rebuiltApkPath, request.OutputApkPath);
                    result.Errors.Add(signResult.Error ?? "Signing failed.");
                    result.Warnings.Add("Unsigned rebuilt APK was preserved.");
                    result.StageSummaries.Add(new PatchStageSummary("signing", false, result.Errors.Last()));
                    result.OutputApkPath = publishedUnsignedPath;
                    result.SelectedArchitecture = architecture;
                    return result;
                }

                result.StageSummaries.Add(new PatchStageSummary("signing", true, "Signed APK created."));
                finalArtifactPath = signResult.SignedApkPath!;
                finalOutputPath = GetSignedPath(request.OutputApkPath);
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
                var methodReference = request.ScriptInjectionProfile != ScriptInjectionProfile.SampleInjection && !activitySmaliPatchApplied
                    ? ResolveApplicationMethodReference(decompiledDirectory)
                    : request.ScriptInjectionProfile == ScriptInjectionProfile.SampleInjection
                    ? "logSampleInjectionApplied"
                    : request.UseDelayedLoad
                        ? "loadFridaGadgetIfNeeded"
                        : "loadFridaGadget";
                methodReference = methodReference.Contains("->", StringComparison.Ordinal)
                    ? methodReference
                    : $"{classDescriptor}->{methodReference}()V";
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

                    result.OutputApkPath = PublishArtifactWithRetry(finalArtifactPath, finalOutputPath);
                    result.SelectedArchitecture = architecture;
                    result.UsedSigning = request.SignOutput;
                    return result;
                }

                result.StageSummaries.Add(new PatchStageSummary("dex-verification", true, $"Confirmed '{methodReference}' in final APK dex. {inspection.Diagnostics}"));
            }

            result.Success = true;
            result.OutputApkPath = PublishArtifactWithRetry(finalArtifactPath, finalOutputPath);
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
                    AbiContainsNativeLibraries(abiPath) &&
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

    private static bool AbiContainsNativeLibraries(string abiPath)
    {
        try
        {
            return Directory
                .EnumerateFiles(abiPath, "*.so", SearchOption.AllDirectories)
                .Any();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string PublishArtifactWithRetry(string sourcePath, string outputPath)
    {
        if (Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
        {
            return outputPath;
        }

        var destinationDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                File.Move(sourcePath, outputPath);
                return outputPath;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(150 * attempt));
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(150 * attempt));
            }
        }

        File.Copy(sourcePath, outputPath, overwrite: true);
        return outputPath;
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

    private static void AddOptionalAssetWarning(
        PatchResult result,
        HashSet<string> warnings,
        OptionalAssetCopyResult status,
        string assetName)
    {
        if (status.Status == OptionalAssetCopyStatus.Copied)
        {
            return;
        }

        if (status.Status is not (OptionalAssetCopyStatus.Missing or OptionalAssetCopyStatus.Skipped or OptionalAssetCopyStatus.Error))
        {
            return;
        }

        var warning = $"Optional {assetName} status: {status.Status} - {status.Detail}";
        if (warnings.Add(warning))
        {
            result.Warnings.Add(warning);
        }
    }

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

    private static bool IsRecoverableActivityPatchFailure(string? error)
        => !string.IsNullOrWhiteSpace(error) &&
           error.Contains(SmaliPatchService.ActivityInjectionPointFailureWithApplicationPatchPrefix, StringComparison.Ordinal) &&
           error.Contains("injection point", StringComparison.OrdinalIgnoreCase);

    private static string ResolveApplicationMethodReference(string decompiledDirectory)
    {
        var manifestPath = Path.Combine(decompiledDirectory, "AndroidManifest.xml");
        var packageName = "com.pulseapk.generated";
        var applicationName = string.Empty;

        if (File.Exists(manifestPath))
        {
            var manifestContent = File.ReadAllText(manifestPath);
            var packageMatch = Regex.Match(manifestContent, @"\bpackage\s*=\s*['""](?<package>[^'""]+)['""]", RegexOptions.CultureInvariant);
            if (packageMatch.Success)
            {
                packageName = packageMatch.Groups["package"].Value;
            }

            var applicationMatch = Regex.Match(manifestContent, @"<application\b[^>]*\bandroid:name\s*=\s*['""](?<name>[^'""]+)['""]", RegexOptions.CultureInvariant);
            if (applicationMatch.Success)
            {
                applicationName = applicationMatch.Groups["name"].Value.Trim();
            }
        }

        var applicationFqcn = ResolveApplicationFqcn(packageName, applicationName);
        var descriptor = $"L{applicationFqcn.Replace('.', '/')};";
        return $"{descriptor}->loadFridaGadgetSafely()V";
    }

    private static string ResolveApplicationFqcn(string packageName, string? applicationName)
    {
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            return $"{packageName}.PulseFridaApplication";
        }

        if (applicationName.StartsWith(".", StringComparison.Ordinal))
        {
            return packageName + applicationName;
        }

        if (!applicationName.Contains(".", StringComparison.Ordinal))
        {
            return $"{packageName}.{applicationName}";
        }

        return applicationName;
    }

    private static string BuildCompactFailureSummary(string operation, ApktoolRunResult runResult, bool? useAapt2 = null)
    {
        var optionSuffix = useAapt2.HasValue ? $" (useAapt2={useAapt2.Value.ToString().ToLowerInvariant()})" : string.Empty;
        var tail = GetRelevantTail(runResult, 4);
        var detail = tail.Count == 0
            ? "No stderr output captured."
            : string.Join(" | ", tail.Select(CompactLine));
        return $"{operation} failed with exit code {runResult.ExitCode}{optionSuffix}. {detail}";
    }

    private static string BuildFailureStageMessage(string operation, ApktoolRunResult runResult, bool? useAapt2 = null)
    {
        var optionSuffix = useAapt2.HasValue ? $" (useAapt2={useAapt2.Value.ToString().ToLowerInvariant()})" : string.Empty;
        var tail = GetRelevantTail(runResult, 20);
        if (tail.Count == 0)
        {
            return $"{operation} failed with exit code {runResult.ExitCode}{optionSuffix}. No stderr output captured.";
        }

        return $"{operation} failed with exit code {runResult.ExitCode}{optionSuffix}. Last relevant log lines:{Environment.NewLine}{string.Join(Environment.NewLine, tail)}";
    }

    private static List<string> GetRelevantTail(ApktoolRunResult runResult, int maxLines)
    {
        var preferred = runResult.Stderr
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(maxLines)
            .ToList();
        if (preferred.Count > 0)
        {
            return preferred;
        }

        return runResult.Stdout
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(maxLines)
            .ToList();
    }

    private static string CompactLine(string line)
    {
        const int maxLength = 180;
        var normalized = Regex.Replace(line, @"\s+", " ").Trim();
        return normalized.Length <= maxLength ? normalized : $"{normalized[..maxLength]}...";
    }
}
