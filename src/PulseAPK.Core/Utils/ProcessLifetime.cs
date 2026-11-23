using System.Diagnostics;

namespace PulseAPK.Core.Utils;

internal static class ProcessLifetime
{
    public static async Task WaitAsync(Process process, CancellationToken token)
    {
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync();
            throw;
        }
    }
}
