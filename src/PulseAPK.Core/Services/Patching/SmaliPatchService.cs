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
        if (originalContent.Contains("frida-gadget", StringComparison.Ordinal) ||
            originalContent.Contains("loadFridaGadget", StringComparison.Ordinal))
        {
            return Task.FromResult<(bool Success, string? Error)>((true, null));
        }

        var classDescriptor = ExtractClassDescriptor(originalContent);
        if (string.IsNullOrWhiteSpace(classDescriptor))
        {
            return Task.FromResult<(bool Success, string? Error)>((false, "Unable to determine class descriptor from smali file."));
        }

        var helperMethods = useDelayedLoad ? BuildDelayedLoadHelperMethods() : BuildImmediateLoadHelperMethods();
        var lifecycleMethodName = useDelayedLoad ? "onResume" : "onCreate";
        var lifecycleSignature = useDelayedLoad ? "()V" : "(Landroid/os/Bundle;)V";
        var superClassDescriptor = useDelayedLoad ? "Landroid/app/Activity;" : "Landroid/app/Activity;";

        var patched = originalContent;
        patched = InsertHelperMethods(patched, helperMethods);
        patched = InjectCallIntoLifecycleMethod(patched, classDescriptor, lifecycleMethodName, lifecycleSignature, superClassDescriptor);

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
        var relativePath = activityName.TrimStart('.').Replace('.', Path.DirectorySeparatorChar) + ".smali";
        foreach (var smaliRoot in Directory.EnumerateDirectories(decompiledDirectory, "smali*", SearchOption.TopDirectoryOnly))
        {
            var direct = Path.Combine(smaliRoot, relativePath);
            if (File.Exists(direct))
            {
                return direct;
            }

            var match = Directory.EnumerateFiles(smaliRoot, Path.GetFileName(relativePath), SearchOption.AllDirectories)
                .FirstOrDefault(path => path.EndsWith(relativePath, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string? ExtractClassDescriptor(string content)
    {
        var match = Regex.Match(content, @"\.class\s+[\w\s-]+\s+(L[^;]+;)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string InsertHelperMethods(string content, IReadOnlyList<string> lines)
    {
        var endIndex = content.LastIndexOf(".end class", StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return content;
        }

        var classDescriptor = ExtractClassDescriptor(content);
        var normalizedLines = lines.Select(line => line.Replace("Lcom/example/PLACEHOLDER;", classDescriptor, StringComparison.Ordinal)).ToArray();
        var method = string.Join(Environment.NewLine, normalizedLines) + Environment.NewLine;
        return content.Insert(endIndex, method);
    }

    private static string InjectCallIntoLifecycleMethod(string content, string classDescriptor, string methodName, string methodSignature, string superClassDescriptor)
    {
        var helperMethodName = methodName == "onResume" ? "loadFridaGadgetIfNeeded" : "loadFridaGadget";
        var methodPattern = new Regex($@"(?ms)(\.method[^\n]* {methodName}\{Regex.Escape(methodSignature)}\s+)(.*?)(\.end method)");
        var match = methodPattern.Match(content);

        if (match.Success)
        {
            var body = match.Groups[2].Value;
            if (body.Contains(helperMethodName, StringComparison.Ordinal))
            {
                return content;
            }

            var call = "    invoke-static {}, " + classDescriptor + $"->{helperMethodName}()V";
            var superCallPattern = new Regex($@"(?m)^\s*invoke-super \{{[^\n]+\}}, [^\n]+->{methodName}\{Regex.Escape(methodSignature)}\s*$");
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

        var endClassIndex = content.LastIndexOf(".end class", StringComparison.Ordinal);
        if (endClassIndex < 0)
        {
            return content;
        }

        return content.Insert(endClassIndex, newMethod);
    }
}
