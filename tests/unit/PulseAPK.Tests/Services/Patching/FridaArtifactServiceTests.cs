using System.Net;
using System.Net.Http;
using System.Text;
using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Models;
using PulseAPK.Core.Services.Patching;

namespace PulseAPK.Tests.Services.Patching;

public class FridaArtifactServiceTests
{
    [Fact]
    public async Task ResolveGadgetAsync_DownloadsAndCachesGadget_WhenMissingFromCache()
    {
        var root = Path.Combine(Path.GetTempPath(), $"frida-artifact-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            const string releaseUrl = "https://api.github.com/repos/frida/frida/releases/latest";
            const string downloadUrl = "https://example.test/frida-gadget-1.0.0-android-arm64.so";
            var payload = Encoding.UTF8.GetBytes("fake-gadget");

            using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
            {
                if (request.RequestUri?.AbsoluteUri == releaseUrl)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""
                            {
                              "assets": [
                                {
                                  "name": "frida-gadget-1.0.0-android-arm64.so",
                                  "browser_download_url": "https://example.test/frida-gadget-1.0.0-android-arm64.so"
                                }
                              ]
                            }
                            """, Encoding.UTF8, "application/json")
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

            var toolRepository = new TestToolRepository(root);
            var service = new FridaArtifactService(httpClient, toolRepository);

            var result = await service.ResolveGadgetAsync(new PatchRequest(), "arm64-v8a");

            Assert.Null(result.Error);
            Assert.NotNull(result.GadgetPath);
            Assert.True(File.Exists(result.GadgetPath));
            Assert.Equal(payload, await File.ReadAllBytesAsync(result.GadgetPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ResolveGadgetAsync_ReturnsCacheMissError_WhenAbiIsUnknown()
    {
        var root = Path.Combine(Path.GetTempPath(), $"frida-artifact-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
            var toolRepository = new TestToolRepository(root);
            var service = new FridaArtifactService(httpClient, toolRepository);

            var result = await service.ResolveGadgetAsync(new PatchRequest(), "mips");

            Assert.Null(result.GadgetPath);
            Assert.NotNull(result.Error);
            Assert.Contains("was not found in tool cache", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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
