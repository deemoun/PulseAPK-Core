using PulseAPK.Core.Models;
using PulseAPK.Core.Services.Patching;

namespace PulseAPK.Tests.Services.Patching;

public class ManifestPatchServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(".ExistingApplication")]
    public async Task SourceDecodingDisabledPreservesApplicationName(string? name)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "AndroidManifest.xml");
            System.Xml.Linq.XNamespace android = "http://schemas.android.com/apk/res/android";
            var application = new System.Xml.Linq.XElement("application");
            application.SetAttributeValue(android + "name", name);
            new System.Xml.Linq.XElement("manifest", application).Save(path);
            var result = await new ManifestPatchService().PatchAsync(path, new PatchRequest { DecodeSources = false });
            Assert.True(result.Success, result.Error);
            var saved = System.Xml.Linq.XDocument.Load(path).Root!.Element("application")!;
            Assert.Equal(name, (string?)saved.Attribute(android + "name"));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task PatchAsync_AddsInternetPermission_AndExtractNativeLibs()
    {
        var manifestPath = Path.Combine(Path.GetTempPath(), $"manifest-{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(manifestPath, "<manifest xmlns:android='http://schemas.android.com/apk/res/android'><application /></manifest>");

        var service = new ManifestPatchService();
        var result = await service.PatchAsync(manifestPath, new PatchRequest());

        var content = await File.ReadAllTextAsync(manifestPath);
        Assert.True(result.Success);
        Assert.Contains("android.permission.INTERNET", content, StringComparison.Ordinal);
        Assert.Contains("extractNativeLibs=\"true\"", content, StringComparison.Ordinal);
        Assert.Contains("android:name=\"PulseFridaApplication\"", content, StringComparison.Ordinal);
    }
}
