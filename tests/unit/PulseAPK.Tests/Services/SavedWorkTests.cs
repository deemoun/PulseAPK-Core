using PulseAPK.Core.Services;

namespace PulseAPK.Tests.Services;

public class SavedWorkTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pulse-saves-").FullName;
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task ReportsPreserveSequentialAndConcurrentExports()
    {
        var service = new ReportService(() => _root, () => new DateTime(2026, 1, 2, 3, 4, 5));
        var first = await service.SaveReportAsync("first", "project");
        var second = await service.SaveReportAsync("second", "project");
        Assert.Equal("2026-01-02-03-04-05-project.txt", Path.GetFileName(first));
        Assert.Equal("2026-01-02-03-04-05-project-1.txt", Path.GetFileName(second));

        var paths = await Task.WhenAll(Enumerable.Range(0, 12).Select(i =>
            Task.Run(() => service.SaveReportAsync($"report {i}", "project"))));
        Assert.Equal(paths.Length, paths.Distinct().Count());
        for (int i = 0; i < paths.Length; i++)
            Assert.Equal($"report {i}", File.ReadAllText(paths[i]));
        Assert.Equal("first", File.ReadAllText(first));
        Assert.Equal("second", File.ReadAllText(second));
    }

    [Fact]
    public void SettingsMigrateReloadAndSurviveFailedPublication()
    {
        var current = Directory.CreateDirectory(Path.Combine(_root, "current")).FullName;
        File.WriteAllText(Path.Combine(_root, "settings.json"), """{"SelectedLanguage":"de-DE"}""");
        var service = new SettingsService(current, _root);
        Assert.Equal("de-DE", service.Settings.SelectedLanguage);
        int notifications = 0;
        service.SettingsChanged += (_, _) => notifications++;
        service.Settings.IsDeviceToolsEnabled = true;
        service.Save();
        service.Save();
        Assert.Equal(1, notifications);

        var path = Path.Combine(current, "settings.json");
        var previous = File.ReadAllBytes(path);
        var failing = new SettingsService(current, _root, (_, _) => throw new IOException("Replacement failed"));
        Assert.True(failing.Settings.IsDeviceToolsEnabled);
        failing.SettingsChanged += (_, _) => notifications++;
        failing.Settings.IsDeviceToolsEnabled = false;
        Assert.Throws<IOException>(failing.Save);
        Assert.Equal(previous, File.ReadAllBytes(path));
        Assert.True(new SettingsService(current, _root).Settings.IsDeviceToolsEnabled);
        Assert.Equal(1, notifications);
        Assert.Equal(path, Assert.Single(Directory.GetFiles(current)));
    }
}
