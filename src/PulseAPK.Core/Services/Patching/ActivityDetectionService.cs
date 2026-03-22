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
        var packageName = (string?)document.Root?.Attribute("package");

        var activities = document.Descendants()
            .Where(element => element.Name.LocalName == "activity")
            .ToList();

        var aliases = document.Descendants()
            .Where(element => element.Name.LocalName == "activity-alias")
            .ToList();

        var launchableComponents = activities
            .Concat(aliases)
            .ToList();

        var withLauncher = launchableComponents.FirstOrDefault(activity =>
            activity.Descendants().Any(node =>
                node.Name.LocalName == "action" &&
                string.Equals((string?)node.Attribute(androidNs + "name"), "android.intent.action.MAIN", StringComparison.Ordinal))
            && activity.Descendants().Any(node =>
                node.Name.LocalName == "category" &&
                string.Equals((string?)node.Attribute(androidNs + "name"), "android.intent.category.LAUNCHER", StringComparison.Ordinal)));

        if (withLauncher is not null)
        {
            if (withLauncher.Name.LocalName == "activity-alias")
            {
                var targetActivity = (string?)withLauncher.Attribute(androidNs + "targetActivity");
                if (TryResolveActivityName(targetActivity, packageName, out var resolvedTarget))
                {
                    var targetExists = activities.Any(activity =>
                        string.Equals(
                            ResolveActivityName((string?)activity.Attribute(androidNs + "name"), packageName),
                            resolvedTarget,
                            StringComparison.Ordinal));

                    if (targetExists)
                    {
                        return Task.FromResult<(string?, string?, string?)>((ResolveActivityName(resolvedTarget, packageName), null, null));
                    }
                }

                var fallbackActivityName = (string?)activities.FirstOrDefault()?.Attribute(androidNs + "name");
                if (TryResolveActivityName(fallbackActivityName, packageName, out var resolvedFallback))
                {
                    return Task.FromResult<(string?, string?, string?)>((ResolveActivityName(resolvedFallback, packageName), "Launcher activity alias has missing or invalid targetActivity. Falling back to first concrete activity in manifest.", null));
                }

                return Task.FromResult<(string?, string?, string?)>((null, null, "Launcher activity alias has missing or invalid targetActivity, and no concrete activity entries were found in AndroidManifest.xml."));
            }

            var launcherActivityName = (string?)withLauncher.Attribute(androidNs + "name");
            if (TryResolveActivityName(launcherActivityName, packageName, out var resolvedLauncher))
            {
                return Task.FromResult<(string?, string?, string?)>((ResolveActivityName(resolvedLauncher, packageName), null, null));
            }

            return Task.FromResult<(string?, string?, string?)>((null, null, "Launcher activity is missing a valid android:name value in AndroidManifest.xml."));
        }

        var firstActivityName = (string?)activities.FirstOrDefault()?.Attribute(androidNs + "name");
        if (TryResolveActivityName(firstActivityName, packageName, out var resolvedFirstActivity))
        {
            return Task.FromResult<(string?, string?, string?)>((ResolveActivityName(resolvedFirstActivity, packageName), "No MAIN/LAUNCHER activity found. Falling back to first activity in manifest.", null));
        }

        return Task.FromResult<(string?, string?, string?)>((null, null, "No activity entries found in AndroidManifest.xml."));
    }

    private static string? ResolveActivityName(string? activityName, string? packageName)
    {
        if (string.IsNullOrWhiteSpace(activityName))
        {
            return null;
        }

        if (activityName.StartsWith(".", StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(packageName) ? null : packageName + activityName;
        }

        if (activityName.Contains(".", StringComparison.Ordinal))
        {
            return activityName;
        }

        return string.IsNullOrWhiteSpace(packageName) ? null : $"{packageName}.{activityName}";
    }

    private static bool TryResolveActivityName(string? activityName, string? packageName, out string resolvedActivityName)
    {
        resolvedActivityName = ResolveActivityName(activityName, packageName) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(resolvedActivityName);
    }
}
