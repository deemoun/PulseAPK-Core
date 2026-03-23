using PulseAPK.Core.Models;
using PulseAPK.Core.Services.Patching;
using System.Text;

namespace PulseAPK.Tests.Services.Patching;

public class PatchRequestValidatorServiceTests
{
    [Fact]
    public void Validate_ReturnsErrors_WhenRequiredInputsMissing()
    {
        var service = new PatchRequestValidatorService();
        var request = new PatchRequest();

        var errors = service.Validate(request);

        Assert.Contains(errors, error => error.Contains("Input APK", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("Output APK", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ReturnsError_WhenConfigJsonIsInvalid()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var inputApk = Path.Combine(root, "input.apk");
        var outputApk = Path.Combine(root, "output.apk");
        var configPath = Path.Combine(root, "frida-gadget.config");
        File.WriteAllText(inputApk, "apk");
        File.WriteAllText(configPath, "{invalid json");

        var service = new PatchRequestValidatorService();
        var request = new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            ConfigFilePath = configPath
        };

        var errors = service.Validate(request);

        Assert.Contains(errors, static error => error.Contains("not valid JSON", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ReturnsError_WhenConfigPathIsNotExpectedScriptPath()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var inputApk = Path.Combine(root, "input.apk");
        var outputApk = Path.Combine(root, "output.apk");
        var configPath = Path.Combine(root, "frida-gadget.config");
        File.WriteAllText(inputApk, "apk");
        File.WriteAllText(configPath, """
{
  "interaction": {
    "type": "script",
    "path": "./script.js"
  }
}
""");

        var service = new PatchRequestValidatorService();
        var request = new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            ConfigFilePath = configPath
        };

        var errors = service.Validate(request);

        Assert.Contains(errors, static error => error.Contains("../../assets/frida-gadget/script.js", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ReturnsError_WhenConfigIsNotUtf8()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var inputApk = Path.Combine(root, "input.apk");
        var outputApk = Path.Combine(root, "output.apk");
        var configPath = Path.Combine(root, "frida-gadget.config");
        File.WriteAllText(inputApk, "apk");
        var config = "{ \"interaction\": { \"path\": \"../../assets/frida-gadget/script.js\" } }";
        File.WriteAllBytes(configPath, Encoding.Unicode.GetBytes(config));

        var service = new PatchRequestValidatorService();
        var request = new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            ConfigFilePath = configPath
        };

        var errors = service.Validate(request);

        Assert.Contains(errors, static error => error.Contains("UTF-8 plain text", StringComparison.Ordinal));
    }
}
