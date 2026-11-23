using System.Diagnostics;
using System.IO.Compression;
using PulseAPK.Core.Services;
using Xunit.Abstractions;

namespace PulseAPK.Tests.Acceptance;

public sealed class AndroidToolchainFactAttribute : FactAttribute
{
    public AndroidToolchainFactAttribute()
    {
        if (new[] { "PULSE_ANDROID_BUILD_TOOLS", "PULSE_ANDROID_JAR", "PULSE_APKTOOL", "PULSE_SIGNER" }
            .Any(name => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))))
            Skip = "Requires configured Android Build Tools, android.jar, apktool and signer.";
    }
}

public sealed class AndroidWorkflowTests(ITestOutputHelper log)
{
    [AndroidToolchainFact]
    public async Task BuildDecodeFindRebuildSignAndVerify()
    {
        static string Env(string name) => Environment.GetEnvironmentVariable(name)!;
        string tool(string name) => Path.Combine(Env("PULSE_ANDROID_BUILD_TOOLS"), OperatingSystem.IsWindows() ? name + ".exe" : name);
        var root = Directory.CreateTempSubdirectory("pulse acceptance ").FullName;
        try
        {
            var settings = new SettingsService(root, root);
            settings.Settings.ApktoolPath = Env("PULSE_APKTOOL");
            settings.Settings.UbersignPath = Env("PULSE_SIGNER");
            var manifest = Path.Combine(root, "AndroidManifest.xml");
            File.WriteAllText(manifest, """<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="org.pulse.acceptance" android:versionCode="1"><uses-sdk android:minSdkVersion="23" android:targetSdkVersion="28"/><application android:label="@string/app_name"/></manifest>""");
            var input = Path.Combine(root, "input.apk");
            log.WriteLine(await Run(tool("aapt2"), "version"));
            log.WriteLine(await Run("java", "-jar", settings.Settings.ApktoolPath, "--version"));
            log.WriteLine(await Run("java", "-jar", settings.Settings.UbersignPath, "--version"));
            var res = Directory.CreateDirectory(Path.Combine(root, "res", "values"));
            File.WriteAllText(Path.Combine(res.FullName, "strings.xml"), "<resources><string name='app_name'>Pulse acceptance</string></resources>");
            var compiled = Path.Combine(root, "resources.zip");
            await Run(tool("aapt2"), "compile", "--dir", res.Parent!.FullName, "-o", compiled);
            await Run(tool("aapt2"), "link", compiled, "-I", Env("PULSE_ANDROID_JAR"), "--manifest", manifest, "-o", input);
            using (var zip = ZipFile.Open(input, ZipArchiveMode.Update))
                zip.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ordinary.dex"), "classes.dex");
            var runner = new ApktoolRunner(settings);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var project = Path.Combine(root, "decoded");
            var decoded = await runner.RunDecompileAsync(input, project, true, true, false, cancellationToken: timeout.Token);
            Assert.True(decoded.ExitCode == 0, string.Join("\n", decoded.Stderr));
            var search = new SmaliAnalyserService().Scan(project, new(), null, timeout.Token, search: new ProjectSearch("orange orchard", true, ".smali", false));
            Assert.Single(search.Findings);
            var rebuilt = Path.Combine(root, "rebuilt.apk");
            var build = await runner.RunBuildAsync(project, rebuilt, false, timeout.Token);
            Assert.True(build.ExitCode == 0, string.Join("\n", build.Stderr));
            var signed = Path.Combine(root, "signed.apk");
            Assert.Equal(0, await new UbersignRunner(settings).RunSigningAsync(rebuilt, signed, timeout.Token));
            await Run("java", "-jar", Path.Combine(Env("PULSE_ANDROID_BUILD_TOOLS"), "lib", "apksigner.jar"), "verify", "--verbose", signed);
            Assert.Contains("package: name='org.pulse.acceptance'", await Run(tool("aapt2"), "dump", "badging", signed));
        }
        finally { Directory.Delete(root, true); }
    }

    [AndroidToolchainFact]
    public async Task AndroidReaderAgreesOnPresentAbsentAndMalformedEntries()
    {
        var root = Directory.CreateTempSubdirectory("pulse-dex-").FullName;
        try
        {
            var dexdump = Path.Combine(Environment.GetEnvironmentVariable("PULSE_ANDROID_BUILD_TOOLS")!,
                OperatingSystem.IsWindows() ? "dexdump.exe" : "dexdump");
            var names = new[] { "Ordinary.dex", "Meadow.dex" };
            var path = Path.Combine(root, "mix.apk");
            var truncated = Path.Combine(root, "truncated.dex");
            File.WriteAllBytes(truncated, [0x64, 0x65, 0x78]);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                for (int i = 0; i < names.Length; i++)
                {
                    var dex = Path.Combine(AppContext.BaseDirectory, "Fixtures", names[i]);
                    var output = await Run(dexdump, "-d", dex);
                    Assert.Equal(i == 0, output.Contains("orange orchard", StringComparison.Ordinal));
                    zip.CreateEntryFromFile(dex, i == 0 ? "classes.dex" : "classes2.dex");
                }
                zip.CreateEntryFromFile(truncated, "classes3.dex");
            }
            await Run(dexdump, ["-d", truncated], expectFailure: true);
            var results = await ReadOnlyDexSearch.InspectAsync(path, "orange orchard", default);
            Assert.Equal(new DexEntrySearch("classes.dex", 1, null), results[0]);
            Assert.Equal(new DexEntrySearch("classes2.dex", 0, null), results[1]);
            Assert.NotNull(results[2].Error);
        }
        finally { Directory.Delete(root, true); }
    }

    private static Task<string> Run(string executable, params string[] arguments) => Run(executable, arguments, false);

    private static async Task<string> Run(string executable, string[] arguments, bool expectFailure)
    {
        var info = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        await PulseAPK.Core.Utils.ProcessLifetime.WaitAsync(process, timeout.Token);
        var output = await stdout + await stderr;
        Assert.True(expectFailure ? process.ExitCode != 0 : process.ExitCode == 0, output);
        return output;
    }
}
