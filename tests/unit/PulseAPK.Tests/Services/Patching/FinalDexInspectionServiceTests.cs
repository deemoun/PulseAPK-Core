using System.IO.Compression;
using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Services.Patching;

namespace PulseAPK.Tests.Services.Patching;

public sealed class FinalDexInspectionServiceTests
{
    [Fact]
    public async Task ContainsMethodReferenceAsync_ReturnsTrue_WhenMethodExistsInLaterDexEntry()
    {
        var apkPath = CreateApkWithDexPayloads(new Dictionary<string, byte[]>
        {
            ["classes.dex"] = "miss"u8.ToArray(),
            ["classes2.dex"] = "hit"u8.ToArray()
        });

        var lookup = new FakeDexMethodLookupService(new Dictionary<string, LookupOutcome>
        {
            ["miss"] = LookupOutcome.NotFound(),
            ["hit"] = LookupOutcome.MatchFound()
        });
        var service = new FinalDexInspectionService(lookup);

        var (found, diagnostics) = await service.ContainsMethodReferenceAsync(
            apkPath,
            "Lzed/rainxch/githubstore/MainActivity;->loadFridaGadget()V");

        Assert.True(found);
        Assert.Contains("classes2.dex", diagnostics);
    }

    [Fact]
    public async Task ContainsMethodReferenceAsync_ReturnsFalse_WithWarningDiagnostics_WhenOneDexIsMalformedAndOthersAreValid()
    {
        var apkPath = CreateApkWithDexPayloads(new Dictionary<string, byte[]>
        {
            ["classes.dex"] = "malformed"u8.ToArray(),
            ["classes2.dex"] = "miss"u8.ToArray()
        });

        var lookup = new FakeDexMethodLookupService(new Dictionary<string, LookupOutcome>
        {
            ["malformed"] = LookupOutcome.ParseFailure(new InvalidDataException("Invalid dex header.")),
            ["miss"] = LookupOutcome.NotFound()
        });
        var service = new FinalDexInspectionService(lookup);

        var (found, diagnostics) = await service.ContainsMethodReferenceAsync(
            apkPath,
            "Lzed/rainxch/githubstore/MainActivity;->loadFridaGadget()V");

        Assert.False(found);
        Assert.Contains("Method tuple not found", diagnostics);
        Assert.Contains("warning 'classes.dex'", diagnostics);
        Assert.Contains("Invalid dex header", diagnostics);
    }

    [Fact]
    public async Task ContainsMethodReferenceAsync_ReturnsFalse_WhenMethodTupleDoesNotExist()
    {
        var apkPath = CreateApkWithDexPayloads(new Dictionary<string, byte[]>
        {
            ["classes.dex"] = "miss-one"u8.ToArray(),
            ["classes2.dex"] = "miss-two"u8.ToArray()
        });

        var lookup = new FakeDexMethodLookupService(new Dictionary<string, LookupOutcome>
        {
            ["miss-one"] = LookupOutcome.NotFound(),
            ["miss-two"] = LookupOutcome.NotFound()
        });
        var service = new FinalDexInspectionService(lookup);

        var (found, diagnostics) = await service.ContainsMethodReferenceAsync(
            apkPath,
            "Lzed/rainxch/githubstore/MainActivity;->loadFridaGadget()V");

        Assert.False(found);
        Assert.Contains("Method tuple not found in any of the 2 dex entries", diagnostics);
        Assert.DoesNotContain("warning", diagnostics, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContainsMethodReferenceAsync_ReturnsFalse_WhenSignatureIsInvalid()
    {
        var apkPath = CreateApkWithDexPayloads(new Dictionary<string, byte[]>
        {
            ["classes.dex"] = "hit"u8.ToArray()
        });

        var service = new FinalDexInspectionService(new FakeDexMethodLookupService(new Dictionary<string, LookupOutcome>()));

        var (found, diagnostics) = await service.ContainsMethodReferenceAsync(
            apkPath,
            "Lzed/rainxch/githubstore/MainActivity;loadFridaGadget()V");

        Assert.False(found);
        Assert.Contains("is invalid", diagnostics);
    }

    private static string CreateApkWithDexPayloads(IReadOnlyDictionary<string, byte[]> dexPayloads)
    {
        var apkPath = Path.Combine(Path.GetTempPath(), $"final-dex-inspection-{Guid.NewGuid():N}.apk");
        using var archive = ZipFile.Open(apkPath, ZipArchiveMode.Create);
        foreach (var dexPayload in dexPayloads)
        {
            var dex = archive.CreateEntry(dexPayload.Key, CompressionLevel.NoCompression);
            using var stream = dex.Open();
            stream.Write(dexPayload.Value, 0, dexPayload.Value.Length);
        }

        return apkPath;
    }

    private sealed class FakeDexMethodLookupService(IReadOnlyDictionary<string, LookupOutcome> outcomes) : IDexMethodLookupService
    {
        public bool ContainsMethodReference(byte[] dexData, string classDescriptor, string methodName, string signature)
        {
            var key = System.Text.Encoding.UTF8.GetString(dexData);
            if (!outcomes.TryGetValue(key, out var outcome))
            {
                return false;
            }

            if (outcome.Exception is not null)
            {
                throw outcome.Exception;
            }

            return outcome.Found;
        }
    }

    private sealed record LookupOutcome(bool Found, Exception? Exception)
    {
        public static LookupOutcome MatchFound() => new(true, null);

        public static LookupOutcome NotFound() => new(false, null);

        public static LookupOutcome ParseFailure(Exception exception) => new(false, exception);
    }
}
