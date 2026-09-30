namespace Sextant.Git;

public static class GitLocator
{
    public static string? FindOnPath(string? pathOverride = null)
    {
        var path = pathOverride ?? Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var fileName = OperatingSystem.IsWindows() ? "git.exe" : "git";
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(entry.Trim(), fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static async Task<GitVersion> ProbeAsync(GitProcessRunner runner, string executable, CancellationToken cancellationToken)
    {
        var output = await runner.RunAsync(new GitRequest
        {
            Executable = executable,
            Arguments = GitCommands.Version(),
        }, cancellationToken).ConfigureAwait(false);
        if (output.ExitCode != 0)
            throw new GitCommandFailedException(output);
        return GitVersions.Parse(System.Text.Encoding.UTF8.GetString(output.Stdout));
    }
}
