namespace PulseAPK.Core.Models;

public enum SigningOutcome { NotStarted, NotRequested, Signed, Failed }
public enum VerificationOutcome { NotRun, Skipped, Passed, Failed, Inconclusive }

public sealed class PatchResult
{
    public SigningOutcome Signing { get; set; }
    public VerificationOutcome Verification { get; set; }
    public bool Success { get; set; }
    public string? OutputApkPath { get; set; }
    public string? SelectedArchitecture { get; set; }
    public string? PatchedActivity { get; set; }
    public bool UsedSigning { get; set; }
    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();
    public List<PatchStageSummary> StageSummaries { get; } = new();

    public static PatchResult Failure(params string[] errors)
    {
        var result = new PatchResult { Success = false };
        result.Errors.AddRange(errors.Where(error => !string.IsNullOrWhiteSpace(error)));
        return result;
    }
}
