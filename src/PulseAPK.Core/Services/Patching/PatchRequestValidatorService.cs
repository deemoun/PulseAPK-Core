using PulseAPK.Core.Models;
using System.Text;
using System.Text.Json;

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
        else if (!string.IsNullOrWhiteSpace(request.ConfigFilePath))
        {
            var validInteractionPaths = GadgetAssetPlacementStrategy.GetValidInteractionPaths(request);
            errors.AddRange(ValidateGadgetConfig(request.ConfigFilePath, validInteractionPaths));
        }

        if (!string.IsNullOrWhiteSpace(request.ScriptFilePath) && !File.Exists(request.ScriptFilePath))
        {
            errors.Add($"Script file '{request.ScriptFilePath}' was not found.");
        }

        return errors;
    }

    private static IEnumerable<string> ValidateGadgetConfig(string configPath, IReadOnlyList<string> expectedInteractionPaths)
    {
        byte[] configBytes;
        try
        {
            configBytes = File.ReadAllBytes(configPath);
        }
        catch (Exception ex)
        {
            return [$"Config file '{configPath}' could not be read: {ex.Message}"];
        }

        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(configBytes);
        }
        catch (DecoderFallbackException)
        {
            return [$"Config file '{configPath}' must be UTF-8 plain text."];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(configBytes);
        }
        catch (JsonException ex)
        {
            return [$"Config file '{configPath}' is not valid JSON: {ex.Message}"];
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("interaction", out var interaction))
            {
                return [$"Config file '{configPath}' must contain interaction.path set to one of: {string.Join(", ", expectedInteractionPaths.Select(p => $"'{p}'"))}."];
            }

            if (!interaction.TryGetProperty("path", out var pathElement) ||
                pathElement.ValueKind != JsonValueKind.String)
            {
                return [$"Config file '{configPath}' must set interaction.path to one of: {string.Join(", ", expectedInteractionPaths.Select(p => $"'{p}'"))}."];
            }

            var configuredPath = pathElement.GetString();
            if (!expectedInteractionPaths.Contains(configuredPath, StringComparer.Ordinal))
            {
                return [$"Config file '{configPath}' sets interaction.path='{configuredPath}', expected one of: {string.Join(", ", expectedInteractionPaths.Select(p => $"'{p}'"))}."];
            }
        }

        return [];
    }
}
