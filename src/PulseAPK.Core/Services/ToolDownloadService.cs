using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PulseAPK.Core.Abstractions;

namespace PulseAPK.Core.Services;

public sealed class ToolDownloadService : IToolDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly IToolRepository _toolRepository;

    public ToolDownloadService(HttpClient httpClient, IToolRepository toolRepository)
    {
        _httpClient = httpClient;
        _toolRepository = toolRepository;

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PulseAPK", "1.0"));
        }
    }

    public Task<ToolDownloadResult> DownloadApktoolAsync(CancellationToken cancellationToken = default)
    {
        return DownloadFromLatestReleaseAsync(
            owner: "iBotPeaches",
            repo: "Apktool",
            artifactFileName: "apktool.jar",
            assetPredicate: static name => name.Equals("apktool.jar", StringComparison.OrdinalIgnoreCase),
            checksumFileName: null,
            checksumAssetPredicate: null,
            checksumValueSelector: null,
            cancellationToken);
    }

    public Task<ToolDownloadResult> DownloadUbersignerAsync(CancellationToken cancellationToken = default)
    {
        return DownloadFromLatestReleaseAsync(
            owner: "patrickfav",
            repo: "uber-apk-signer",
            artifactFileName: "ubersigner.jar",
            assetPredicate: static name => name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                                         && name.Contains("uber-apk-signer", StringComparison.OrdinalIgnoreCase),
            checksumFileName: "ubersigner.sha256",
            checksumAssetPredicate: static name => name.Contains("sha256", StringComparison.OrdinalIgnoreCase)
                                                   || name.Contains("checksum", StringComparison.OrdinalIgnoreCase),
            checksumValueSelector: static (checksumContent, assetName) => ExtractSha256FromChecksumFile(checksumContent, assetName),
            cancellationToken);
    }

    private async Task<ToolDownloadResult> DownloadFromLatestReleaseAsync(
        string owner,
        string repo,
        string artifactFileName,
        Func<string, bool> assetPredicate,
        string? checksumFileName,
        Func<string, bool>? checksumAssetPredicate,
        Func<string, string, string?>? checksumValueSelector,
        CancellationToken cancellationToken)
    {
        if (_toolRepository.TryGetCachedToolPath(artifactFileName, out var cachedPath))
        {
            return new ToolDownloadResult(cachedPath, Downloaded: false);
        }

        var release = await GetLatestReleaseAsync(owner, repo, cancellationToken);

        var artifactAsset = release.Assets.FirstOrDefault(a => assetPredicate(a.Name))
            ?? throw new InvalidOperationException($"Could not find expected tool asset for {owner}/{repo} latest release.");

        var artifactPath = _toolRepository.GetToolPath(artifactFileName);
        await DownloadAssetToFileAsync(artifactAsset.BrowserDownloadUrl, artifactPath, cancellationToken);

        if (checksumFileName is null || checksumAssetPredicate is null || checksumValueSelector is null)
        {
            return new ToolDownloadResult(artifactPath, Downloaded: true);
        }

        var checksumAsset = release.Assets.FirstOrDefault(a => checksumAssetPredicate(a.Name))
            ?? throw new InvalidOperationException($"Could not find checksum asset for {owner}/{repo} latest release.");

        var checksumPath = _toolRepository.GetToolPath(checksumFileName);
        await DownloadAssetToFileAsync(checksumAsset.BrowserDownloadUrl, checksumPath, cancellationToken);

        var checksumContent = await File.ReadAllTextAsync(checksumPath, cancellationToken);
        var expectedSha256 = checksumValueSelector(checksumContent, artifactAsset.Name);

        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            File.Delete(artifactPath);
            throw new InvalidOperationException("Could not parse SHA256 checksum value for downloaded tool.");
        }

        var actualSha256 = await ComputeSha256Async(artifactPath, cancellationToken);
        if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(artifactPath);
            throw new InvalidOperationException("SHA256 verification failed for downloaded tool.");
        }

        return new ToolDownloadResult(artifactPath, Downloaded: true);
    }

    private async Task<GitHubReleaseResponse> GetLatestReleaseAsync(string owner, string repo, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"https://api.github.com/repos/{owner}/{repo}/releases/latest", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync(stream, ToolDownloadJsonContext.Default.GitHubReleaseResponse, cancellationToken);

        if (release is null)
        {
            throw new InvalidOperationException($"GitHub API returned an unexpected payload for {owner}/{repo} latest release.");
        }

        return release;
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

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? ExtractSha256FromChecksumFile(string checksumContent, string assetName)
    {
        var lines = checksumContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            if (parts.Length == 1)
            {
                return NormalizeSha(parts[0]);
            }

            var fileName = parts[^1].TrimStart('*');
            if (fileName.Equals(assetName, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeSha(parts[0]);
            }
        }

        return null;
    }

    private static string? NormalizeSha(string candidate)
    {
        var value = candidate.Trim().ToLowerInvariant();
        if (value.Length != 64)
        {
            return null;
        }

        foreach (var ch in value)
        {
            if (!Uri.IsHexDigit(ch))
            {
                return null;
            }
        }

        return value;
    }
}

public sealed class GitHubReleaseResponse
{
    public required List<GitHubReleaseAssetResponse> Assets { get; init; }
}

public sealed class GitHubReleaseAssetResponse
{
    public required string Name { get; init; }
    public required string BrowserDownloadUrl { get; init; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GitHubReleaseResponse))]
internal partial class ToolDownloadJsonContext : JsonSerializerContext;
