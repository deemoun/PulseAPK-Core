using System.Text.RegularExpressions;
using PulseAPK.Core.Abstractions.Patching;

namespace PulseAPK.Core.Services.Patching;

public sealed class SmaliPatchService : ISmaliPatchService
{
    public Task<(bool Success, string? Error)> PatchAsync(string decompiledDirectory, string activityName, bool useDelayedLoad, CancellationToken cancellationToken = default)
    {
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

        var lifecycleMethodName = useDelayedLoad ? "onResume" : "onCreate";
        var lifecycleSignature = useDelayedLoad ? "()V" : "(Landroid/os/Bundle;)V";
        var patched = originalContent;

        if (useDelayedLoad)
        {
            patched = EnsureDelayedLoadHelperMembers(patched, classDescriptor);
        }
        else
        {
            patched = EnsureImmediateLoadHelperMethod(patched);
        }

        patched = InjectCallIntoLifecycleMethod(patched, classDescriptor, lifecycleMethodName, lifecycleSignature, superClassDescriptor);
        patched = EnsureHelpersForReferencedCalls(patched, classDescriptor);

        if (ReferenceEquals(patched, originalContent) || patched == originalContent)
        {
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
            "    .locals 1",
            string.Empty,
            "    const-string v0, \"frida-gadget\"",
            "    invoke-static {v0}, Ljava/lang/System;->loadLibrary(Ljava/lang/String;)V",
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
            "    .locals 1",
            string.Empty,
            "    sget-boolean v0, Lcom/example/PLACEHOLDER;->sFridaLoaded:Z",
            "    if-nez v0, :loaded",
            string.Empty,
            "    const-string v0, \"frida-gadget\"",
            "    invoke-static {v0}, Ljava/lang/System;->loadLibrary(Ljava/lang/String;)V",
            "    const/4 v0, 0x1",
            "    sput-boolean v0, Lcom/example/PLACEHOLDER;->sFridaLoaded:Z",
            string.Empty,
            "    :loaded",
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

    private static string EnsureHelpersForReferencedCalls(string content, string classDescriptor)
    {
        if (content.Contains("->loadFridaGadget()V", StringComparison.Ordinal) &&
            !HasStaticHelperMethod(content, "loadFridaGadget"))
        {
            content = EnsureImmediateLoadHelperMethod(content);
        }

        if (content.Contains("->loadFridaGadgetIfNeeded()V", StringComparison.Ordinal) &&
            !HasStaticHelperMethod(content, "loadFridaGadgetIfNeeded"))
        {
            content = EnsureDelayedLoadHelperMembers(content, classDescriptor);
        }

        return content;
    }

    private static string EnsureHelperMethodIsStatic(string content, string methodName)
    {
        var methodPattern = new Regex($@"(?m)^(?<indent>[ \t]*)\.method\s+(?<modifiers>[^\n]*?)\b{Regex.Escape(methodName)}\s*\(\)V\s*$");
        var match = methodPattern.Match(content);
        if (!match.Success)
        {
            return content;
        }

        var modifiers = match.Groups["modifiers"].Value;
        if (Regex.IsMatch(modifiers, @"(^|\s)static(\s|$)"))
        {
            return content;
        }

        var normalizedModifiers = string.Join(" ", modifiers.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var indent = match.Groups["indent"].Value;
        var updatedSignature = $"{indent}.method {normalizedModifiers} static {methodName}()V";
        return content[..match.Index] + updatedSignature + content[(match.Index + match.Length)..];
    }

    private static bool HasStaticHelperMethod(string content, string methodName)
    {
        var staticMethodPattern = new Regex($@"(?m)^[ \t]*\.method\s+[^\n]*\bstatic\b[^\n]*\b{Regex.Escape(methodName)}\s*\(\)V\s*$");
        return staticMethodPattern.IsMatch(content);
    }

    private static string InsertLinesBeforeEndClass(string content, IReadOnlyList<string> lines)
    {
        var endClassMatch = FindLastEndClassDirective(content);
        if (endClassMatch is null)
        {
            return content;
        }

        var method = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        return content.Insert(endClassMatch.Index, method);
    }

    private static string InjectCallIntoLifecycleMethod(string content, string classDescriptor, string methodName, string methodSignature, string superClassDescriptor)
    {
        var helperMethodName = methodName == "onResume" ? "loadFridaGadgetIfNeeded" : "loadFridaGadget";
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
            var superCallPattern = new Regex($@"(?m)^\s*invoke-super \{{[^\n]+\}}, [^\n]+->{methodName}{Regex.Escape(methodSignature)}\s*$");
            if (superCallPattern.IsMatch(body))
            {
                body = superCallPattern.Replace(body, m => m.Value + Environment.NewLine + call, 1);
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
              $"    invoke-static {{}}, {classDescriptor}->loadFridaGadgetIfNeeded()V{Environment.NewLine}" +
              "    return-void" + Environment.NewLine +
              ".end method" + Environment.NewLine + Environment.NewLine
            : $".method protected onCreate(Landroid/os/Bundle;)V{Environment.NewLine}" +
              "    .locals 0" + Environment.NewLine +
              $"    invoke-super {{p0, p1}}, {superClassDescriptor}->onCreate(Landroid/os/Bundle;)V{Environment.NewLine}" +
              $"    invoke-static {{}}, {classDescriptor}->loadFridaGadget()V{Environment.NewLine}" +
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
        var matches = Regex.Matches(content, @"(?m)^[ \t]*\.end class\b");
        if (matches.Count == 0)
        {
            return null;
        }

        return matches[^1];
    }
}
