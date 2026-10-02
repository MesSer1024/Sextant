using System.ComponentModel;
using System.Text;

namespace Sextant.Git;

internal readonly record struct GitHubAccount(string Host, string User, bool Active);

internal interface IGitHubLogin
{
    Task<IReadOnlyList<GitHubAccount>> AccountsAsync(CancellationToken cancellationToken);

    Task<string?> TokenAsync(string user, CancellationToken cancellationToken);
}

internal static class GitHubAccounts
{
    public static IReadOnlyList<GitHubAccount> ParseStatus(string text)
    {
        var accounts = new List<GitHubAccount>();
        string? host = null;
        string? user = null;
        var active = false;
        var pending = false;
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            const string marker = "Logged in to ";
            var index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
            {
                Add();
                var parts = line[(index + marker.Length)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && string.Equals(parts[1], "account", StringComparison.Ordinal))
                {
                    host = parts[0];
                    user = parts[2];
                    active = false;
                    pending = true;
                }

                continue;
            }

            if (pending && line.Contains("Active account:", StringComparison.OrdinalIgnoreCase))
                active = line.Contains("true", StringComparison.OrdinalIgnoreCase);
        }

        Add();
        return accounts;

        void Add()
        {
            if (pending && host is not null && user is not null)
                accounts.Add(new GitHubAccount(host, user, active));
            pending = false;
            active = false;
        }
    }

    public static bool IsAccessFailure(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return false;
        if (stderr.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            return false;
        return stderr.Contains("Not Found", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("Authentication", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("Authorization", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("Forbidden", StringComparison.OrdinalIgnoreCase);
    }

    public static string AccessHint(string stderr)
    {
        if (IsAccessFailure(stderr))
            return "The signed-in GitHub account cannot see this repository.";
        if (stderr.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            return "The LFS object is not on the server.";
        return "";
    }

    private sealed class GhLogin : IGitHubLogin
    {
        private readonly GitProcessRunner _runner;

        public GhLogin(GitProcessRunner runner) => _runner = runner;

        public async Task<IReadOnlyList<GitHubAccount>> AccountsAsync(CancellationToken cancellationToken)
        {
            var output = await RunAsync(["auth", "status"], cancellationToken).ConfigureAwait(false);
            if (output is null)
                return [];
            var text = output.StandardError;
            var stdout = Encoding.UTF8.GetString(output.Stdout);
            if (!string.IsNullOrWhiteSpace(stdout))
                text = string.IsNullOrWhiteSpace(text) ? stdout : stdout + "\n" + text;
            return ParseStatus(text);
        }

        public async Task<string?> TokenAsync(string user, CancellationToken cancellationToken)
        {
            var output = await RunAsync(["auth", "token", "-u", user], cancellationToken).ConfigureAwait(false);
            if (output is null || output.ExitCode != 0)
                return null;
            var token = Encoding.UTF8.GetString(output.Stdout).Trim();
            return token.Length == 0 ? null : token;
        }

        private async Task<GitOutput?> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            try
            {
                return await _runner.RunAsync(new GitRequest
                {
                    Executable = "gh",
                    Arguments = arguments,
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Win32Exception)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    public static IGitHubLogin Login(GitProcessRunner runner) => new GhLogin(runner);
}
