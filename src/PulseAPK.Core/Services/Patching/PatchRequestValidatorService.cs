using PulseAPK.Core.Models;

namespace PulseAPK.Core.Services.Patching;

public sealed class PatchRequestValidatorService
{
    public IReadOnlyList<string> Validate(PatchRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.InputApkPath))
        {
            errors.Add("Input APK path is required.");
        }
        else if (!File.Exists(request.InputApkPath))
        {
            errors.Add($"Input APK '{request.InputApkPath}' was not found.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputApkPath))
        {
            errors.Add("Output APK path is required.");
        }

        if (!string.IsNullOrWhiteSpace(request.ConfigFilePath) && !File.Exists(request.ConfigFilePath))
        {
            errors.Add($"Config file '{request.ConfigFilePath}' was not found.");
        }

        if (!string.IsNullOrWhiteSpace(request.ScriptFilePath) && !File.Exists(request.ScriptFilePath))
        {
            errors.Add($"Script file '{request.ScriptFilePath}' was not found.");
        }

        return errors;
    }
}
