using PulseAPK.Core.Services.Patching;

namespace PulseAPK.Tests.Services.Patching;

public class ActivityDetectionServiceTests
{
    [Fact]
    public async Task DetectMainActivityAsync_LauncherAliasWithValidTargetActivity_ReturnsTargetActivity()
    {
        var root = CreateManifest(@"<manifest xmlns:android='http://schemas.android.com/apk/res/android' package='com.example'>
  <application>
    <activity android:name='com.example.MainActivity' />
    <activity-alias android:name='com.example.EntryAlias' android:targetActivity='com.example.MainActivity'>
      <intent-filter>
        <action android:name='android.intent.action.MAIN' />
        <category android:name='android.intent.category.LAUNCHER' />
      </intent-filter>
    </activity-alias>
  </application>
</manifest>");

        var service = new ActivityDetectionService();
        var result = await service.DetectMainActivityAsync(root);

        Assert.Equal("com.example.MainActivity", result.ActivityName);
        Assert.Null(result.Warning);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task DetectMainActivityAsync_LauncherAliasWithoutTargetActivity_FallsBackToFirstConcreteActivityWithWarning()
    {
        var root = CreateManifest(@"<manifest xmlns:android='http://schemas.android.com/apk/res/android' package='com.example'>
  <application>
    <activity android:name='com.example.FallbackActivity' />
    <activity android:name='com.example.SecondActivity' />
    <activity-alias android:name='com.example.EntryAlias'>
      <intent-filter>
        <action android:name='android.intent.action.MAIN' />
        <category android:name='android.intent.category.LAUNCHER' />
      </intent-filter>
    </activity-alias>
  </application>
</manifest>");

        var service = new ActivityDetectionService();
        var result = await service.DetectMainActivityAsync(root);

        Assert.Equal("com.example.FallbackActivity", result.ActivityName);
        Assert.Equal("Launcher activity alias has missing or invalid targetActivity. Falling back to first concrete activity in manifest.", result.Warning);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task DetectMainActivityAsync_PrefersMainLauncherActivity_WhenLauncherIsConcreteActivity()
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
        Assert.Null(result.Warning);
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
