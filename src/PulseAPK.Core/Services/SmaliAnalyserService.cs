using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PulseAPK.Core.Services
{
    public class SmaliAnalyserService
    {
        public const int SearchLimit = 1000;
        public const string Extensionless = "Extensionless files";
        public static readonly IReadOnlyList<string> SearchTypes = ["All supported text", ".smali", ".xml", ".json", ".txt", ".properties", ".java", ".kt", ".js", ".yaml", ".yml", ".html", ".css", ".csv", ".md", ".cfg", ".ini"];

        public Task<AnalysisResult> AnalyseProjectAsync(string projectPath, Action<string>? logCallback,
            CancellationToken cancellationToken = default, Action<string>? progress = null)
        {
            var root = Path.GetFullPath(projectPath);
            return Task.Run(() =>
            {
                var warnings = new List<string>();
                var rules = AnalysisRulesLoader.LoadRules(warnings.Add);
                foreach (var warning in warnings) logCallback?.Invoke(warning);
                var result = Scan(root, rules, logCallback, cancellationToken, progress);
                result.RuleWarnings.AddRange(warnings);
                return result;
            });
        }

        public Task<AnalysisResult> SearchAsync(string root, ProjectSearch search, CancellationToken token, Action<Finding[]>? matches = null)
        {
            if (string.IsNullOrWhiteSpace(search.Text)) throw new ArgumentException("Enter text to search for.");
            return Task.Run(() =>
            {
                var warnings = new List<string>();
                var rules = AnalysisRulesLoader.LoadRules(warnings.Add);
                var result = Scan(root, rules, null, token, search: search, matches: matches);
                result.RuleWarnings.AddRange(warnings);
                return result;
            });
        }

        internal AnalysisResult Scan(string root, AnalysisRuleSet rules, Action<string>? log, CancellationToken token, Action<string>? progress = null, ProjectSearch? search = null, Action<Finding[]>? matches = null)
        {
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            var result = new AnalysisResult();
            result.LibraryPaths = rules.LibraryPaths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
            result.Rules = rules.Rules.Where(rule => rule != null).ToArray();
            var patterns = search == null ? BuildRegexCache(rules, warning =>
            {
                result.RuleWarnings.Add(warning);
                log?.Invoke(warning);
            }) : new();
            result.ActiveCategories.UnionWith(patterns.Keys);
            var directories = new Stack<DirectoryInfo>();
            directories.Push(new DirectoryInfo(root));
            var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
            var progressTimer = System.Diagnostics.Stopwatch.StartNew();
            int delivered = 0;
            var batchTimer = System.Diagnostics.Stopwatch.StartNew();
            void Deliver(bool flush)
            {
                int pending = result.Findings.Count - delivered;
                if (matches == null || pending == 0) return;
                if (!flush && delivered != 0 && pending < 64 && batchTimer.ElapsedMilliseconds < 150) return;
                matches(result.Findings.GetRange(delivered, pending).ToArray());
                delivered += pending;
                batchTimer.Restart();
            }
            try
            {
                while (directories.TryPop(out var directory))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        foreach (var entry in directory.EnumerateFileSystemInfos("*", options))
                        {
                            token.ThrowIfCancellationRequested();
                            if (entry is DirectoryInfo child)
                            {
                                directories.Push(child);
                                continue;
                            }
                            var extension = search?.FileType ?? ".smali";
                            bool supported = extension == SearchTypes[0]
                                ? SearchTypes.Skip(1).Contains(entry.Extension, StringComparer.OrdinalIgnoreCase)
                                : entry.Extension.Equals(extension == Extensionless ? "" : extension, StringComparison.OrdinalIgnoreCase);
                            if (!supported) continue;
                            var relative = Path.GetRelativePath(root, entry.FullName);
                            if (search?.IncludeLibraries != true && IsLibraryClass(relative, rules.LibraryPaths))
                            {
                                result.Excluded++;
                                continue;
                            }
                            try
                            {
                                AnalyzeFile(entry.FullName, relative, result, patterns, log, token, search, Deliver);
                                result.Scanned++;
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                                result.Unreadable++;
                                log?.Invoke($"Could not read {relative}: {ex.Message}");
                            }
                            if (progressTimer.ElapsedMilliseconds >= 300)
                            {
                                log?.Invoke(result.Coverage);
                                progress?.Invoke(result.Coverage);
                                progressTimer.Restart();
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        result.Inaccessible++;
                        log?.Invoke($"Could not enumerate {directory.FullName}: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                result.Cancelled = true;
            }
            Deliver(true);
            return result;
        }

        private static void AnalyzeFile(string path, string relative, AnalysisResult result,
            Dictionary<string, List<(Regex Pattern, AnalysisRule Rule)>> patterns, Action<string>? log, CancellationToken token, ProjectSearch? search, Action<bool> deliver)
        {
            int number = 0;
            foreach (var line in File.ReadLines(path))
            {
                token.ThrowIfCancellationRequested();
                number++;
                if (search != null)
                {
                    if (!line.Contains(search.Text, search.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)) continue;
                    result.MatchingLines++;
                    if (result.Findings.Count < SearchLimit)
                    {
                        result.Findings.Add(new Finding { FilePath = path, RelativePath = relative, LineNumber = number, OriginalLine = line, Context = line.Trim() });
                        deliver(result.Findings.Count == SearchLimit);
                    }
                    continue;
                }
                foreach (var (category, compiled) in patterns)
                {
                    for (int i = 0; i < compiled.Count; i++)
                    {
                        try
                        {
                            if (!compiled[i].Pattern.IsMatch(line)) continue;
                            var finding = new Finding { FilePath = path, RelativePath = relative, LineNumber = number,
                                Context = line.Trim(), OriginalLine = line, Category = category, Severity = compiled[i].Rule.Severity, Description = compiled[i].Rule.Description };
                            result.Findings.Add(finding);
                            result.CategoryList(category)?.Add(finding);
                            break;
                        }
                        catch (RegexMatchTimeoutException)
                        {
                            log?.Invoke($"Timed out; disabled {category} pattern {compiled[i].Pattern}");
                            compiled.RemoveAt(i--);
                            result.Timeouts++;
                        }
                    }
                }
            }
        }

        private bool IsLibraryClass(string relativePath, List<string> libraryPaths)
        {
            var pathSegments = relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

            foreach (var pattern in libraryPaths)
            {
                var normalizedPattern = pattern.Replace('\\', '/').Trim('/');
                if (string.IsNullOrWhiteSpace(normalizedPattern))
                {
                    continue;
                }

                var patternSegments = normalizedPattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (ContainsSegmentSequence(pathSegments, patternSegments))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ContainsSegmentSequence(IReadOnlyList<string> pathSegments, string[] patternSegments)
        {
            if (patternSegments.Length == 0)
            {
                return false;
            }

            for (var i = 0; i <= pathSegments.Count - patternSegments.Length; i++)
            {
                var match = true;
                for (var j = 0; j < patternSegments.Length; j++)
                {
                    if (!string.Equals(pathSegments[i + j], patternSegments[j], StringComparison.OrdinalIgnoreCase))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return true;
                }
            }

            return false;
        }

        private Dictionary<string, List<(Regex Pattern, AnalysisRule Rule)>> BuildRegexCache(AnalysisRuleSet rules, Action<string>? logCallback)
        {
            var cache = new Dictionary<string, List<(Regex Pattern, AnalysisRule Rule)>>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < rules.Rules.Count; i++)
            {
                var rule = rules.Rules[i];
                if (rule == null || string.IsNullOrWhiteSpace(rule.Category) || rule.RegexPatterns == null || rule.RegexPatterns.Count == 0)
                {
                    logCallback?.Invoke($"Skipping invalid rule at position {i + 1}.");
                    continue;
                }
                var category = rule.Category ?? string.Empty;
                if (!cache.TryGetValue(category, out var compiledPatterns))
                {
                    compiledPatterns = new List<(Regex Pattern, AnalysisRule Rule)>();
                    cache[category] = compiledPatterns;
                }

                foreach (var pattern in rule.RegexPatterns)
                {
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        logCallback?.Invoke($"Skipping empty pattern in category '{category}'.");
                        continue;
                    }
                    try
                    {
                        compiledPatterns.Add((new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(250)), rule));
                    }
                    catch (ArgumentException ex)
                    {
                        logCallback?.Invoke($"Warning: Skipping invalid regex pattern '{pattern}' in category '{rule.Category}': {ex.Message}");
                    }
                }
            }

            foreach (var category in cache.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).ToArray())
                cache.Remove(category);
            return cache;
        }

    }

    public sealed record ProjectSearch(string Text, bool MatchCase, string FileType, bool IncludeLibraries);

    public class AnalysisResult
    {
        public List<Finding> Findings { get; } = new();
        public long MatchingLines { get; set; }
        public int Scanned, Excluded, Unreadable, Inaccessible, Timeouts;
        public bool Cancelled;
        public AnalysisRule[] Rules { get; set; } = [];
        public string[] LibraryPaths { get; set; } = [];
        public List<string> RuleWarnings { get; } = [];
        public string Coverage => $"Scanned {Scanned} files · Excluded {Excluded} · Unreadable {Unreadable} · Inaccessible directories {Inaccessible} · Timed-out rules {Timeouts}";
        public string Status => Cancelled ? "Cancelled · partial results" :
            Unreadable + Inaccessible + Timeouts + RuleWarnings.Count > 0 ? "Completed with partial coverage" : "Analysis complete";
        internal List<Finding>? CategoryList(string category) => category.ToLowerInvariant() switch
        {
            "root_check" => RootChecks, "emulator_check" => EmulatorChecks, "hardcoded_creds" => HardcodedCredentials,
            "sql_query" => SqlQueries, "http_url" => HttpUrls, _ => null
        };
        public HashSet<string> ActiveCategories { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<Finding> RootChecks { get; set; } = new List<Finding>();
        public List<Finding> EmulatorChecks { get; set; } = new List<Finding>();
        public List<Finding> HardcodedCredentials { get; set; } = new List<Finding>();
        public List<Finding> SqlQueries { get; set; } = new List<Finding>();
        public List<Finding> HttpUrls { get; set; } = new List<Finding>();
    }

    public class Finding
    {
        public string Category { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Description { get; set; } = "";
        public string OriginalLine { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Context { get; set; } = string.Empty;
    }
}
