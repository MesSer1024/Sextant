namespace Sextant.Git;

public static class RepositoryAdmin
{
    public static async Task InitAsync(GitProcessRunner runner, string executable, string path, CancellationToken cancellationToken)
    {
        var output = await runner.RunAsync(new GitRequest
        {
            Executable = executable,
            Arguments = GitCommands.Init(path),
            WorkingDirectory = Path.GetDirectoryName(path),
        }, cancellationToken).ConfigureAwait(false);
        if (output.ExitCode != 0)
            throw new GitCommandFailedException(output);
    }

    public static async Task CloneAsync(
        GitProcessRunner runner,
        string executable,
        string url,
        string destination,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var output = await runner.RunAsync(new GitRequest
        {
            Executable = executable,
            Arguments = GitCommands.Clone(url, destination),
            WorkingDirectory = Path.GetDirectoryName(destination),
            Progress = progress,
        }, cancellationToken).ConfigureAwait(false);
        if (output.ExitCode != 0)
            throw new GitCommandFailedException(output);
    }

    public static async Task AddSafeDirectoryAsync(
        GitProcessRunner runner,
        string executable,
        string path,
        CancellationToken cancellationToken)
    {
        var output = await runner.RunAsync(new GitRequest
        {
            Executable = executable,
            Arguments = GitCommands.AddSafeDirectory(RepoPath.Normalize(path)),
        }, cancellationToken).ConfigureAwait(false);
        if (output.ExitCode != 0)
            throw new GitCommandFailedException(output);
    }
}
