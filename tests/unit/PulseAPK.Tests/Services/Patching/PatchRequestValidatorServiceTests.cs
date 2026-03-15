using PulseAPK.Core.Models;
using PulseAPK.Core.Services.Patching;

namespace PulseAPK.Tests.Services.Patching;

public class PatchRequestValidatorServiceTests
{
    [Fact]
    public void Validate_ReturnsErrors_WhenRequiredInputsMissing()
    {
        var service = new PatchRequestValidatorService();
        var request = new PatchRequest();

        var errors = service.Validate(request);

        Assert.Contains(errors, error => error.Contains("Input APK", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("Output APK", StringComparison.OrdinalIgnoreCase));
    }
}
