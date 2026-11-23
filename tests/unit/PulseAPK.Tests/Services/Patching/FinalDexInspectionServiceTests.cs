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

    [Fact]
    public async Task ContainsStringMarkerAsync_ReturnsTrue_WhenMarkerExistsInLaterDexEntry()
    {
        var apkPath = CreateApkWithDexPayloads(new Dictionary<string, byte[]>
        {
            ["classes.dex"] = ReadFixture("Meadow.dex"),
            ["classes2.dex"] = ReadFixture("Ordinary.dex")
        });

        var service = new FinalDexInspectionService(new FakeDexMethodLookupService(new Dictionary<string, LookupOutcome>()));

        var (found, diagnostics) = await service.ContainsStringMarkerAsync(apkPath, "orange orchard");

        Assert.True(found);
        Assert.Contains("classes2.dex", diagnostics);
        Assert.Contains("Marker literal", diagnostics);
    }

    [Fact]
    public async Task ContainsStringMarkerAsync_ReturnsFalse_WhenMarkerMissingAfterSuccessfulParsing()
    {
        var apkPath = CreateApkWithDexPayloads(new Dictionary<string, byte[]>
        {
            ["classes.dex"] = ReadFixture("Ordinary.dex"),
            ["classes2.dex"] = ReadFixture("Meadow.dex")
        });

        var service = new FinalDexInspectionService(new FakeDexMethodLookupService(new Dictionary<string, LookupOutcome>()));

        var (found, diagnostics) = await service.ContainsStringMarkerAsync(apkPath, "blue orchard");

        Assert.False(found);
        Assert.True(diagnostics.Contains("not found in any of the 2 dex entries", StringComparison.Ordinal), diagnostics);
        Assert.DoesNotContain("warning", diagnostics, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContainsStringMarkerAsync_ReturnsFalse_WithParseFailureDiagnostics_WhenAllDexEntriesFailParsing()
    {
        var apkPath = CreateApkWithDexPayloads(new Dictionary<string, byte[]>
        {
            ["classes.dex"] = "invalid-one"u8.ToArray(),
            ["classes2.dex"] = "invalid-two"u8.ToArray()
        });

        var service = new FinalDexInspectionService(new FakeDexMethodLookupService(new Dictionary<string, LookupOutcome>()));

        var (found, diagnostics) = await service.ContainsStringMarkerAsync(apkPath, "/dev/pulseapk-fake-root-");

        Assert.False(found);
        Assert.Contains("Marker inspection failed for all 2 dex entries", diagnostics);
        Assert.Contains("warning 'classes.dex'", diagnostics);
        Assert.Contains("warning 'classes2.dex'", diagnostics);
    }

    [Fact]
    public async Task ContainsStringMarkerAsync_ReturnsTrue_WhenParserFailsButRawDexContainsMarker()
    {
        var apkPath = CreateApkWithDexPayloads(new Dictionary<string, byte[]>
        {
            ["classes.dex"] = [0x00, 0x01, .. System.Text.Encoding.UTF8.GetBytes("/dev/pulseapk-fake-root-42"), 0x00]
        });

        var service = new FinalDexInspectionService(new FakeDexMethodLookupService(new Dictionary<string, LookupOutcome>()));

        var (found, diagnostics) = await service.ContainsStringMarkerAsync(apkPath, "/dev/pulseapk-fake-root-");

        Assert.True(found);
        Assert.Contains("raw byte scan fallback", diagnostics);
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

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
