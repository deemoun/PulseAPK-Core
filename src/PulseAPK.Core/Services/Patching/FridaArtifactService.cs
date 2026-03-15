using PulseAPK.Core.Abstractions;
using PulseAPK.Core.Abstractions.Patching;
using PulseAPK.Core.Models;

namespace PulseAPK.Core.Services.Patching;

public sealed class FridaArtifactService : IFridaArtifactService
{
    private readonly IToolRepository _toolRepository;

    public FridaArtifactService(IToolRepository toolRepository)
    {
        _toolRepository = toolRepository;
    }

    public Task<(string? GadgetPath, string? Error)> ResolveGadgetAsync(PatchRequest request, string architecture, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.CustomGadgetBinaryPath))
        {
            if (File.Exists(request.CustomGadgetBinaryPath))
            {
                return Task.FromResult<(string?, string?)>((request.CustomGadgetBinaryPath, null));
            }

            return Task.FromResult<(string?, string?)>((null, $"Custom gadget binary '{request.CustomGadgetBinaryPath}' was not found."));
        }

        var cachePath = _toolRepository.GetToolPath(Path.Combine("frida", architecture, "libfrida-gadget.so"));
        if (File.Exists(cachePath))
        {
            return Task.FromResult<(string?, string?)>((cachePath, null));
        }

        return Task.FromResult<(string?, string?)>((null, $"Frida gadget for ABI '{architecture}' was not found in tool cache: '{cachePath}'."));
    }
}
