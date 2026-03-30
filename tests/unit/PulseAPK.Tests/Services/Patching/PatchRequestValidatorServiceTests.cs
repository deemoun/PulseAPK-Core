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
        var scriptPath = Path.Combine(root, "script.so");
        File.WriteAllText(inputApk, "apk");
        File.WriteAllBytes(scriptPath, [0x7F, (byte)'E', (byte)'L', (byte)'F', 1, 1, 1, 1]);
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
            ConfigFilePath = configPath,
            ScriptFilePath = scriptPath
        };

        var errors = service.Validate(request);

        Assert.Contains(errors, static error => error.Contains("./libfrida-gadget.script.so", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AllowsAssetsInteractionPath_WhenSafeModeIsSelectedForNonElfScript()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var inputApk = Path.Combine(root, "input.apk");
        var outputApk = Path.Combine(root, "output.apk");
        var configPath = Path.Combine(root, "frida-gadget.config");
        var scriptPath = Path.Combine(root, "script.js");
        File.WriteAllText(inputApk, "apk");
        File.WriteAllText(scriptPath, "console.log('safe mode');");
        File.WriteAllText(configPath, """
{
  "interaction": {
    "type": "script",
    "path": "./assets/frida/libfrida-gadget.script.so"
  }
}
""");

        var service = new PatchRequestValidatorService();
        var request = new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            ConfigFilePath = configPath,
            ScriptFilePath = scriptPath
        };

        var errors = service.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_AllowsLegacyLibInteractionPath_WhenSafeModeIsSelectedForNonElfScript()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var inputApk = Path.Combine(root, "input.apk");
        var outputApk = Path.Combine(root, "output.apk");
        var configPath = Path.Combine(root, "frida-gadget.config");
        var scriptPath = Path.Combine(root, "script.js");
        File.WriteAllText(inputApk, "apk");
        File.WriteAllText(scriptPath, "console.log('safe mode legacy path');");
        File.WriteAllText(configPath, """
{
  "interaction": {
    "type": "script",
    "path": "./libfrida-gadget.script.so"
  }
}
""");

        var service = new PatchRequestValidatorService();
        var request = new PatchRequest
        {
            InputApkPath = inputApk,
            OutputApkPath = outputApk,
            ConfigFilePath = configPath,
            ScriptFilePath = scriptPath
        };

        var errors = service.Validate(request);

        Assert.Empty(errors);
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
        var config = "{ \"interaction\": { \"path\": \"./libfrida-gadget.script.so\" } }";
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
