namespace PulseAPK.Core.Abstractions.Patching;

public interface IApktoolService
{
    Task<int> DecompileAsync(string apkPath, string outputDirectory, bool decodeResources, bool decodeSources, CancellationToken cancellationToken = default);
    Task<int> BuildAsync(string decompiledDirectory, string outputApkPath, bool useAapt2, CancellationToken cancellationToken = default);
}
