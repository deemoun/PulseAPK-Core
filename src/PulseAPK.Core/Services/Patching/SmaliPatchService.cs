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

        var methodBody = useDelayedLoad
            ? new[]
            {
                ".method private static loadFridaGadget()V",
                "    .locals 1",
                "",
                "    const-string v0, \"frida-gadget\"",
                "    invoke-static {v0}, Ljava/lang/System;->loadLibrary(Ljava/lang/String;)V",
                "    return-void",
                ".end method",
                string.Empty
            }
            : new[]
            {
                ".method private static loadFridaGadget()V",
                "    .locals 1",
                "",
                "    const-string v0, \"frida-gadget\"",
                "    invoke-static {v0}, Ljava/lang/System;->loadLibrary(Ljava/lang/String;)V",
                "    return-void",
                ".end method",
                string.Empty
            };

        var patched = originalContent;
        patched = InsertHelperMethod(patched, methodBody);
        patched = InjectCallIntoOnCreate(patched, classDescriptor);

        if (ReferenceEquals(patched, originalContent) || patched == originalContent)
        {
            return Task.FromResult<(bool Success, string? Error)>((false, "Unable to find an injection point in activity smali file."));
        }

        File.WriteAllText(smaliFile, patched);
        return Task.FromResult<(bool Success, string? Error)>((true, null));
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

    private static string InsertHelperMethod(string content, IReadOnlyList<string> lines)
    {
        var endIndex = content.LastIndexOf(".end class", StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return content;
        }

        var method = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        return content.Insert(endIndex, method);
    }

    private static string InjectCallIntoOnCreate(string content, string classDescriptor)
    {
        var onCreatePattern = new Regex(@"(?ms)(\.method[^\n]* onCreate\(Landroid/os/Bundle;\)V\s+)(.*?)(\.end method)");
        var match = onCreatePattern.Match(content);

        if (match.Success)
        {
            var body = match.Groups[2].Value;
            if (body.Contains("loadFridaGadget", StringComparison.Ordinal))
            {
                return content;
            }

            var call = "    invoke-static {}, " + classDescriptor + "->loadFridaGadget()V";
            var superCallPattern = new Regex(@"(?m)^\s*invoke-super \{[^\n]+\}, [^\n]+->onCreate\(Landroid/os/Bundle;\)V\s*$");
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

        var methodToAdd = $".method protected onCreate(Landroid/os/Bundle;)V{Environment.NewLine}" +
                          "    .locals 0" + Environment.NewLine +
                          "    invoke-super {p0, p1}, Landroid/app/Activity;->onCreate(Landroid/os/Bundle;)V" + Environment.NewLine +
                          $"    invoke-static {{}}, {classDescriptor}->loadFridaGadget()V{Environment.NewLine}" +
                          "    return-void" + Environment.NewLine +
                          ".end method" + Environment.NewLine + Environment.NewLine;

        var endClassIndex = content.LastIndexOf(".end class", StringComparison.Ordinal);
        if (endClassIndex < 0)
        {
            return content;
        }

        return content.Insert(endClassIndex, methodToAdd);
    }
}
