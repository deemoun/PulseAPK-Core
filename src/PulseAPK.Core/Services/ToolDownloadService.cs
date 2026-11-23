using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PulseAPK.Core.Abstractions;

namespace PulseAPK.Core.Services;

public sealed class ToolDownloadService : IToolDownloadService
{
    private static readonly SemaphoreSlim ApktoolGate = new(1);
    private static readonly SemaphoreSlim SignerGate = new(1);
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
            assetPredicate: static name => IsApktoolReleaseJarAssetName(name),
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
        var gate = checksumFileName == null ? ApktoolGate : SignerGate;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var artifactPath = _toolRepository.GetToolPath(artifactFileName);
            var receipt = checksumFileName == null ? null : _toolRepository.GetToolPath(checksumFileName + ".verified");
            if (_toolRepository.TryGetCachedToolPath(artifactFileName, out var cachedPath) &&
                (receipt == null || (File.Exists(receipt) &&
                    await File.ReadAllTextAsync(receipt, cancellationToken) == await ComputeSha256Async(cachedPath, cancellationToken))))
                return new ToolDownloadResult(cachedPath, Downloaded: false);

            var release = await GetLatestReleaseAsync(owner, repo, cancellationToken);
            var asset = release.Assets.FirstOrDefault(a => assetPredicate(a.Name))
                ?? throw new InvalidOperationException($"Could not find expected tool asset for {owner}/{repo} latest release.");
            var temporary = artifactPath + $".{Guid.NewGuid():N}.download";
            using var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            try
            {
                using (output)
                    await response.Content.CopyToAsync(output, cancellationToken);

                if (receipt != null)
                {
                    var checksum = release.Assets.FirstOrDefault(a => checksumAssetPredicate!(a.Name))
                        ?? throw new InvalidOperationException($"Could not find checksum asset for {owner}/{repo} latest release.");
                    var manifest = await _httpClient.GetStringAsync(checksum.BrowserDownloadUrl, cancellationToken);
                    var expected = checksumValueSelector!(manifest, asset.Name);
                    var actual = await ComputeSha256Async(temporary, cancellationToken);
                    if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("SHA256 verification failed for downloaded tool.");

                    var stagedReceipt = temporary + ".verified";
                    try
                    {
                        await File.WriteAllTextAsync(stagedReceipt, actual, cancellationToken);
                        File.Move(stagedReceipt, receipt, overwrite: true);
                    }
                    finally { File.Delete(stagedReceipt); }
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, artifactPath, overwrite: true);
                return new ToolDownloadResult(artifactPath, Downloaded: true);
            }
            finally { File.Delete(temporary); }
        }
        finally { gate.Release(); }
    }

    private async Task<GitHubReleaseResponse> GetLatestReleaseAsync(string owner, string repo, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"https://api.github.com/repos/{owner}/{repo}/releases/latest", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync(stream, ToolDownloadJsonContext.Default.GitHubReleaseResponse, cancellationToken);

        if (release is null)
        {
            throw new InvalidOperationException($"GitHub API returned an unexpected payload for {owner}/{repo} latest release.");
        }

        return release;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? ExtractSha256FromChecksumFile(string checksumContent, string assetName)
    {
        var lines = checksumContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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

    private static bool IsApktoolReleaseJarAssetName(string name)
    {
        if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var baseName = Path.GetFileNameWithoutExtension(name);
        return baseName.Equals("apktool", StringComparison.OrdinalIgnoreCase)
            || baseName.StartsWith("apktool_", StringComparison.OrdinalIgnoreCase)
            || baseName.StartsWith("apktool-", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class GitHubReleaseResponse
{
    [JsonPropertyName("assets")]
    public required List<GitHubReleaseAssetResponse> Assets { get; init; }
}

public sealed class GitHubReleaseAssetResponse
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("browser_download_url")]
    public required string BrowserDownloadUrl { get; init; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GitHubReleaseResponse))]
internal partial class ToolDownloadJsonContext : JsonSerializerContext;
