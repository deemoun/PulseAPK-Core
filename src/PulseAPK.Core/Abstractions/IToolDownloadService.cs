namespace PulseAPK.Core.Abstractions;

public interface IToolDownloadService
{
    Task<ToolDownloadResult> DownloadApktoolAsync(CancellationToken cancellationToken = default);
    Task<ToolDownloadResult> DownloadUbersignerAsync(CancellationToken cancellationToken = default);
}

public sealed record ToolDownloadResult(string Path, bool Downloaded);
