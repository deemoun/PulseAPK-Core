namespace PulseAPK.Tests;

public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Tests POSIX permissions or a Unix shell executable.";
    }
}
