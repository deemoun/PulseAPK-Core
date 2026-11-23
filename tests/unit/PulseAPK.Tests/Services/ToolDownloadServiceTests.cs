using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Services;
using Xunit;

namespace PulseAPK.Tests.Services;

public class ToolDownloadServiceTests
{
    [Fact]
    public async Task DownloadApktoolAsync_DownloadsVersionedJarAssetName()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"pulseapk-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            const string versionedAssetName = "apktool_2.11.1.jar";
            const string downloadUrl = "https://example.test/assets/apktool_2.11.1.jar";
            var payload = Encoding.UTF8.GetBytes("fake-apktool-binary");

            using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
            {
                if (request.RequestUri?.AbsoluteUri == "https://api.github.com/repos/iBotPeaches/Apktool/releases/latest")
                {
                    var release = new
                    {
                        assets = new[]
                        {
                            new { name = "notes.txt", browser_download_url = "https://example.test/assets/notes.txt" },
                            new { name = versionedAssetName, browser_download_url = downloadUrl }
                        }
                    };

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(release), Encoding.UTF8, "application/json")
                    };
                }

                if (request.RequestUri?.AbsoluteUri == downloadUrl)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(payload)
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }));

            var toolRepository = new TestToolRepository(tempRoot);
            var service = new ToolDownloadService(httpClient, toolRepository);

            var result = await service.DownloadApktoolAsync();

            Assert.True(result.Downloaded);
            Assert.Equal(toolRepository.GetToolPath("apktool.jar"), result.Path);
            Assert.True(File.Exists(result.Path));
            Assert.Equal(payload, await File.ReadAllBytesAsync(result.Path));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("mismatch")]
    [InlineData("cancel")]
    public async Task SignerIsCachedOnlyAfterVerification(string failure)
    {
        var root = Directory.CreateTempSubdirectory("pulse-tools-");
        using var cancel = new CancellationTokenSource();
        try
        {
            const string name = "uber-apk-signer-1.jar";
            byte[] payload = [17, 42];
            string checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));
            string release = JsonSerializer.Serialize(new { assets = new[]
            {
                new { name, browser_download_url = "https://fixture.test/file" },
                new { name = "sha256.txt", browser_download_url = "https://fixture.test/check" }
            } });
            int copies = 0;
            string mode = failure;
            using var client = new HttpClient(new StubHttpMessageHandler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/latest")) return new(HttpStatusCode.OK) { Content = new StringContent(release) };
                if (path == "/file")
                {
                    Interlocked.Increment(ref copies);
                    return new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
                }
                if (mode == "missing") return new(HttpStatusCode.NotFound);
                if (mode == "cancel") cancel.Cancel();
                return new(HttpStatusCode.OK) { Content = new StringContent(mode == "mismatch" ? new string('0', 64) : checksum) };
            }));
            var service = new ToolDownloadService(client, new TestToolRepository(root.FullName));
            var attempt = service.DownloadUbersignerAsync(cancel.Token);
            if (mode == "missing") await Assert.ThrowsAsync<HttpRequestException>(() => attempt);
            else if (mode == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
            else await Assert.ThrowsAsync<InvalidOperationException>(() => attempt);
            Assert.Empty(root.GetFiles());
            mode = "ok";
            // Simulate a stale, pre-verification cache entry.
            File.WriteAllText(Path.Combine(root.FullName, "ubersigner.jar"), "old");
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => service.DownloadUbersignerAsync())));
            Assert.Single(results, result => result.Downloaded);
            Assert.Equal(2, copies);
            Assert.Equal(payload, File.ReadAllBytes(results[0].Path));
        }
        finally { root.Delete(true); }
    }

    private sealed class TestToolRepository : IToolRepository
    {
        public TestToolRepository(string toolsDirectory)
        {
            ToolsDirectory = toolsDirectory;
        }

        public string ToolsDirectory { get; }

        public string GetToolPath(string fileName)
        {
            return Path.Combine(ToolsDirectory, fileName);
        }

        public bool TryGetCachedToolPath(string fileName, out string path)
        {
            path = GetToolPath(fileName);
            return File.Exists(path);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
