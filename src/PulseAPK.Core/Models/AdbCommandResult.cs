namespace PulseAPK.Core.Models;

public sealed record AdbCommandResult(
    string Executable,
    IReadOnlyList<string> Arguments,
    string StandardOutput,
    string StandardError,
    int ExitCode,
    bool TimedOut)
{
    public string CommandText => string.Join(" ", new[] { Executable }.Concat(Arguments).Select(Quote));

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return value.Any(char.IsWhiteSpace)
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }
}
