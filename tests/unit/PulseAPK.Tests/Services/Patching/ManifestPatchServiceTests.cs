using PulseAPK.Core.Models;
using PulseAPK.Core.Services.Patching;

namespace PulseAPK.Tests.Services.Patching;

public class ManifestPatchServiceTests
{
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
    }
}
