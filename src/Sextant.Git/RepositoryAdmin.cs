using System.Text;

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

    public static async Task<RepoBadge> ReadBadgeAsync(
        GitProcessRunner runner,
        string executable,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var top = await runner.RunAsync(new GitRequest
            {
                Executable = executable,
                Arguments = GitCommands.TopLevel(path),
                WorkingDirectory = path,
            }, cancellationToken).ConfigureAwait(false);
            if (top.ExitCode != 0)
                return RepoBadge.Failed(string.IsNullOrWhiteSpace(top.StandardError) ? "Not a git repository." : top.StandardError.Trim());

            var toplevel = RepoPath.Normalize(Encoding.UTF8.GetString(top.Stdout).Trim());
            var status = await runner.RunAsync(new GitRequest
            {
                Executable = executable,
                Arguments = GitCommands.Status(toplevel),
                WorkingDirectory = toplevel,
            }, cancellationToken).ConfigureAwait(false);
            if (status.ExitCode != 0)
                return RepoBadge.Failed(status.StandardError.Trim());

            var snapshot = Parsing.StatusParser.Parse(status.Stdout);
            var conflicted = snapshot.Entries.Any(entry => entry.Kind == ChangeKind.Unmerged);
            return new RepoBadge(
                true,
                snapshot.Branch.Detached ? Short(snapshot.Branch.Oid) : snapshot.Branch.HeadName,
                snapshot.Branch.Detached,
                snapshot.Entries.Count > 0,
                conflicted,
                snapshot.Branch.Ahead,
                snapshot.Branch.Behind,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return RepoBadge.Failed(exception.Message);
        }
    }

    private static string? Short(string? oid) =>
        string.IsNullOrEmpty(oid) ? oid : oid.Length <= 7 ? oid : oid[..7];
}
