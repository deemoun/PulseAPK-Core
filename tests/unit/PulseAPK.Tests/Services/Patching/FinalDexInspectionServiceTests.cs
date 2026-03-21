using System.IO.Compression;
using System.Text;
using PulseAPK.Core.Services.Patching;

namespace PulseAPK.Tests.Services.Patching;

public sealed class FinalDexInspectionServiceTests
{
    [Fact]
    public async Task ContainsMethodReferenceAsync_ReturnsTrue_WhenDexContainsSplitMethodComponents()
    {
        var apkPath = CreateApkWithDexPayload(Encoding.ASCII.GetBytes("Lzed/rainxch/githubstore/MainActivity;\0loadFridaGadget\0()V\0"));
        var service = new FinalDexInspectionService();

        var found = await service.ContainsMethodReferenceAsync(
            apkPath,
            "Lzed/rainxch/githubstore/MainActivity;->loadFridaGadget()V");

        Assert.True(found);
    }

    [Fact]
    public async Task ContainsMethodReferenceAsync_ReturnsFalse_WhenSignatureIsInvalid()
    {
        var apkPath = CreateApkWithDexPayload(Encoding.ASCII.GetBytes("Lzed/rainxch/githubstore/MainActivity;\0loadFridaGadget\0()V\0"));
        var service = new FinalDexInspectionService();

        var found = await service.ContainsMethodReferenceAsync(
            apkPath,
            "Lzed/rainxch/githubstore/MainActivity;loadFridaGadget()V");

        Assert.False(found);
    }

    private static string CreateApkWithDexPayload(byte[] dexPayload)
    {
        var apkPath = Path.Combine(Path.GetTempPath(), $"final-dex-inspection-{Guid.NewGuid():N}.apk");
        using var archive = ZipFile.Open(apkPath, ZipArchiveMode.Create);
        var dex = archive.CreateEntry("classes.dex", CompressionLevel.NoCompression);
        using var stream = dex.Open();
        stream.Write(dexPayload, 0, dexPayload.Length);
        return apkPath;
    }
}
