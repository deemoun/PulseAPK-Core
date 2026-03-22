using System.Net.Http.Headers;
using System.Text.Json;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Models;
using SharpCompress.Compressors.Xz;

namespace PulseAPK.Core.Services.Patching;

public sealed class FridaArtifactService : IFridaArtifactService
{
    private const string FridaReleasesApiUrl = "https://api.github.com/repos/frida/frida/releases/latest";

    private static readonly IReadOnlyDictionary<string, string> AbiToFridaSuffix = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["arm64-v8a"] = "android-arm64",
        ["armeabi-v7a"] = "android-arm",
        ["x86"] = "android-x86",
        ["x86_64"] = "android-x86_64"
    };

    private readonly HttpClient _httpClient;
    private readonly IToolRepository _toolRepository;

    public FridaArtifactService(HttpClient httpClient, IToolRepository toolRepository)
    {
        _httpClient = httpClient;
        _toolRepository = toolRepository;

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PulseAPK", "1.0"));
        }
    }

    public async Task<(string? GadgetPath, string? Error)> ResolveGadgetAsync(PatchRequest request, string architecture, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.CustomGadgetBinaryPath))
        {
            if (File.Exists(request.CustomGadgetBinaryPath))
            {
                return (request.CustomGadgetBinaryPath, null);
            }

            return (null, $"Custom gadget binary '{request.CustomGadgetBinaryPath}' was not found.");
        }

        var cachePath = _toolRepository.GetToolPath(Path.Combine("frida", architecture, "libfrida-gadget.so"));
        if (File.Exists(cachePath))
        {
            return (cachePath, null);
        }

        if (!AbiToFridaSuffix.TryGetValue(architecture, out var fridaArchitecture))
        {
            return (null, $"Frida gadget for ABI '{architecture}' was not found in tool cache: '{cachePath}'.");
        }

        try
        {
            await DownloadAndCacheGadgetAsync(fridaArchitecture, cachePath, cancellationToken);
            return (cachePath, null);
        }
        catch (Exception ex)
        {
            return (null, $"Frida gadget for ABI '{architecture}' was not found in tool cache: '{cachePath}'. Automatic download failed: {ex.Message}");
        }
    }

    private async Task DownloadAndCacheGadgetAsync(string fridaArchitecture, string cachePath, CancellationToken cancellationToken)
    {
        using var releaseResponse = await _httpClient.GetAsync(FridaReleasesApiUrl, cancellationToken);
        releaseResponse.EnsureSuccessStatusCode();

        await using var releaseStream = await releaseResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var releaseDocument = await JsonDocument.ParseAsync(releaseStream, cancellationToken: cancellationToken);

        var targetSuffix = $"-{fridaArchitecture}.so";
        var assets = releaseDocument.RootElement.GetProperty("assets").EnumerateArray();
        var assetUrl = default(string);
        var compressed = false;

        foreach (var asset in assets)
        {
            var name = asset.GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("frida-gadget-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.EndsWith(targetSuffix, StringComparison.OrdinalIgnoreCase))
            {
                assetUrl = asset.GetProperty("browser_download_url").GetString();
                compressed = false;
                break;
            }

            if (name.EndsWith(targetSuffix + ".xz", StringComparison.OrdinalIgnoreCase))
            {
                assetUrl = asset.GetProperty("browser_download_url").GetString();
                compressed = true;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(assetUrl))
        {
            throw new InvalidOperationException($"Could not find Frida gadget asset for '{fridaArchitecture}' in the latest Frida release.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        if (!compressed)
        {
            await DownloadAssetToFileAsync(assetUrl, cachePath, cancellationToken);
            return;
        }

        var compressedPath = cachePath + ".xz.download";
        await DownloadAssetToFileAsync(assetUrl, compressedPath, cancellationToken);

        try
        {
            await using var source = new FileStream(compressedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await using var xzStream = new XZStream(source);
            await using var target = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await xzStream.CopyToAsync(target, cancellationToken);
            await target.FlushAsync(cancellationToken);
        }
        finally
        {
            if (File.Exists(compressedPath))
            {
                File.Delete(compressedPath);
            }
        }
    }

    private async Task DownloadAssetToFileAsync(string downloadUrl, string destinationPath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var tempPath = destinationPath + ".download";
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await source.CopyToAsync(target, cancellationToken);
            await target.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, destinationPath, overwrite: true);
    }
}
