namespace PulseAPK.Core.Abstractions.Patching;

public interface IFinalDexInspectionService
{
    Task<(bool Found, string Diagnostics)> ContainsMethodReferenceAsync(string apkPath, string methodReference, CancellationToken cancellationToken = default);

    Task<(bool Found, string Diagnostics)> ContainsStringMarkerAsync(string apkPath, string markerLiteral, CancellationToken cancellationToken = default);
}
