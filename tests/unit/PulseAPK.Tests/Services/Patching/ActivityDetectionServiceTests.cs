using PulseAPK.Core.Services.Patching;

namespace PulseAPK.Tests.Services.Patching;

public class ActivityDetectionServiceTests
{
    [Fact]
    public async Task DetectMainActivityAsync_PrefersMainLauncherActivity()
    {
        var root = CreateManifest(@"<manifest xmlns:android='http://schemas.android.com/apk/res/android' package='com.example'>
  <application>
    <activity android:name='com.example.FallbackActivity' />
    <activity android:name='com.example.MainActivity'>
      <intent-filter>
        <action android:name='android.intent.action.MAIN' />
        <category android:name='android.intent.category.LAUNCHER' />
      </intent-filter>
    </activity>
  </application>
</manifest>");

        var service = new ActivityDetectionService();
        var result = await service.DetectMainActivityAsync(root);

        Assert.Equal("com.example.MainActivity", result.ActivityName);
        Assert.Null(result.Error);
    }

    private static string CreateManifest(string manifest)
    {
        var root = Path.Combine(Path.GetTempPath(), $"pulseapk-manifest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "AndroidManifest.xml"), manifest);
        return root;
    }
}
