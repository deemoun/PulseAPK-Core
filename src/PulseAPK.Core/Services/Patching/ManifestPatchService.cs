using System.Xml;
using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Models;

namespace PulseAPK.Core.Services.Patching;

public sealed class ManifestPatchService : IManifestPatchService
{
    private const string AndroidNs = "http://schemas.android.com/apk/res/android";

    public Task<(bool Success, string? Error)> PatchAsync(string manifestPath, PatchRequest request, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(manifestPath))
        {
            return Task.FromResult<(bool Success, string? Error)>((false, "Manifest file was not found."));
        }

        var document = new XmlDocument { PreserveWhitespace = true };
        document.Load(manifestPath);

        var manager = new XmlNamespaceManager(document.NameTable);
        manager.AddNamespace("android", AndroidNs);

        var manifestNode = document.SelectSingleNode("/manifest");
        if (manifestNode is null)
        {
            return Task.FromResult<(bool Success, string? Error)>((false, "Invalid AndroidManifest.xml structure."));
        }

        if (request.EnsureInternetPermission)
        {
            EnsureInternetPermission(document, manifestNode, manager);
        }

        if (request.EnsureExtractNativeLibs)
        {
            EnsureExtractNativeLibs(document, manager);
        }

        document.Save(manifestPath);
        return Task.FromResult<(bool Success, string? Error)>((true, null));
    }

    private static void EnsureInternetPermission(XmlDocument document, XmlNode manifestNode, XmlNamespaceManager manager)
    {
        var exists = document.SelectSingleNode("/manifest/uses-permission[@android:name='android.permission.INTERNET']", manager) is not null;
        if (exists)
        {
            return;
        }

        var permission = document.CreateElement("uses-permission");
        var attr = document.CreateAttribute("android", "name", AndroidNs);
        attr.Value = "android.permission.INTERNET";
        permission.Attributes?.Append(attr);

        manifestNode.PrependChild(permission);
    }

    private static void EnsureExtractNativeLibs(XmlDocument document, XmlNamespaceManager manager)
    {
        var applicationNode = document.SelectSingleNode("/manifest/application", manager);
        if (applicationNode is null)
        {
            return;
        }

        var existing = applicationNode.Attributes?["extractNativeLibs", AndroidNs];
        if (existing is not null)
        {
            existing.Value = "true";
            return;
        }

        var attr = document.CreateAttribute("android", "extractNativeLibs", AndroidNs);
        attr.Value = "true";
        applicationNode.Attributes?.Append(attr);
    }
}
