using PulseAPK.Core.Models;
using PulseAPK.Core.ViewModels;

namespace PulseAPK.Tests.ViewModels;

public sealed class CommandLogFormatterTests
{
    [Fact]
    public void FormatCommandResult_UsesCommandAndOutputLabelsOnly()
    {
        var result = new AdbCommandResult(
            "/home/deem/Android/Sdk/platform-tools/adb",
            ["devices", "-l"],
            "List of devices attached\n",
            string.Empty,
            0,
            false);

        var formatted = CommandLogFormatter.FormatCommandResult(result);

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "Command: /home/deem/Android/Sdk/platform-tools/adb devices -l",
                "Output:",
                "List of devices attached"),
            formatted);
        Assert.DoesNotContain("stdout", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stderr", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exit code", formatted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatCommandResult_CombinesStandardOutputAndStandardErrorAsOutput()
    {
        var result = new AdbCommandResult(
            "adb",
            ["install", "app.apk"],
            "performing streamed install\n",
            "adb: failed to install app.apk\n",
            1,
            false);

        var formatted = CommandLogFormatter.FormatCommandResult(result);

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "Command: adb install app.apk",
                "Output:",
                "performing streamed install",
                "adb: failed to install app.apk"),
            formatted);
    }

    [Fact]
    public void FormatCommandResult_UsesEmptyOutputPlaceholder()
    {
        var result = new AdbCommandResult("adb", ["devices"], string.Empty, "  ", 0, false);

        var formatted = CommandLogFormatter.FormatCommandResult(result);

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "Command: adb devices",
                "Output:",
                "(empty)"),
            formatted);
    }
}
