using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulseAPK.Core.Services
{
    public class AnalysisRuleSet
    {
        [JsonPropertyName("library_paths")]
        public List<string> LibraryPaths { get; set; } = new();

        [JsonPropertyName("rules")]
        public List<AnalysisRule> Rules { get; set; } = new();
    }

    public class AnalysisRule
    {
        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("regex_patterns")]
        public List<string> RegexPatterns { get; set; } = new();
    }

    public static class AnalysisRulesLoader
    {
        private const string RulesFileName = "smali_analysis_rules.json";
        private const string AppName = "PulseAPK";
        private const string DefaultRulesJson = """
            {
              "library_paths": [
                "\\androidx\\",
                "\\kotlin\\",
                "\\kotlinx\\",
                "\\com\\google\\",
                "\\com\\squareup\\",
                "\\okhttp3\\",
                "\\okio\\",
                "\\retrofit2\\",
                "\\com\\android\\",
                "\\android\\support\\"
              ],
              "rules": [
                {
                  "category": "root_check",
                  "description": "Runtime exec calls and common root binaries",
                  "severity": "high",
                  "regex_patterns": [
                    "Runtime.*exec.*[\\\"'](su|magisk|busybox|superuser|xposed|zygisk)[\\\"']",
                    "const-string.*[\\\"/]system/(xbin|bin)/(su|magisk|busybox|toybox)[\\\"']",
                    "const-string.*[\\\"/]sbin/(su|magisk)[\\\"']",
                    "const-string.*[\\\"']/(vendor|system/vendor)/bin/magisk[\\\"']",
                    "const-string.*[\\\"'](com\\.topjohnwu\\.magisk|io\\.topjohnwu\\.magisk)",
                    "const-string.*[\\\"']eu\\.chainfire\\.supersu",
                    "const-string.*[\\\"']com\\.noshufou\\.android\\.su",
                    "const-string.*[\\\"']com\\.koushikdutta\\.superuser",
                    "const-string.*[\\\"'](de\\.robv\\.android\\.xposed|org\\.lsposed)",
                    "const-string.*[\\\"'](com\\.thirdparty\\.superuser|com\\.kingoapp\\.root|com\\.kingroot\\.kinguser)",
                    "const-string.*[\\\"/]system/app/(Superuser|SuperSU|Magisk)",
                    "const-string.*[\\\"']/(data/local/tmp|/cache/magisk)[\\\"']",
                    "const-string.*[\\\"']ro\\.build\\.tags[\\\"'].*(test-keys|dev-keys|userdebug)",
                    "Build.*TAGS.*(test-keys|dev-keys|userdebug)",
                    "const-string.*[\\\"']ro\\.debuggable[\\\"']",
                    "const-string.*[\\\"']ro\\.debuggable[\\\"'].*=1",
                    "const-string.*[\\\"']ro\\.secure[\\\"'].*=0",
                    "const-string.*[\\\"']persist\\.sys\\.magisk[\\\"']",
                    "const-string.*[\\\"']zygisk[\\\"']",
                    "const-string.*[\\\"']/(vendor|system/vendor)/(x?bin|bin)/(su|magisk)[\\\"']",
                    "const-string.*[\\\"']/(system/)?vendor/su[\\\"']",
                    "const-string.*[\\\"']/(s?bin|vendor)/su[\\\"']",
                    "const-string.*[\\\"']/system/(xbin|bin)/daemonsu[\\\"']",
                    "const-string.*[\\\"']/(system|vendor|product)/etc/init/[^\\\"']*su\\.rc[\\\"']",
                    "const-string.*[\\\"']mount[\\\"'].*(system|vendor).*(rw|remount)",
                    "const-string.*(access|fopen|stat|lstat)\\(.*[\\\"']/system/(xbin|bin)/su[\\\"']",
                    "const-string.*[\\\"']/system/xbin/su[\\\"'].*access",
                    "RootBeer",
                    "RootTools",
                    "isDeviceRooted",
                    "checkRootMethod",
                    "findBinary",
                    "canExecuteSu"
                  ]
                },
                {
                  "category": "emulator_check",
                  "description": "System property and Build checks for emulators",
                  "severity": "high",
                  "regex_patterns": [
                    "const-string.*[\\\"']ro\\.kernel\\.qemu[\\\"']",
                    "const-string.*[\\\"']ro\\.hardware[\\\"'].*(goldfish|ranchu|qemu)",
                    "const-string.*[\\\"']ro\\.product\\.model[\\\"'].*sdk",
                    "const-string.*[\\\"']ro\\.product\\.model[\\\"'].*Emulator",
                    "const-string.*[\\\"'](generic_x86|sdk_gphone|google_sdk|sdk.*x86|AOSP on IA Emulator)[\\\"']",
                    "const-string.*[\\\"'](vbox86p|vbox86|twrp_emulator)[\\\"']",
                    "const-string.*[\\\"'](Genymotion|BlueStacks|NoxPlayer|MEmu|LDPlayer|Memu|Andyroid|Droid4X)[\\\"']",
                    "const-string.*[\\\"'](15555215554|15555215556|15555215558|15555215560)[\\\"']",
                    "const-string.*[\\\"']0000000000000000[\\\"']",
                    "const-string.*[\\\"'](qemu_prop|qemu\\.hw\\.mainkeys|init\\.rc_qemu|init\\.goldfish|qemud)[\\\"']",
                    "const-string.*[\\\"']ro\\.product\\.(brand|device|name)[\\\"'].*(generic|emulator|vbox|sdk_gphone)",
                    "const-string.*[\\\"']ro\\.hardware[\\\"'].*(vbox|ttvm)",
                    "const-string.*[\\\"']gsm\\.operator\\.numeric[\\\"'].*(310260|000000)",
                    "const-string.*[\\\"']gsm\\.operator\\.alpha[\\\"'].*(Android|Test)",
                    "Build.*FINGERPRINT.*(generic|vbox|emulator|sdk|andy|tstvbox|x86)",
                    "Build.*FINGERPRINT.*test-keys",
                    "Build.*MODEL.*(sdk|Emulator|Android SDK built for|vbox86p)",
                    "Build.*MANUFACTURER.*(unknown|Genymotion|Google|vbox|tstvbox)",
                    "const-string.*[\\\"'](ranchu|goldfish|qemu)\\.pipe[\\\"']",
                    "isEmulator",
                    "checkEmulator",
                    "detectEmulator",
                    "const-string.*[\\\"']/dev/(qemu_pipe|ranchu_pipe|vboxguest|vport|qemu_trace|goldfish_pipe)[\\\"']",
                    "const-string.*[\\\"']/proc/tty/drivers[\\\"']",
                    "const-string.*[\\\"']/proc/cpuinfo[\\\"'].*(intel|amd).*hypervisor",
                    "const-string.*[\\\"']hypervisor[\\\"'].*(Intel|AMD|VirtualBox|KVM)",
                    "const-string.*[\\\"'](vbox|virtio|qemu|tcg)[\\\"'].*cpu"
                  ]
                },
                {
                  "category": "hardcoded_creds",
                  "description": "Hardcoded secrets, tokens, and credentials",
                  "severity": "medium",
                  "regex_patterns": [
                    "const-string.*[\\\"']Authorization:\\s*(Bearer|Basic|Token)\\s+[A-Za-z0-9\\-_\\.]+",
                    "const-string.*[\\\"'](Bearer|Basic)\\s+[A-Za-z0-9\\-_\\.]{20,}[\\\"']",
                    "sput-object.*\\.(api_?key|api_?token|auth_?token|access_?token)",
                    "const-string.*(?i)(key|secret|token|password|passphrase).{0,8}[\\\"'][A-Za-z0-9+/]{32,}={0,2}[\\\"']",
                    "const-string.*(?i)(key|secret|token|password|passphrase).{0,8}[\\\"'][0-9A-Fa-f]{32,}[\\\"']",
                    "const-string.*(?i)(key|secret|token|password).{0,6}[\\\"'][^\\\"']{12,}[\\\"']",
                    "\\.field.*(?i)(password|secret|api_?key|access_?token).*Ljava/lang/String;.*[\\\"'][^\\\"']{6,}[\\\"']",
                    "const-string.*[\\\"'][a-zA-Z0-9_]+:[a-zA-Z0-9_]{8,}@",
                    "const-string.*[\\\"'](AKIA|ASIA)[A-Z0-9]{16}[\\\"']",
                    "const-string.*[\\\"']AIza[A-Za-z0-9\\-_]{35}[\\\"']",
                    "const-string.*(?i)(client_?secret|app_?secret|db_?password).*[\\\"'][^\\\"']{8,}[\\\"']",
                    "const-string.*(?i)(key|secret|token|pass(word)?|client_?secret).{0,8}[\\\"']AAA[A-Za-z0-9_-]{8,}[\\\"']",
                    "const-string.*(?i)(key|secret|token|pass(word)?|client_?secret).{0,8}[\\\"'][A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{10,}\\.[A-Za-z0-9_-]{10,}[\\\"']",
                    "const-string.*(?i)(key|secret|token|pass(word)?|client_?secret).{0,8}[\\\"'][0-9]{8,10}:[A-Za-z0-9_-]{30,}[\\\"']",
                    "const-string.*(?i)(key|secret|token|pass(word)?|client_?secret).{0,8}[\\\"']AAAAAI[a-zA-Z0-9_-]{8,}[\\\"']"
                  ]
                },
                {
                  "category": "sql_query",
                  "description": "SQL statements and database calls",
                  "severity": "medium",
                  "regex_patterns": [
                    "const-string.*[\\\"']\\s*SELECT\\s+\\*?\\s+(FROM|[a-zA-Z_])",
                    "const-string.*SELECT[^\\\"']*[\\\"']\\s*\\+",
                    "const-string.*[\\\"']\\s*INSERT\\s+INTO\\s+",
                    "const-string.*[\\\"']\\s*UPDATE\\s+\\w+\\s+SET\\s+",
                    "const-string.*[\\\"']\\s*DELETE\\s+FROM\\s+",
                    "const-string.*[\\\"']\\s*CREATE\\s+TABLE\\s+",
                    "const-string.*[\\\"']\\s*DROP\\s+TABLE\\s+",
                    "const-string.*[\\\"']\\s*ALTER\\s+TABLE\\s+",
                    "const-string.*\\s+WHERE\\s+\\w+\\s*=",
                    "invoke-virtual.*SQLiteDatabase;->execSQL\\(Ljava/lang/String",
                    "invoke-virtual.*SQLiteDatabase;->rawQuery\\(Ljava/lang/String",
                    "invoke-virtual.*SQLiteDatabase;->compileStatement\\("
                  ]
                },
                {
                  "category": "http_url",
                  "description": "HTTP/HTTPS URLs and API endpoints",
                  "severity": "medium",
                  "regex_patterns": [
                    "const-string.*[\\\"']https?://[a-zA-Z0-9\\-\\.]+\\.[a-zA-Z]{2,}[/\\w\\-\\._~:/?#\\[\\]@!$&'()*+,;=]*[\\\"']",
                    "const-string.*[\\\"']http://[a-zA-Z0-9\\-\\.]+\\.[a-zA-Z]{2,}",
                    "https?://[^\\\"']+/(api|v1|v2|v3)/",
                    "https?://[^\\\"']+/(auth|login|signin|signup|register)/",
                    "https?://[^\\\"']+/(user|account|profile)/",
                    "https?://[^\\\"']+/(token|oauth|refresh)/",
                    "https?://[^\\\"']+/(payment|checkout|billing)/",
                    "https?://[^\\\"']+/(admin|dashboard)/",
                    "const-string.*[\\\"']https?://[^\\\"']+/graphql[\\\"']",
                    "const-string.*(?i)(endpoint|callback|webhook|graphql)[^\\n]{0,20}https?://[^\\\"']+",
                    "https?://[^\\\"']+/(rest|api/v[0-9]+|callback|webhook)/"
                  ]
                }
              ]
            }
            """;

        /// <summary>
        /// Loads rules from the file. If missing, it creates it with defaults.
        /// If valid, returns loaded rules.
        /// If invalid/error, returns default rules (without overwriting the file, to preserve user edits).
        /// </summary>
        public static object InitializeRules()
        {
            var filePath = EnsureRulesFileExists();

            try
            {
                var fileContents = File.ReadAllText(filePath);
                var rules = JsonSerializer.Deserialize<AnalysisRuleSet>(fileContents, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (rules != null)
                {
                    return rules;
                }
            }
            catch (Exception)
            {
                // Fallback to defaults.
                // We intentionally do NOT overwrite the bad file here, so the user can fix their typo.
            }

            return GetDefaultRuleSet();
        }

        private static string EnsureRulesFileExists()
        {
            var baseDirectory = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
            var rulesFolder = ResolveRulesFolder();
            var userRulesPath = Path.Combine(rulesFolder, RulesFileName);

            if (File.Exists(userRulesPath))
            {
                return userRulesPath;
            }

            var legacyPaths = new[]
            {
                Path.Combine(baseDirectory, RulesFileName),
                // When running from the build output, walk up to the project root for development convenience.
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", RulesFileName))
            };

            foreach (var legacyPath in legacyPaths)
            {
                if (!File.Exists(legacyPath) || string.Equals(legacyPath, userRulesPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    File.Copy(legacyPath, userRulesPath, overwrite: false);
                    return userRulesPath;
                }
                catch
                {
                    // Ignore copy errors and keep checking additional legacy paths.
                }
            }

            try
            {
                File.WriteAllText(userRulesPath, DefaultRulesJson);
            }
            catch
            {
                // Ignore write errors (e.g. permissions), we will just return the path and fail to read later, falling back to defaults in memory.
            }

            return userRulesPath;
        }

        private static string ResolveRulesFolder()
        {
            var appDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(appDataDirectory))
            {
                appDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            if (string.IsNullOrWhiteSpace(appDataDirectory))
            {
                appDataDirectory = Environment.CurrentDirectory;
            }

            var rulesDirectory = Path.Combine(appDataDirectory, AppName);
            Directory.CreateDirectory(rulesDirectory);
            return rulesDirectory;
        }

        private static AnalysisRuleSet GetDefaultRuleSet()
        {
            return JsonSerializer.Deserialize<AnalysisRuleSet>(DefaultRulesJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new AnalysisRuleSet();
        }
    }
}
