namespace Sextant.Git;

public sealed class GitOutput
{
    public required int ExitCode { get; init; }

    public required byte[] Stdout { get; init; }

    public required string StandardError { get; init; }

    public required string? Progress { get; init; }

    public required TimeSpan Duration { get; init; }

    public required IReadOnlyList<string> DisplayArguments { get; init; }
}

public sealed class GitRequest
{
    public required string Executable { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public IProgress<string>? Progress { get; init; }
}

public sealed class GitCommandFailedException : Exception
{
    public GitCommandFailedException(GitOutput output)
        : base(Describe(output))
    {
        ExitCode = output.ExitCode;
        StandardError = output.StandardError;
        Arguments = output.DisplayArguments;
        IsDubiousOwnership = StandardError.Contains("dubious ownership", StringComparison.OrdinalIgnoreCase)
            || StandardError.Contains("safe.directory", StringComparison.OrdinalIgnoreCase);
    }

    public int ExitCode { get; }

    public string StandardError { get; }

    public IReadOnlyList<string> Arguments { get; }

    public bool IsDubiousOwnership { get; }

    private static string Describe(GitOutput output)
    {
        var detail = string.IsNullOrWhiteSpace(output.StandardError)
            ? "Git failed."
            : output.StandardError.Trim();
        return $"git exited {output.ExitCode}{Environment.NewLine}{detail}";
    }
}
