using System.Text.RegularExpressions;
using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Models;

namespace PulseAPK.Core.Services.Patching;

public sealed class SmaliPatchService : ISmaliPatchService
{
    internal const string ActivityInjectionPointFailureWithApplicationPatchPrefix = "Application smali patch applied, but activity patch failed:";

    public Task<(bool Success, string? Error)> PatchAsync(
        string decompiledDirectory,
        string activityName,
        ScriptInjectionProfile profile,
        bool useDelayedLoad,
        CancellationToken cancellationToken = default)
    {
        if (profile != ScriptInjectionProfile.SampleInjection)
        {
            var applicationPatch = PatchApplicationSmali(decompiledDirectory);
            if (!applicationPatch.Success)
            {
                return Task.FromResult(applicationPatch);
            }
        }

        var smaliFile = ResolveActivitySmaliFile(decompiledDirectory, activityName);
        if (smaliFile is null)
        {
            return Task.FromResult<(bool Success, string? Error)>((false, $"Could not locate smali file for activity '{activityName}'."));
        }

        var originalContent = File.ReadAllText(smaliFile);

        var classDescriptor = ExtractClassDescriptor(originalContent);
        if (string.IsNullOrWhiteSpace(classDescriptor))
        {
            return Task.FromResult<(bool Success, string? Error)>((false, "Unable to determine class descriptor from smali file."));
        }

        var superClassDescriptor = ExtractSuperClassDescriptor(originalContent);
        if (string.IsNullOrWhiteSpace(superClassDescriptor))
        {
            return Task.FromResult<(bool Success, string? Error)>((false, $"Unable to determine superclass descriptor from smali file '{smaliFile}'."));
        }

        var lifecycleMethodName = profile == ScriptInjectionProfile.SampleInjection || !useDelayedLoad ? "onCreate" : "onResume";
        var lifecycleSignature = lifecycleMethodName == "onResume" ? "()V" : "(Landroid/os/Bundle;)V";
        var patched = originalContent;

        if (profile == ScriptInjectionProfile.SampleInjection)
        {
            patched = EnsureSampleInjectionHelperMethod(patched);
        }
        else if (useDelayedLoad)
        {
            patched = EnsureDelayedLoadHelperMembers(patched, classDescriptor);
        }
        else
        {
            patched = EnsureImmediateLoadHelperMethod(patched);
        }

        patched = InjectCallIntoLifecycleMethod(patched, classDescriptor, lifecycleMethodName, lifecycleSignature, superClassDescriptor, profile);
        patched = EnsureHelpersForReferencedCalls(patched, classDescriptor);

        if (HasMissingStaticHelperForReferencedCalls(patched, classDescriptor))
        {
            return Task.FromResult<(bool Success, string? Error)>((false, "Patched smali references Frida helper methods that are missing static definitions."));
        }

        if (ReferenceEquals(patched, originalContent) || patched == originalContent)
        {
            if (profile != ScriptInjectionProfile.SampleInjection)
            {
                return Task.FromResult<(bool Success, string? Error)>((false, $"{ActivityInjectionPointFailureWithApplicationPatchPrefix} Unable to find an injection point in activity smali file."));
            }

            return Task.FromResult<(bool Success, string? Error)>((false, "Unable to find an injection point in activity smali file."));
        }

        File.WriteAllText(smaliFile, patched);
        return Task.FromResult<(bool Success, string? Error)>((true, null));
    }

    private static IReadOnlyList<string> BuildImmediateLoadHelperMethods()
    {
        return
        [
            ".method private static loadFridaGadget()V",
            "    .locals 3",
            string.Empty,
            "    :try_start_0",
            "    const-string v0, \"frida-gadget\"",
            "    invoke-static {v0}, Ljava/lang/System;->loadLibrary(Ljava/lang/String;)V",
            "    const-string v0, \"FridaGadget\"",
            "    const-string v1, \"Frida gadget loaded in activity lifecycle; you can attach now.\"",
            "    invoke-static {v0, v1}, Landroid/util/Log;->i(Ljava/lang/String;Ljava/lang/String;)I",
            "    move-result v0",
            "    :try_end_0",
            "    .catch Ljava/lang/Throwable; {:try_start_0 .. :try_end_0} :catch_0",
            string.Empty,
            "    goto :done",
            string.Empty,
            "    :catch_0",
            "    move-exception v0",
            "    const-string v1, \"FridaGadget\"",
            "    const-string v2, \"Failed to load frida-gadget\"",
            "    invoke-static {v1, v2, v0}, Landroid/util/Log;->e(Ljava/lang/String;Ljava/lang/String;Ljava/lang/Throwable;)I",
            "    move-result v1",
            string.Empty,
            "    :done",
            "    return-void",
            ".end method",
            string.Empty
        ];
    }

    private static IReadOnlyList<string> BuildDelayedLoadHelperMethods()
    {
        return
        [
            ".field private static sFridaLoaded:Z",
            string.Empty,
            ".method private static loadFridaGadgetIfNeeded()V",
            "    .locals 3",
            string.Empty,
            "    sget-boolean v0, Lcom/example/PLACEHOLDER;->sFridaLoaded:Z",
            "    if-nez v0, :loaded",
            string.Empty,
            "    :try_start_0",
            "    const-string v0, \"frida-gadget\"",
            "    invoke-static {v0}, Ljava/lang/System;->loadLibrary(Ljava/lang/String;)V",
            "    const-string v0, \"FridaGadget\"",
            "    const-string v1, \"Frida gadget loaded in activity lifecycle; you can attach now.\"",
            "    invoke-static {v0, v1}, Landroid/util/Log;->i(Ljava/lang/String;Ljava/lang/String;)I",
            "    move-result v0",
            "    const/4 v0, 0x1",
            "    sput-boolean v0, Lcom/example/PLACEHOLDER;->sFridaLoaded:Z",
            "    :try_end_0",
            "    .catch Ljava/lang/Throwable; {:try_start_0 .. :try_end_0} :catch_0",
            string.Empty,
            "    goto :loaded",
            string.Empty,
            "    :catch_0",
            "    move-exception v0",
            "    const-string v1, \"FridaGadget\"",
            "    const-string v2, \"Failed to load frida-gadget\"",
            "    invoke-static {v1, v2, v0}, Landroid/util/Log;->e(Ljava/lang/String;Ljava/lang/String;Ljava/lang/Throwable;)I",
            "    move-result v1",
            string.Empty,
            "    :loaded",
            "    return-void",
            ".end method",
            string.Empty
        ];
    }

    private static IReadOnlyList<string> BuildSampleInjectionHelperMethods()
    {
        return
        [
            ".method private static logSampleInjectionApplied()V",
            "    .locals 2",
            string.Empty,
            "    const-string v0, \"PulseAPK\"",
            "    const-string v1, \"PulseAPK: The app sample patch was applied\"",
            "    invoke-static {v0, v1}, Landroid/util/Log;->i(Ljava/lang/String;Ljava/lang/String;)I",
            "    move-result v0",
            "    return-void",
            ".end method",
            string.Empty
        ];
    }

    private static string? ResolveActivitySmaliFile(string decompiledDirectory, string activityName)
    {
        if (string.IsNullOrWhiteSpace(activityName))
        {
            return null;
        }

        var normalizedActivityName = NormalizeActivityName(decompiledDirectory, activityName);
        var expectedClassDescriptor = "L" + normalizedActivityName.TrimStart('.').Replace('.', '/') + ";";
        var relativePath = normalizedActivityName.TrimStart('.').Replace('.', Path.DirectorySeparatorChar) + ".smali";
        var fallbackFileName = normalizedActivityName.TrimStart('.');
        if (fallbackFileName.Contains(Path.DirectorySeparatorChar) || fallbackFileName.Contains(Path.AltDirectorySeparatorChar))
        {
            fallbackFileName = Path.GetFileName(fallbackFileName);
        }

        fallbackFileName += ".smali";

        foreach (var smaliRoot in Directory.EnumerateDirectories(decompiledDirectory, "smali*", SearchOption.TopDirectoryOnly))
        {
            var direct = Path.Combine(smaliRoot, relativePath);
            if (File.Exists(direct) && SmaliFileMatchesClassDescriptor(direct, expectedClassDescriptor))
            {
                return direct;
            }

            var match = Directory.EnumerateFiles(smaliRoot, Path.GetFileName(relativePath), SearchOption.AllDirectories)
                .FirstOrDefault(path =>
                    path.EndsWith(relativePath, StringComparison.OrdinalIgnoreCase) &&
                    SmaliFileMatchesClassDescriptor(path, expectedClassDescriptor));
            if (match is not null)
            {
                return match;
            }

            if (fallbackFileName.Contains(Path.DirectorySeparatorChar) || fallbackFileName.Contains(Path.AltDirectorySeparatorChar))
            {
                continue;
            }

            var fallbackMatch = Directory.EnumerateFiles(smaliRoot, fallbackFileName, SearchOption.AllDirectories)
                .FirstOrDefault(path => SmaliFileMatchesClassDescriptor(path, expectedClassDescriptor));
            if (fallbackMatch is not null)
            {
                return fallbackMatch;
            }
        }

        return null;
    }

    private static bool SmaliFileMatchesClassDescriptor(string smaliFilePath, string expectedClassDescriptor)
    {
        var fileContent = File.ReadAllText(smaliFilePath);
        var descriptor = ExtractClassDescriptor(fileContent);
        return string.Equals(descriptor, expectedClassDescriptor, StringComparison.Ordinal);
    }


    private static string NormalizeActivityName(string decompiledDirectory, string activityName)
    {
        var trimmedActivityName = activityName.Trim();
        if (trimmedActivityName.StartsWith(".", StringComparison.Ordinal))
        {
            var packageName = ReadPackageName(decompiledDirectory);
            if (!string.IsNullOrWhiteSpace(packageName))
            {
                return packageName + trimmedActivityName;
            }

            return trimmedActivityName;
        }

        if (trimmedActivityName.Contains(".", StringComparison.Ordinal))
        {
            return trimmedActivityName;
        }

        var manifestPackageName = ReadPackageName(decompiledDirectory);
        return string.IsNullOrWhiteSpace(manifestPackageName) ? trimmedActivityName : $"{manifestPackageName}.{trimmedActivityName}";
    }

    private static string? ReadPackageName(string decompiledDirectory)
    {
        var manifestPath = Path.Combine(decompiledDirectory, "AndroidManifest.xml");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var manifestContent = File.ReadAllText(manifestPath);
        var packageMatch = Regex.Match(manifestContent, @"\bpackage\s*=\s*['""](?<package>[^'""]+)['""]");
        return packageMatch.Success ? packageMatch.Groups["package"].Value : null;
    }

    private static string? ExtractClassDescriptor(string content)
    {
        var match = Regex.Match(content, @"\.class\s+[\w\s-]+\s+(L[^;]+;)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractSuperClassDescriptor(string content)
    {
        var match = Regex.Match(content, @"(?m)^\.super\s+(L[^;]+;)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string EnsureImmediateLoadHelperMethod(string content)
    {
        content = EnsureHelperMethodIsStatic(content, "loadFridaGadget");

        if (HasStaticHelperMethod(content, "loadFridaGadget"))
        {
            return content;
        }

        return InsertLinesBeforeEndClass(content, BuildImmediateLoadHelperMethods());
    }

    private static string EnsureDelayedLoadHelperMembers(string content, string classDescriptor)
    {
        if (!content.Contains(".field private static sFridaLoaded:Z", StringComparison.Ordinal))
        {
            content = InsertLinesBeforeEndClass(content,
            [
                ".field private static sFridaLoaded:Z",
                string.Empty
            ]);
        }

        content = EnsureHelperMethodIsStatic(content, "loadFridaGadgetIfNeeded");

        if (HasStaticHelperMethod(content, "loadFridaGadgetIfNeeded"))
        {
            return content;
        }

        var helperMethodLines = BuildDelayedLoadHelperMethods()
            .Where(line => !line.StartsWith(".field ", StringComparison.Ordinal))
            .ToArray();

        var normalizedLines = helperMethodLines
            .Select(line => line.Replace("Lcom/example/PLACEHOLDER;", classDescriptor, StringComparison.Ordinal))
            .ToArray();

        return InsertLinesBeforeEndClass(content, normalizedLines);
    }

    private static string EnsureSampleInjectionHelperMethod(string content)
    {
        content = EnsureHelperMethodIsStatic(content, "logSampleInjectionApplied");
        if (HasStaticHelperMethod(content, "logSampleInjectionApplied"))
        {
            return content;
        }

        return InsertLinesBeforeEndClass(content, BuildSampleInjectionHelperMethods());
    }

    private static string EnsureHelpersForReferencedCalls(string content, string classDescriptor)
    {
        if (content.Contains($"{classDescriptor}->loadFridaGadget()V", StringComparison.Ordinal) &&
            !HasStaticHelperMethod(content, "loadFridaGadget"))
        {
            content = EnsureImmediateLoadHelperMethod(content);
        }

        if (content.Contains($"{classDescriptor}->loadFridaGadgetIfNeeded()V", StringComparison.Ordinal) &&
            !HasStaticHelperMethod(content, "loadFridaGadgetIfNeeded"))
        {
            content = EnsureDelayedLoadHelperMembers(content, classDescriptor);
        }

        if (content.Contains($"{classDescriptor}->logSampleInjectionApplied()V", StringComparison.Ordinal) &&
            !HasStaticHelperMethod(content, "logSampleInjectionApplied"))
        {
            content = EnsureSampleInjectionHelperMethod(content);
        }

        return content;
    }

    private static bool HasMissingStaticHelperForReferencedCalls(string content, string classDescriptor)
    {
        var referencesImmediateHelper = content.Contains($"{classDescriptor}->loadFridaGadget()V", StringComparison.Ordinal);
        if (referencesImmediateHelper && !HasStaticHelperMethod(content, "loadFridaGadget"))
        {
            return true;
        }

        var referencesDelayedHelper = content.Contains($"{classDescriptor}->loadFridaGadgetIfNeeded()V", StringComparison.Ordinal);
        if (referencesDelayedHelper && !HasStaticHelperMethod(content, "loadFridaGadgetIfNeeded"))
        {
            return true;
        }

        var referencesSampleHelper = content.Contains($"{classDescriptor}->logSampleInjectionApplied()V", StringComparison.Ordinal);
        return referencesSampleHelper && !HasStaticHelperMethod(content, "logSampleInjectionApplied");
    }

    private static string EnsureHelperMethodIsStatic(string content, string methodName)
    {
        var methodPattern = new Regex($@"(?m)^(?<indent>\s*)\.method\s+(?<modifiers>[^\n]*?)\b{Regex.Escape(methodName)}\s*\(\s*\)\s*V\s*$");
        var match = methodPattern.Match(content);
        if (!match.Success)
        {
            return content;
        }

        var modifiers = match.Groups["modifiers"].Value
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !string.Equals(token, "static", StringComparison.Ordinal))
            .ToList();
        modifiers.Add("static");
        var normalizedModifiers = string.Join(" ", modifiers);
        var indent = match.Groups["indent"].Value;
        var updatedSignature = $"{indent}.method {normalizedModifiers} {methodName}()V";
        return content[..match.Index] + updatedSignature + content[(match.Index + match.Length)..];
    }

    private static bool HasStaticHelperMethod(string content, string methodName)
    {
        var staticMethodPattern = new Regex($@"(?m)^\s*\.method\s+[^\n]*\bstatic\b[^\n]*\b{Regex.Escape(methodName)}\s*\(\s*\)\s*V\s*$");
        return staticMethodPattern.IsMatch(content);
    }

    private static string InsertLinesBeforeEndClass(string content, IReadOnlyList<string> lines)
    {
        var method = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        var endClassMatch = FindLastEndClassDirective(content);
        if (endClassMatch is null)
        {
            if (!content.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                content += Environment.NewLine;
            }

            return content + method;
        }

        return content.Insert(endClassMatch.Index, method);
    }

    private static string InjectCallIntoLifecycleMethod(
        string content,
        string classDescriptor,
        string methodName,
        string methodSignature,
        string superClassDescriptor,
        ScriptInjectionProfile profile)
    {
        var helperMethodName = profile == ScriptInjectionProfile.SampleInjection
            ? "logSampleInjectionApplied"
            : methodName == "onResume" ? "loadFridaGadgetIfNeeded" : "loadFridaGadget";
        var methodPattern = new Regex($@"(?ms)(\.method[^\n]* {methodName}{Regex.Escape(methodSignature)}\s+)(.*?)(\.end method)");
        var match = methodPattern.Match(content);

        if (match.Success)
        {
            var body = match.Groups[2].Value;
            if (body.Contains(helperMethodName, StringComparison.Ordinal))
            {
                return content;
            }

            var call = "    invoke-static {}, " + classDescriptor + $"->{helperMethodName}()V";
            var superCallPattern = new Regex($@"(?m)^(?<indent>\s*)invoke-super \{{[^\n]+\}}, [^\n]+->{methodName}{Regex.Escape(methodSignature)}\s*$");
            var superCallMatch = superCallPattern.Match(body);
            if (superCallMatch.Success)
            {
                var superCallEndIndex = superCallMatch.Index + superCallMatch.Length;
                var callLine = Environment.NewLine + superCallMatch.Groups["indent"].Value + call.TrimStart();
                body = body.Insert(superCallEndIndex, callLine);
            }
            else
            {
                var split = body.Split(Environment.NewLine);
                var insertAt = Array.FindIndex(split, line => line.TrimStart().StartsWith(".locals", StringComparison.Ordinal) || line.TrimStart().StartsWith(".registers", StringComparison.Ordinal));
                if (insertAt >= 0)
                {
                    var lines = split.ToList();
                    lines.Insert(insertAt + 1, call);
                    body = string.Join(Environment.NewLine, lines);
                }
                else
                {
                    return content;
                }
            }

            return content[..match.Groups[2].Index] + body + content[(match.Groups[2].Index + match.Groups[2].Length)..];
        }

        var newMethod = methodName == "onResume"
            ? $".method protected onResume()V{Environment.NewLine}" +
              "    .locals 0" + Environment.NewLine +
              $"    invoke-super {{p0}}, {superClassDescriptor}->onResume()V{Environment.NewLine}" +
              $"    invoke-static {{}}, {classDescriptor}->{helperMethodName}()V{Environment.NewLine}" +
              "    return-void" + Environment.NewLine +
              ".end method" + Environment.NewLine + Environment.NewLine
            : $".method protected onCreate(Landroid/os/Bundle;)V{Environment.NewLine}" +
              "    .locals 0" + Environment.NewLine +
              $"    invoke-super {{p0, p1}}, {superClassDescriptor}->onCreate(Landroid/os/Bundle;)V{Environment.NewLine}" +
              $"    invoke-static {{}}, {classDescriptor}->{helperMethodName}()V{Environment.NewLine}" +
              "    return-void" + Environment.NewLine +
              ".end method" + Environment.NewLine + Environment.NewLine;

        var endClassMatch = FindLastEndClassDirective(content);
        if (endClassMatch is null)
        {
            return content;
        }

        return content.Insert(endClassMatch.Index, newMethod);
    }

    private static Match? FindLastEndClassDirective(string content)
    {
        var matches = Regex.Matches(content, @"(?m)^\s*\.end\s+class\b");
        if (matches.Count == 0)
        {
            return null;
        }

        return matches[^1];
    }

    private static (bool Success, string? Error) PatchApplicationSmali(string decompiledDirectory)
    {
        var (applicationDescriptor, applicationSmaliPath) = ResolveOrCreateApplicationClass(decompiledDirectory);
        if (string.IsNullOrWhiteSpace(applicationDescriptor) || string.IsNullOrWhiteSpace(applicationSmaliPath))
        {
            return (false, "Unable to resolve Application class for Frida gadget loading.");
        }

        var originalContent = File.Exists(applicationSmaliPath) ? File.ReadAllText(applicationSmaliPath) : BuildDefaultApplicationSmali(applicationDescriptor);
        var superClass = ExtractSuperClassDescriptor(originalContent) ?? "Landroid/app/Application;";
        var patched = originalContent;

        patched = EnsureApplicationGadgetMembers(patched, applicationDescriptor);
        patched = EnsureAttachBaseContextLoadsGadget(patched, applicationDescriptor, superClass);
        patched = EnsureOnCreateGuardLoadsGadget(patched, applicationDescriptor, superClass);

        if (!string.Equals(patched, originalContent, StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(applicationSmaliPath)!);
            File.WriteAllText(applicationSmaliPath, patched);
        }

        return (true, null);
    }

    private static (string Descriptor, string SmaliPath) ResolveOrCreateApplicationClass(string decompiledDirectory)
    {
        var manifestPath = Path.Combine(decompiledDirectory, "AndroidManifest.xml");
        var packageName = ReadPackageName(decompiledDirectory) ?? "com.pulseapk.generated";
        var applicationName = ReadApplicationName(manifestPath);

        var fqcn = ResolveApplicationFqcn(packageName, applicationName);
        var descriptor = $"L{fqcn.Replace('.', '/')};";
        var relativePath = fqcn.Replace('.', Path.DirectorySeparatorChar) + ".smali";

        var smaliRoots = Directory.EnumerateDirectories(decompiledDirectory, "smali*", SearchOption.TopDirectoryOnly).ToList();
        if (smaliRoots.Count == 0)
        {
            var root = Path.Combine(decompiledDirectory, "smali");
            Directory.CreateDirectory(root);
            smaliRoots.Add(root);
        }

        foreach (var smaliRoot in smaliRoots)
        {
            var direct = Path.Combine(smaliRoot, relativePath);
            if (File.Exists(direct))
            {
                return (descriptor, direct);
            }
        }

        return (descriptor, Path.Combine(smaliRoots[0], relativePath));
    }

    private static string? ReadApplicationName(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var manifestContent = File.ReadAllText(manifestPath);
        var applicationMatch = Regex.Match(manifestContent, @"<application\b[^>]*\bandroid:name\s*=\s*['""](?<name>[^'""]+)['""]");
        return applicationMatch.Success ? applicationMatch.Groups["name"].Value : null;
    }

    private static string ResolveApplicationFqcn(string packageName, string? applicationName)
    {
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            return $"{packageName}.PulseFridaApplication";
        }

        var trimmed = applicationName.Trim();
        if (trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            return packageName + trimmed;
        }

        if (!trimmed.Contains(".", StringComparison.Ordinal))
        {
            return $"{packageName}.{trimmed}";
        }

        return trimmed;
    }

    private static string BuildDefaultApplicationSmali(string applicationDescriptor)
    {
        return
            $".class public {applicationDescriptor}{Environment.NewLine}" +
            ".super Landroid/app/Application;" + Environment.NewLine + Environment.NewLine +
            ".method public constructor <init>()V" + Environment.NewLine +
            "    .locals 0" + Environment.NewLine + Environment.NewLine +
            "    invoke-direct {p0}, Landroid/app/Application;-><init>()V" + Environment.NewLine + Environment.NewLine +
            "    return-void" + Environment.NewLine +
            ".end method" + Environment.NewLine + Environment.NewLine +
            ".end class";
    }

    private static string EnsureApplicationGadgetMembers(string content, string classDescriptor)
    {
        if (!content.Contains(".field private static gadgetLoaded:Z", StringComparison.Ordinal))
        {
            content = InsertFieldBeforeFirstMethod(content, ".field private static gadgetLoaded:Z");
        }

        if (!HasStaticHelperMethod(content, "loadFridaGadgetSafely"))
        {
            var lines = new[]
            {
                ".method private static loadFridaGadgetSafely()V",
                "    .locals 3",
                "",
                $"    sget-boolean v0, {classDescriptor}->gadgetLoaded:Z",
                "    if-nez v0, :done",
                "",
                "    :try_start_0",
                "    const-string v0, \"frida-gadget\"",
                "    invoke-static {v0}, Ljava/lang/System;->loadLibrary(Ljava/lang/String;)V",
                "    const/4 v0, 0x1",
                $"    sput-boolean v0, {classDescriptor}->gadgetLoaded:Z",
                "    const-string v0, \"FridaGadget\"",
                "    const-string v1, \"Frida gadget loaded in attachBaseContext; you can attach now.\"",
                "    invoke-static {v0, v1}, Landroid/util/Log;->i(Ljava/lang/String;Ljava/lang/String;)I",
                "    move-result v0",
                "    :try_end_0",
                "    .catch Ljava/lang/Throwable; {:try_start_0 .. :try_end_0} :catch_0",
                "",
                "    goto :done",
                "",
                "    :catch_0",
                "    move-exception v0",
                "    const-string v1, \"FridaGadget\"",
                "    const-string v2, \"Failed to load frida-gadget\"",
                "    invoke-static {v1, v2, v0}, Landroid/util/Log;->e(Ljava/lang/String;Ljava/lang/String;Ljava/lang/Throwable;)I",
                "    move-result v1",
                "",
                "    :done",
                "    return-void",
                ".end method",
                ""
            };
            content = InsertLinesBeforeEndClass(content, lines);
        }

        return content;
    }

    private static string InsertFieldBeforeFirstMethod(string content, string fieldLine)
    {
        var methodMatch = Regex.Match(content, @"(?m)^\s*\.method\b");
        if (methodMatch.Success)
        {
            var fieldBlock = fieldLine + Environment.NewLine + Environment.NewLine;
            return content.Insert(methodMatch.Index, fieldBlock);
        }

        return InsertLinesBeforeEndClass(content, [fieldLine, string.Empty]);
    }

    private static string EnsureAttachBaseContextLoadsGadget(string content, string classDescriptor, string superClassDescriptor)
    {
        return EnsureLifecycleMethodCall(
            content,
            classDescriptor,
            "attachBaseContext",
            "(Landroid/content/Context;)V",
            $"invoke-super {{p0, p1}}, {superClassDescriptor}->attachBaseContext(Landroid/content/Context;)V",
            "    invoke-static {}, " + classDescriptor + "->loadFridaGadgetSafely()V");
    }

    private static string EnsureOnCreateGuardLoadsGadget(string content, string classDescriptor, string superClassDescriptor)
    {
        return EnsureLifecycleMethodCall(
            content,
            classDescriptor,
            "onCreate",
            "()V",
            $"invoke-super {{p0}}, {superClassDescriptor}->onCreate()V",
            "    invoke-static {}, " + classDescriptor + "->loadFridaGadgetSafely()V");
    }

    private static string EnsureLifecycleMethodCall(
        string content,
        string classDescriptor,
        string methodName,
        string methodSignature,
        string superInvokeLine,
        string helperInvokeLine)
    {
        var methodPattern = new Regex($@"(?ms)(\.method[^\n]* {methodName}{Regex.Escape(methodSignature)}\s+)(.*?)(\.end method)");
        var match = methodPattern.Match(content);
        if (match.Success)
        {
            var body = match.Groups[2].Value;
            if (!body.Contains("loadFridaGadgetSafely", StringComparison.Ordinal))
            {
                var superCallPattern = new Regex($@"(?m)^(?<indent>\s*)invoke-super \{{[^\n]+\}}, [^\n]+->{methodName}{Regex.Escape(methodSignature)}\s*$");
                var superCallMatch = superCallPattern.Match(body);
                if (superCallMatch.Success)
                {
                    var insertIndex = superCallMatch.Index + superCallMatch.Length;
                    body = body.Insert(insertIndex, Environment.NewLine + helperInvokeLine);
                }
                else
                {
                    body = body.TrimEnd() + Environment.NewLine + "    " + superInvokeLine + Environment.NewLine + helperInvokeLine + Environment.NewLine;
                }
            }

            return content[..match.Groups[2].Index] + body + content[(match.Groups[2].Index + match.Groups[2].Length)..];
        }

        var newMethod =
            $".method protected {methodName}{methodSignature}{Environment.NewLine}" +
            "    .locals 0" + Environment.NewLine +
            $"    {superInvokeLine}{Environment.NewLine}" +
            $"{helperInvokeLine}{Environment.NewLine}" +
            "    return-void" + Environment.NewLine +
            ".end method" + Environment.NewLine + Environment.NewLine;
        return InsertLinesBeforeEndClass(content, [newMethod]);
    }
}
