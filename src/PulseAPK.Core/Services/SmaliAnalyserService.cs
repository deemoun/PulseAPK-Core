using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PulseAPK.Core.Services
{
    public class SmaliAnalyserService
    {
        public async Task<AnalysisResult> AnalyseProjectAsync(string projectPath, Action<string> logCallback)
        {
            var result = new AnalysisResult();

            if (!Directory.Exists(projectPath))
            {
                throw new DirectoryNotFoundException($"Project path '{projectPath}' does not exist.");
            }

            // Ensure rules are initialized/loaded (this covers missing files/defaults)
            AnalysisRuleSet rules;
            try 
            {
                // Force a reload/check to ensure we have the latest
                rules = (AnalysisRuleSet)AnalysisRulesLoader.InitializeRules();
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Warning: Failed to load custom rules: {ex.Message}. Using defaults.");
                rules = (AnalysisRuleSet)AnalysisRulesLoader.InitializeRules();
            }

            // Find all .smali files recursively, skipping inaccessible directories (sandbox/container safe)
            var smaliFiles = GetSmaliFiles(projectPath, logCallback);

            if (smaliFiles.Length == 0)
            {
                throw new InvalidOperationException("No Smali files found in the project directory.");
            }

            logCallback?.Invoke($"Found {smaliFiles.Length} Smali files. Starting analysis...");
            if (rules.Rules.Count > 0)
            {
                logCallback?.Invoke($"Loaded {rules.Rules.Count} analysis categories.");
                foreach (var rule in rules.Rules)
                {
                    result.ActiveCategories.Add(rule.Category);
                }
            }
            logCallback?.Invoke("");

            var regexCache = BuildRegexCache(rules, logCallback);
            var categoryOrder = BuildCategoryOrder(rules, regexCache);
            var normalizedProjectRoot = Path.GetFullPath(projectPath).Replace('\\', '/').TrimEnd('/') + "/";

            // Run all detection methods with progress reporting
            await Task.Run(() =>
            {
                int processedFiles = 0;
                int totalFiles = smaliFiles.Length;
                int reportInterval = totalFiles > 100 ? totalFiles / 20 : 10; // Report every 5% or every 10 files

                foreach (var file in smaliFiles)
                {
                    try
                    {
                        // Run all detections on this file
                        AnalyzeFile(file, result, rules, regexCache, categoryOrder, normalizedProjectRoot);
                        
                        processedFiles++;
                        
                        // Report progress periodically
                        if (processedFiles % reportInterval == 0 || processedFiles == totalFiles)
                        {
                            logCallback?.Invoke($"({processedFiles}/{totalFiles} files processed)");
                        }
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"Error reading file {file}: {ex.Message}");
                    }
                }
            });

            return result;
        }

        private string[] GetSmaliFiles(string projectPath, Action<string> logCallback)
        {
            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };

                return Directory.GetFiles(projectPath, "*.smali", options);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                logCallback?.Invoke($"Warning: Unable to enumerate some folders due to sandbox/file-system restrictions: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private void AnalyzeFile(
            string filePath,
            AnalysisResult result,
            AnalysisRuleSet rules,
            Dictionary<string, List<Regex>> regexCache,
            IReadOnlyList<string> categoryOrder,
            string projectRoot)
        {
            // Skip library classes
            if (IsLibraryClass(filePath, projectRoot, rules.LibraryPaths))
            {
                return;
            }

            var lineNumber = 0;
            var deduplicationSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var enableCategoryEarlyExit = false;
            var detectedCategories = enableCategoryEarlyExit
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : null;
            foreach (var line in File.ReadLines(filePath))
            {
                lineNumber++;

                foreach (var category in categoryOrder)
                {
                    if (detectedCategories != null && detectedCategories.Contains(category))
                    {
                        continue;
                    }

                    if (!regexCache.TryGetValue(category, out var cachedPatterns))
                    {
                        continue;
                    }

                    if (CheckPatterns(line, cachedPatterns))
                    {
                        AddFinding(category, filePath, lineNumber, line, result, deduplicationSet);

                        if (detectedCategories != null)
                        {
                            detectedCategories.Add(category);
                        }
                    }
                }
            }
        }

        private bool IsLibraryClass(string filePath, string projectRoot, List<string> libraryPaths)
        {
            // Normalize path separators for comparison
            var normalizedPath = Path.GetFullPath(filePath).Replace('\\', '/');
            var normalizedRoot = projectRoot;
            var relativePath = normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                ? normalizedPath.Substring(normalizedRoot.Length)
                : normalizedPath;
            var pathSegments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

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

        private Dictionary<string, List<Regex>> BuildRegexCache(AnalysisRuleSet rules, Action<string> logCallback)
        {
            var cache = new Dictionary<string, List<Regex>>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < rules.Rules.Count; i++)
            {
                var rule = rules.Rules[i];
                var category = rule.Category ?? string.Empty;
                if (!cache.TryGetValue(category, out var compiledPatterns))
                {
                    compiledPatterns = new List<Regex>();
                    cache[category] = compiledPatterns;
                }

                foreach (var pattern in rule.RegexPatterns)
                {
                    try
                    {
                        compiledPatterns.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
                    }
                    catch (ArgumentException ex)
                    {
                        logCallback?.Invoke($"Warning: Skipping invalid regex pattern '{pattern}' in category '{rule.Category}': {ex.Message}");
                    }
                }
            }

            return cache;
        }

        private List<string> BuildCategoryOrder(AnalysisRuleSet rules, Dictionary<string, List<Regex>> regexCache)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var orderedCategories = new List<string>();

            foreach (var rule in rules.Rules)
            {
                var category = rule.Category ?? string.Empty;
                if (regexCache.ContainsKey(category) && seen.Add(category))
                {
                    orderedCategories.Add(category);
                }
            }

            return orderedCategories;
        }

        private void AddFinding(
            string category,
            string filePath,
            int lineNumber,
            string line,
            AnalysisResult result,
            HashSet<string> deduplicationSet)
        {
            var normalizedPath = Path.GetFullPath(filePath).Replace('\\', '/');
            var deduplicationKey = $"{category}|{normalizedPath}|{lineNumber}";
            if (!deduplicationSet.Add(deduplicationKey))
            {
                return;
            }

            var finding = new Finding
            {
                FilePath = filePath,
                LineNumber = lineNumber,
                Context = line.Trim()
            };

            switch (category.ToLowerInvariant())
            {
                case "root_check":
                    result.RootChecks.Add(finding);
                    break;
                case "emulator_check":
                    result.EmulatorChecks.Add(finding);
                    break;
                case "hardcoded_creds":
                    result.HardcodedCredentials.Add(finding);
                    break;
                case "sql_query":
                    result.SqlQueries.Add(finding);
                    break;
                case "http_url":
                    result.HttpUrls.Add(finding);
                    break;
                // Add other categories here if the UI supports them or use a generic list if available
            }
        }

        private bool CheckPatterns(string line, List<Regex> patterns)
        {
            foreach (var pattern in patterns)
            {
                if (pattern.IsMatch(line))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public class AnalysisResult
    {
        public HashSet<string> ActiveCategories { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<Finding> RootChecks { get; set; } = new List<Finding>();
        public List<Finding> EmulatorChecks { get; set; } = new List<Finding>();
        public List<Finding> HardcodedCredentials { get; set; } = new List<Finding>();
        public List<Finding> SqlQueries { get; set; } = new List<Finding>();
        public List<Finding> HttpUrls { get; set; } = new List<Finding>();
    }

    public class Finding
    {
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Context { get; set; } = string.Empty;
    }
}
