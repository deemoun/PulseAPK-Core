using PulseAPK.Core.Models;

namespace PulseAPK.Core.Abstractions.Patching;

public interface IApktoolService
{
    Task<ApktoolRunResult> DecompileAsync(string apkPath, string outputDirectory, bool decodeResources, bool decodeSources, CancellationToken cancellationToken = default);
    Task<ApktoolRunResult> BuildAsync(string decompiledDirectory, string outputApkPath, bool useAapt2, CancellationToken cancellationToken = default);
}
