using System.Xml.Linq;
using PulseAPK.Core.Abstractions.Patching;

namespace PulseAPK.Core.Services.Patching;

public sealed class ActivityDetectionService : IActivityDetectionService
{
    public Task<(string? ActivityName, string? Warning, string? Error)> DetectMainActivityAsync(string decompiledDirectory, CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(decompiledDirectory, "AndroidManifest.xml");
        if (!File.Exists(manifestPath))
        {
            return Task.FromResult<(string?, string?, string?)>((null, null, "AndroidManifest.xml was not found in decompiled output."));
        }

        var document = XDocument.Load(manifestPath);
        var androidNs = XNamespace.Get("http://schemas.android.com/apk/res/android");

        var activities = document.Descendants()
            .Where(element => element.Name.LocalName is "activity" or "activity-alias")
            .ToList();

        var withLauncher = activities.FirstOrDefault(activity =>
            activity.Descendants().Any(node =>
                node.Name.LocalName == "action" &&
                string.Equals((string?)node.Attribute(androidNs + "name"), "android.intent.action.MAIN", StringComparison.Ordinal))
            && activity.Descendants().Any(node =>
                node.Name.LocalName == "category" &&
                string.Equals((string?)node.Attribute(androidNs + "name"), "android.intent.category.LAUNCHER", StringComparison.Ordinal))));

        if (withLauncher is not null)
        {
            return Task.FromResult<(string?, string?, string?)>(((string?)withLauncher.Attribute(androidNs + "name"), null, null));
        }

        var firstActivityName = (string?)activities.FirstOrDefault()?.Attribute(androidNs + "name");
        if (!string.IsNullOrWhiteSpace(firstActivityName))
        {
            return Task.FromResult<(string?, string?, string?)>((firstActivityName, "No MAIN/LAUNCHER activity found. Falling back to first activity in manifest.", null));
        }

        return Task.FromResult<(string?, string?, string?)>((null, null, "No activity entries found in AndroidManifest.xml."));
    }
}
