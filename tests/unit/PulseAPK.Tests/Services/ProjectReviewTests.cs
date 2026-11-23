using System.Text.Json;
using PulseAPK.Core.Services;

namespace PulseAPK.Tests.Services;

public class ProjectReviewTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pulse-review-").FullName;
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void InvalidOnlyCategoriesDoNotAppearActiveOrMatchEveryLine()
    {
        File.WriteAllText(Path.Combine(_root, "test.smali"), "ordinary sample");
        var rules = new AnalysisRuleSet { Rules =
        [
            new() { Category = "blank", RegexPatterns = [""] },
            new() { Category = "broken", RegexPatterns = ["["] },
            new() { Category = "working", RegexPatterns = ["sample"] }
        ] };
        var result = new SmaliAnalyserService().Scan(_root, rules, null, default);
        Assert.Equal("working", Assert.Single(result.ActiveCategories));
        Assert.Equal("working", Assert.Single(result.Findings).Category);
        Assert.Equal(2, result.RuleWarnings.Count);
        Assert.Contains("partial", result.Status);
    }

    [Fact]
    public void SearchBatchesAreBoundedAndCountAllMatchingLines()
    {
        var path = Path.Combine(_root, "Config");
        File.WriteAllLines(path, Enumerable.Repeat("needle", 2017));
        var options = new ProjectSearch("needle", false, SmaliAnalyserService.Extensionless, false);
        var batches = new List<Finding[]>();
        var analyser = new SmaliAnalyserService();
        var result = analyser.Scan(_root, new(), null, default, search: options, matches: batches.Add);
        Assert.Equal(2017, result.MatchingLines);
        Assert.Equal(SmaliAnalyserService.SearchLimit, result.Findings.Count);
        Assert.Single(batches[0]);
        Assert.Equal(result.Findings, batches.SelectMany(batch => batch).ToArray());
        using var stop = new CancellationTokenSource();
        var partial = analyser.Scan(_root, new(), null, stop.Token, search: options, matches: _ => stop.Cancel());
        Assert.True(partial.Cancelled);
        Assert.Equal(1, partial.MatchingLines);
        File.Move(path, path + ".toml");
        var custom = analyser.Scan(_root, new(), null, default, search: options with { FileType = ".toml" });
        Assert.Equal(2017, custom.MatchingLines);
    }

    [Fact]
    public async Task InterruptedReportPublicationLeavesOnlyPreviousReport()
    {
        var path = Path.Combine(_root, "report.json");
        File.WriteAllText(path, "previous");
        await Assert.ThrowsAsync<IOException>(() => PulseAPK.Core.Utils.AtomicOutput.WriteAsync(path, async stream =>
        {
            await stream.WriteAsync(new byte[] { 123 });
            throw new IOException("Interrupted report");
        }));
        Assert.Equal(path, Assert.Single(Directory.GetFiles(_root)));
        Assert.Equal("previous", File.ReadAllText(path));
    }

    [Fact]
    public void LiteralSearchHonorsCaseFileTypeAndLibrarySettings()
    {
        Directory.CreateDirectory(Path.Combine(_root, "library"));
        File.WriteAllText(Path.Combine(_root, "α.xml"), "one\nA[B]");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "a[b]");
        File.WriteAllText(Path.Combine(_root, "library", "α.xml"), "a[b]");
        File.WriteAllText(Path.Combine(_root, "binary.dex"), "a[b]");
        var rules = new AnalysisRuleSet { LibraryPaths = ["library"] };
        var service = new SmaliAnalyserService();
        var options = new ProjectSearch("a[b]", false, ".xml", false);
        AnalysisResult Find(ProjectSearch query) => service.Scan(_root, rules, null, default, search: query);
        var match = Assert.Single(Find(options).Findings);
        Assert.Equal("α.xml", match.RelativePath);
        Assert.Equal(2, match.LineNumber);
        Assert.Empty(Find(options with { MatchCase = true }).Findings);
        Assert.Equal(3, Find(options with { IncludeLibraries = true, FileType = SmaliAnalyserService.SearchTypes[0] }).Findings.Count);
    }

    [Fact]
    public async Task StructuredExportKeepsRuleMetadataAndExcludesRawSecretsAcrossCategories()
    {
        const string secret = "sentinel-private-value";
        File.WriteAllText(Path.Combine(_root, "α.smali"), secret);
        var rules = new AnalysisRuleSet { Rules =
        [
            new() { Category = "hardcoded_creds", Description = "Credential literal", Severity = "high", RegexPatterns = ["sentinel"] },
            new() { Category = "custom", Severity = "low", RegexPatterns = ["[", "private"] }
        ] };
        var result = new SmaliAnalyserService().Scan(_root, rules, null, default);
        Assert.Single(result.RuleWarnings);
        Assert.Contains("partial", result.Status);
        var reports = new ReportService(() => _root, () => DateTime.Now);
        var path = await reports.SaveAnalysisAsync(result, "example");
        Assert.Equal(".json", Path.GetExtension(path));
        var json = File.ReadAllText(path);
        Assert.DoesNotContain(secret, json);
        Assert.DoesNotContain(_root, json);
        using var doc = JsonDocument.Parse(json);
        var findings = doc.RootElement.GetProperty("findings");
        Assert.Equal("custom", findings[0].GetProperty("category").GetString());
        Assert.Equal("α.smali", findings[1].GetProperty("path").GetString());
        Assert.Equal("high", findings[1].GetProperty("severity").GetString());
        Assert.Equal("Credential literal", findings[1].GetProperty("description").GetString());
        Assert.Equal("[masked]", findings[1].GetProperty("evidence").GetString());
    }
}
