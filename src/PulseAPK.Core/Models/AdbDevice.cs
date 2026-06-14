namespace PulseAPK.Core.Models;

public sealed record AdbDevice(string Serial, string State, string Model)
{
    public string DisplayName
    {
        get
        {
            var modelText = string.IsNullOrWhiteSpace(Model) ? string.Empty : $" - {Model}";
            return $"{Serial} ({State}){modelText}";
        }
    }

    public bool IsUsable => string.Equals(State, "device", StringComparison.OrdinalIgnoreCase);
}
