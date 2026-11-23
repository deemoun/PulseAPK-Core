using PulseAPK.Core.Services;
using PulseAPK.Core.Services.Patching;
using PulseAPK.Core.ViewModels;

namespace PulseAPK.Tests.Services;

public class AnalysisAndPublicationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pulse-review-").FullName;
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task ConcurrentPublicationsLeaveOriginalAndEachCompletedCopyIntact()
    {
        var path = Path.Combine(_root, "app.apk");
        File.WriteAllText(path, "original");
        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            using var input = new MemoryStream([(byte)i]);
            return await PatchPipelineService.PublishArtifactAsync(input, path, default);
        }));
        var paths = await Task.WhenAll(tasks);
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Equal(10, paths.Distinct().Count());
        for (int i = 0; i < paths.Length; i++)
            Assert.Equal(new byte[] { (byte)i }, File.ReadAllBytes(paths[i]));
    }

    private sealed class InterruptedStream(Action interrupt) : MemoryStream
    {
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken token)
        {
            await destination.WriteAsync(new byte[] { 1 }, token);
            interrupt();
            token.ThrowIfCancellationRequested();
            throw new IOException("Source read failed");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InterruptedPublicationRemovesPartialCopy(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new InterruptedStream(() => { if (cancel) cancellation.Cancel(); });
        var path = Path.Combine(_root, "output.apk");
        var save = PatchPipelineService.PublishArtifactAsync(stream, path, cancellation.Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
        else await Assert.ThrowsAsync<IOException>(() => save);
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public void TimeoutDisablesOnlyOffendingPatternAndPreviewDetectsChanges()
    {
        var rules = new AnalysisRuleSet { LibraryPaths = ["library"], Rules =
            [new AnalysisRule { Category = "sample", RegexPatterns = ["^(a+)+$", "needle"] }] };
        var path = Path.Combine(_root, "Δ.smali");
        var slow = new string('a', 10000) + "!";
        File.WriteAllLines(path, [slow, slow, "needle"]);
        Directory.CreateDirectory(Path.Combine(_root, "library"));
        File.WriteAllText(Path.Combine(_root, "library", "Δ.smali"), "needle");
        var service = new SmaliAnalyserService();
        var logs = new List<string>();
        var result = service.Scan(_root, rules, logs.Add, default);
        Assert.Equal(1, result.Timeouts);
        Assert.Single(logs, line => line.Contains("disabled sample"));
        Assert.Equal(1, result.Excluded);
        Assert.Equal(1, result.Scanned);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("Δ.smali", finding.RelativePath);
        Assert.Contains("▶     3  needle", AnalyserViewModel.ReadPreview(finding, default));
        File.WriteAllText(path, "1\n2\nchanged");
        Assert.Contains("changed", AnalyserViewModel.ReadPreview(finding, default));
        File.Delete(path);
        Assert.Contains("Cannot read", AnalyserViewModel.ReadPreview(finding, default));
    }

    [UnixFact]
    public void ScanCountsUnreadableFilesAndInaccessibleDirectoriesSeparately()
    {
        if (OperatingSystem.IsWindows()) return;
        var folder = Directory.CreateDirectory(Path.Combine(_root, "closed")).FullName;
        var path = Path.Combine(_root, "closed.smali");
        File.WriteAllText(path, "text");
        var folderMode = File.GetUnixFileMode(folder);
        var fileMode = File.GetUnixFileMode(path);
        try
        {
            File.SetUnixFileMode(folder, UnixFileMode.None);
            File.SetUnixFileMode(path, UnixFileMode.None);
            // Privileged Unix users can still read both fixtures.
            try { using var readable = File.OpenRead(path); return; }
            catch (UnauthorizedAccessException) { }
            var result = new SmaliAnalyserService().Scan(_root, new AnalysisRuleSet(), null, default);
            Assert.Equal(1, result.Unreadable);
            Assert.Equal(1, result.Inaccessible);
            Assert.Equal(0, result.Scanned);
        }
        finally
        {
            File.SetUnixFileMode(folder, folderMode);
            File.SetUnixFileMode(path, fileMode);
        }
    }

    [Fact]
    public void CancelledScanKeepsFindingsAndMarksCoveragePartial()
    {
        File.WriteAllLines(Path.Combine(_root, "sample.smali"), ["needle", new string('a', 10000) + "!", "needle"]);
        using var cancellation = new CancellationTokenSource();
        var rules = new AnalysisRuleSet { Rules = [new AnalysisRule { Category = "test", RegexPatterns = ["needle", "^(a+)+$"] }] };
        var result = new SmaliAnalyserService().Scan(_root, rules, _ => cancellation.Cancel(), cancellation.Token);
        Assert.Single(result.Findings);
        Assert.True(result.Cancelled);
        Assert.Contains("partial", result.Status);
    }
}
