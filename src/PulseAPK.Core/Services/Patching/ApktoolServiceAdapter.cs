using PulseAPK.Core.Abstractions.Patching;

namespace PulseAPK.Core.Services.Patching;

public sealed class ApktoolServiceAdapter : IApktoolService
{
    private readonly ApktoolRunner _runner;

    public ApktoolServiceAdapter(ApktoolRunner runner)
    {
        _runner = runner;
    }

    public Task<int> DecompileAsync(string apkPath, string outputDirectory, bool decodeResources, bool decodeSources, CancellationToken cancellationToken = default)
        => _runner.RunDecompileAsync(apkPath, outputDirectory, decodeResources, decodeSources, keepOriginalManifest: false, forceOverwrite: true, cancellationToken);

    public Task<int> BuildAsync(string decompiledDirectory, string outputApkPath, bool useAapt2, CancellationToken cancellationToken = default)
        => _runner.RunBuildAsync(decompiledDirectory, outputApkPath, useAapt2, cancellationToken);
}
