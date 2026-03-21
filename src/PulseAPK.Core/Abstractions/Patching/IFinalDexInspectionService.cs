namespace PulseAPK.Core.Abstractions.Patching;

public interface IFinalDexInspectionService
{
    Task<bool> ContainsMethodReferenceAsync(string apkPath, string methodReference, CancellationToken cancellationToken = default);
}
