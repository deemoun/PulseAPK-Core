using PulseAPK.Core.Abstractions.Patching;

namespace PulseAPK.Core.Services.Patching;

public sealed class SigningService : ISigningService
{
    private readonly UbersignRunner _ubersignRunner;

    public SigningService(UbersignRunner ubersignRunner)
    {
        _ubersignRunner = ubersignRunner;
    }

    public async Task<(bool Success, string? SignedApkPath, string? Error)> SignAsync(string inputApkPath, string outputApkPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var code = await _ubersignRunner.RunSigningAsync(inputApkPath, outputApkPath, cancellationToken);
            return code == 0
                ? (true, outputApkPath, null)
                : (false, null, $"Signing failed with exit code {code}.");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }
}
