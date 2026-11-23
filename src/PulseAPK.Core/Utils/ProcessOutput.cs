using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PulseAPK.Core.Utils;

internal static class ProcessOutput
{
    public static async Task WaitAndDrainAsync(Process process, Action<string> output, Action<string> error, CancellationToken token)
    {
        var stdout = DrainLines(process.StandardOutput, output);
        var stderr = DrainLines(process.StandardError, error);
        try { await ProcessLifetime.WaitAsync(process, token); }
        finally { await Task.WhenAll(stdout, stderr); }
    }

    private static Task DrainLines(StreamReader reader, Action<string> onLine) =>
        Task.Factory.StartNew(() =>
        {
            using var macReader = OperatingSystem.IsMacOS() && reader.BaseStream is PipeStream pipe
                ? OpenMacPipeReader(pipe, reader.CurrentEncoding) : null;
            var source = macReader ?? reader;
            string? line;
            while ((line = source.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                onLine(line);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    // Both synchronous and asynchronous PipeStream reads can enter CreatePipeSocket on macOS.
    // A FileStream over an owned duplicate reads the descriptor directly without that adapter.
    private static StreamReader OpenMacPipeReader(PipeStream pipe, System.Text.Encoding encoding)
    {
        int descriptor = DuplicateDescriptor(pipe.SafePipeHandle);
        if (descriptor < 0) throw new IOException("Could not duplicate the process output descriptor.",
            new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        try { return new StreamReader(new FileStream(handle, FileAccess.Read), encoding); }
        catch { handle.Dispose(); throw; }
    }

    [DllImport("libc", EntryPoint = "dup", SetLastError = true)]
    private static extern int DuplicateDescriptor(SafePipeHandle handle);
}
