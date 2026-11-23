using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using PulseAPK.Core.Services;
using PulseAPK.Core.Utils;
using PulseAPK.Core.ViewModels;
using Xunit.Abstractions;

namespace PulseAPK.Tests.Acceptance;

public class InspectionAndPerformanceTests(ITestOutputHelper log) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pulse-review-").FullName;
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task InspectionReportsEachDexAndPreservesOriginalApk()
    {
        var path = Path.Combine(_root, "test.apk");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (entry, fixture) in new[] { ("classes.dex", "Ordinary.dex"), ("classes2.dex", "Meadow.dex") })
                archive.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), entry);
            using var bad = archive.CreateEntry("classes3.dex").Open();
            bad.Write(new byte[120]);
        }
        var original = File.ReadAllBytes(path);
        var result = await ReadOnlyDexSearch.InspectAsync(path, "orange orchard", default);
        Assert.Equal(new DexEntrySearch("classes.dex", 1, null), result[0]);
        Assert.Equal(new DexEntrySearch("classes2.dex", 0, null), result[1]);
        Assert.NotNull(result[2].Error);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task MidBuildEditsArePreserved(bool directory, bool acceptCopy)
    {
        var dest = Path.Combine(_root, "out");
        var stage = Path.Combine(_root, "stage");
        string Content(string path) => directory ? Path.Combine(path, "user-edit.txt") : path;
        if (directory) { Directory.CreateDirectory(dest); Directory.CreateDirectory(stage); }
        File.WriteAllText(Content(dest), "first");
        File.WriteAllText(Content(stage), "built");
        var snapshot = await DestinationSnapshot.CaptureAsync(dest, default);
        File.WriteAllText(Content(dest), "later");
        var publish = snapshot.PublishAsync(stage, dest, directory, _ => Task.FromResult(acceptCopy), default);
        if (acceptCopy)
        {
            var path = await publish;
            Assert.Equal("built", File.ReadAllText(Content(path)));
        }
        else await Assert.ThrowsAsync<IOException>(() => publish);
        Assert.Equal("later", File.ReadAllText(Content(dest)));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void MeasureLargeProjectSearch()
    {
        const int files = 256, lines = 2048;
        var text = string.Join('\n', Enumerable.Range(0, lines).Select(i => $"const-string v0, \"ordinary sample {i}\""));
        for (int i = 0; i < files; i++) File.WriteAllText(Path.Combine(_root, $"file{i}.smali"), text);
        var service = new SmaliAnalyserService();
        var options = new ProjectSearch("ordinary", false, ".smali", false);
        var timer = Stopwatch.StartNew();
        double? first = null;
        using var process = Process.GetCurrentProcess();
        var baseline = process.WorkingSet64;
        long peak = baseline;
        using var stopSampler = new ManualResetEventSlim();
        var sampler = new Thread(() =>
        {
            using var observed = Process.GetCurrentProcess();
            do { observed.Refresh(); peak = Math.Max(peak, observed.WorkingSet64); } while (!stopSampler.Wait(5));
        });
        sampler.Start();
        AnalysisResult result;
        try { result = service.Scan(_root, new(), null, default, search: options, matches: _ => first ??= timer.Elapsed.TotalMilliseconds); }
        finally { stopSampler.Set(); sampler.Join(); }
        Assert.Equal(files * lines, result.MatchingLines);
        Assert.Equal(1000, result.Findings.Count);
        process.Refresh();
        log.WriteLine($"524288 lines: first batch {first:F1} ms, complete {timer.Elapsed.TotalMilliseconds:F1} ms; sampled process peak working set {peak / 1048576} MiB, starting working set {baseline / 1048576} MiB.");
        var largeFile = Path.Combine(_root, "long.smali");
        using (var writer = File.CreateText(largeFile))
            for (int i = 0; i < 250000; i++) writer.WriteLine("ordinary text");
        timer.Restart();
        var preview = AnalyserViewModel.ReadPreview(new Finding { FilePath = largeFile, LineNumber = 250000, OriginalLine = "ordinary text" }, default);
        Assert.Contains("250000", preview);
        log.WriteLine($"Preview at line 250000: {timer.Elapsed.TotalMilliseconds:F1} ms.");
        using var cancel = new CancellationTokenSource();
        var partial = service.Scan(_root, new(), null, cancel.Token, search: options, matches: _ => { timer.Restart(); cancel.Cancel(); });
        Assert.True(partial.Cancelled);
        log.WriteLine($"Cancel to return: {timer.Elapsed.TotalMilliseconds:F1} ms.");
    }
}
