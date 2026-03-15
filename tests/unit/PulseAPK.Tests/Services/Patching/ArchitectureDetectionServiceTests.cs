using System.IO.Compression;
using PulseAPK.Core.Models;
using PulseAPK.Core.Services.Patching;

namespace PulseAPK.Tests.Services.Patching;

public class ArchitectureDetectionServiceTests
{
    [Fact]
    public async Task ResolveAsync_UsesExplicitArchitecture_WhenProvided()
    {
        var service = new ArchitectureDetectionService();
        var request = new PatchRequest { SelectedArchitecture = "x86_64" };

        var result = await service.ResolveAsync(request);

        Assert.Equal("x86_64", result.Architecture);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ResolveAsync_ScansApkLibraries_WhenNoExplicitArchitecture()
    {
        var apkPath = CreateApkWithEntry("lib/armeabi-v7a/libfoo.so");
        var service = new ArchitectureDetectionService();

        var result = await service.ResolveAsync(new PatchRequest { InputApkPath = apkPath });

        Assert.Equal("armeabi-v7a", result.Architecture);
        Assert.Null(result.Error);
    }

    private static string CreateApkWithEntry(string entry)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.apk");
        using var archive = ZipFile.Open(tempFile, ZipArchiveMode.Create);
        archive.CreateEntry(entry);
        return tempFile;
    }
}
