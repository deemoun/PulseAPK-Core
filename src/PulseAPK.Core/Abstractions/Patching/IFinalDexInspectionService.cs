namespace PulseAPK.Core.Abstractions.Patching;

public interface IFinalDexInspectionService
{
    Task<(bool Found, string Diagnostics)> ContainsMethodReferenceAsync(string apkPath, string methodReference, CancellationToken cancellationToken = default);
}
