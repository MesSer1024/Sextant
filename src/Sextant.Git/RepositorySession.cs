using System.Text;
using Sextant.Git.Parsing;

namespace Sextant.Git;

public sealed class RepositorySession : IAsyncDisposable
{
    private readonly GitProcessRunner _runner;
    private readonly string _executable;
    private readonly RepositoryScheduler _scheduler = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _stateLock = new();
    private readonly LaneAssigner _lanes = new();
    private readonly RequestGate _diffGate = new();
    private readonly RequestGate _filesGate = new();
    private readonly List<CommandLogEntry> _commands = [];
    private string _toplevel = "";
    private string _gitDirectory = "";
    private Encoding _encoding = Encoding.UTF8;
    private BranchHeader _branch = new(null, null, false, true, null, 0, 0);
    private List<StatusEntry> _entries = [];
    private List<GitRef> _refs = [];
    private List<GraphCommit> _commits = [];
    private List<string> _remotes = [];
    private Dictionary<string, string> _config = new(StringComparer.OrdinalIgnoreCase);
    private bool _historyEnded;
    private bool _historyCapped;
    private int _historyGeneration = 1;
    private bool _merge;
    private string? _mergeMessage;
    private TimeSpan _statusDuration;
    private PerformanceSuggestion? _suggestion;
    private string _tipSignature = "";

    private RepositorySession(GitProcessRunner runner, string executable)
    {
        _runner = runner;
        _executable = executable;
    }

    public string Toplevel => _toplevel;

    public string GitDirectory => _gitDirectory;

    public string DisplayName
    {
        get
        {
            var name = Path.GetFileName(_toplevel);
            return string.IsNullOrEmpty(name) ? _toplevel : name;
        }
    }

    public static async Task<RepositorySession> OpenAsync(
        GitProcessRunner runner,
        string executable,
        string path,
        CancellationToken cancellationToken)
    {
        var session = new RepositorySession(runner, executable);
        await session.OpenCoreAsync(path, cancellationToken).ConfigureAwait(false);
        return session;
    }

    public SessionState Snapshot()
    {
        lock (_stateLock)
        {
            return new SessionState
            {
                Branch = _branch,
                Entries = _entries.ToArray(),
                Refs = _refs.ToArray(),
                Commits = _commits.ToArray(),
                HistoryEnded = _historyEnded,
                HistoryCapped = _historyCapped,
                HistoryGeneration = _historyGeneration,
                MergeInProgress = _merge,
                MergeMessage = _mergeMessage,
                LastStatusDuration = _statusDuration,
                Config = new Dictionary<string, string>(_config, StringComparer.OrdinalIgnoreCase),
                Remotes = _remotes.ToArray(),
                Commands = _commands.ToArray(),
                Suggestion = _suggestion,
            };
        }
    }

    public Task RefreshStatusAsync(CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            string? previous;
            lock (_stateLock)
                previous = _branch.Oid;
            var status = await QueryStatusAsync(ct).ConfigureAwait(false);
            var headChanged = false;
            lock (_stateLock)
            {
                headChanged = previous != status.Snapshot.Branch.Oid;
                ApplyStatus(status);
            }

            if (headChanged)
                await LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: true).ConfigureAwait(false);
        }, cancellationToken);

    public Task RefreshRefsAndStatusAsync(CancellationToken cancellationToken) =>
        RunAsync(ct => LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: false), cancellationToken);

    public Task ReloadHistoryAsync(CancellationToken cancellationToken) =>
        RunAsync(ReloadHistoryCoreAsync, cancellationToken);

    public Task<bool> LoadMoreHistoryAsync(bool pastCap, CancellationToken cancellationToken) =>
        RunAsync(ct => LoadMoreCoreAsync(pastCap, ct), cancellationToken);

    public Task<IReadOnlyList<CommitFileChange>?> CommitFilesAsync(string sha, CancellationToken cancellationToken)
    {
        var token = _filesGate.Next();
        return RunAsync(async ct =>
        {
            var changes = await _scheduler.ReadAsync(async inner =>
            {
                var output = await ExecuteAsync(GitCommands.NameStatus(_toplevel, sha), null, inner).ConfigureAwait(false);
                Checked(output);
                return NameStatusParser.Parse(output.Stdout);
            }, ct).ConfigureAwait(false);
            return _filesGate.IsCurrent(token) ? changes : null;
        }, cancellationToken);
    }

    public Task<DiffDocument?> WorkingDiffAsync(string path, bool staged, bool untracked, bool allowLarge, CancellationToken cancellationToken)
    {
        var token = _diffGate.Next();
        return RunAsync(async ct =>
        {
            var document = await _scheduler.ReadAsync(async inner =>
            {
                if (untracked && !staged)
                    return await DiffUntrackedAsync(path, allowLarge, inner).ConfigureAwait(false);
                var arguments = staged
                    ? GitCommands.DiffStaged(_toplevel, path)
                    : GitCommands.DiffUnstaged(_toplevel, path);
                var output = await ExecuteAsync(arguments, null, inner).ConfigureAwait(false);
                Checked(output);
                return ToDiff(output, allowLarge);
            }, ct).ConfigureAwait(false);
            return _diffGate.IsCurrent(token) ? document : null;
        }, cancellationToken);
    }

    public Task<DiffDocument?> CommitDiffAsync(
        string sha,
        string? firstParent,
        string path,
        bool allowLarge,
        CancellationToken cancellationToken)
    {
        var token = _diffGate.Next();
        return RunAsync(async ct =>
        {
            var document = await _scheduler.ReadAsync(async inner =>
            {
                var arguments = string.IsNullOrEmpty(firstParent)
                    ? GitCommands.ShowPatch(_toplevel, sha, path)
                    : GitCommands.DiffRange(_toplevel, firstParent, sha, path);
                var output = await ExecuteAsync(arguments, null, inner).ConfigureAwait(false);
                Checked(output);
                return ToDiff(output, allowLarge);
            }, ct).ConfigureAwait(false);
            return _diffGate.IsCurrent(token) ? document : null;
        }, cancellationToken);
    }

    public Task StageFileAsync(string path, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.Stage(_toplevel, path), null, cancellationToken);

    public Task UnstageFileAsync(string path, CancellationToken cancellationToken)
    {
        var unborn = IsUnborn();
        var command = unborn ? GitCommands.UnstageUnborn(_toplevel, path) : GitCommands.Unstage(_toplevel, path);
        var fallback = unborn ? null : GitCommands.UnstageUnborn(_toplevel, path);
        return MutateAsync(command, null, cancellationToken, fallback);
    }

    public Task DiscardTrackedAsync(string path, CancellationToken cancellationToken)
    {
        var unborn = IsUnborn();
        var command = unborn ? GitCommands.DiscardUnborn(_toplevel, path) : GitCommands.DiscardTracked(_toplevel, path);
        var fallback = unborn ? null : GitCommands.DiscardUnborn(_toplevel, path);
        return MutateAsync(command, null, cancellationToken, fallback);
    }

    public Task DiscardUntrackedAsync(string path, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.DiscardUntracked(_toplevel, path), null, cancellationToken);

    public Task ApplyHunkAsync(string rawPatch, int hunkIndex, bool reverse, CancellationToken cancellationToken)
    {
        var patch = HunkPatch.Slice(rawPatch, hunkIndex);
        return RunAsync(async ct =>
        {
            await _scheduler.WriteAsync(async token =>
            {
                var file = Path.Combine(Path.GetTempPath(), "sextant-hunk-" + Guid.NewGuid().ToString("N") + ".patch");
                try
                {
                    await File.WriteAllTextAsync(file, patch, new UTF8Encoding(false), token).ConfigureAwait(false);
                    var arguments = reverse
                        ? GitCommands.ApplyCachedReverse(_toplevel, file)
                        : GitCommands.ApplyCached(_toplevel, file);
                    Checked(await ExecuteAsync(arguments, null, token).ConfigureAwait(false));
                    return 0;
                }
                finally
                {
                    TryDelete(file);
                }
            }, ct).ConfigureAwait(false);
            await LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: false).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task CommitAsync(string message, CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            await _scheduler.WriteAsync(async token =>
            {
                var file = Path.Combine(Path.GetTempPath(), "sextant-msg-" + Guid.NewGuid().ToString("N"));
                try
                {
                    await File.WriteAllTextAsync(file, message, new UTF8Encoding(false), token).ConfigureAwait(false);
                    Checked(await ExecuteAsync(GitCommands.Commit(_toplevel, file), null, token).ConfigureAwait(false));
                    return 0;
                }
                finally
                {
                    TryDelete(file);
                }
            }, ct).ConfigureAwait(false);
            await LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: false).ConfigureAwait(false);
        }, cancellationToken);

    public Task SwitchAsync(string branch, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.Switch(_toplevel, branch), null, cancellationToken);

    public Task SwitchTrackAsync(string remoteBranch, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.SwitchTrack(_toplevel, remoteBranch), null, cancellationToken);

    public Task CreateBranchAsync(string name, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.CreateBranch(_toplevel, name), null, cancellationToken);

    public Task DeleteBranchAsync(string name, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.DeleteBranch(_toplevel, name), null, cancellationToken);

    public Task SetUpstreamAsync(string branch, string upstream, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.SetUpstream(_toplevel, branch, upstream), null, cancellationToken);

    public Task MergeAsync(string branch, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.Merge(_toplevel, branch), null, cancellationToken);

    public Task AbortMergeAsync(CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.AbortMerge(_toplevel), null, cancellationToken);

    public Task FetchAsync(IProgress<string>? progress, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.Fetch(_toplevel), progress, cancellationToken);

    public Task PullAsync(IProgress<string>? progress, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.Pull(_toplevel), progress, cancellationToken);

    public Task PushAsync(IProgress<string>? progress, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.Push(_toplevel), progress, cancellationToken);

    public Task PushUpstreamAsync(string remote, string branch, IProgress<string>? progress, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.PushUpstream(_toplevel, remote, branch), progress, cancellationToken);

    public Task MergetoolAsync(string path, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.Mergetool(_toplevel, path), null, cancellationToken);

    public Task SetLocalConfigAsync(string key, string value, CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            await _scheduler.WriteAsync(async token =>
            {
                Checked(await ExecuteAsync(GitCommands.SetLocal(_toplevel, key, value), null, token).ConfigureAwait(false));
                return 0;
            }, ct).ConfigureAwait(false);
            await ReloadConfigAsync(ct).ConfigureAwait(false);
            await LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: false).ConfigureAwait(false);
        }, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _scheduler.Dispose();
        await Task.CompletedTask;
    }

    private async Task OpenCoreAsync(string path, CancellationToken cancellationToken)
    {
        await RunAsync(async ct =>
        {
            var top = Checked(await ExecuteInAsync(GitCommands.TopLevel(path), path, ct).ConfigureAwait(false));
            var dir = Checked(await ExecuteInAsync(GitCommands.GitDir(path), path, ct).ConfigureAwait(false));
            _toplevel = RepoPath.Normalize(Encoding.UTF8.GetString(top.Stdout).Trim());
            _gitDirectory = RepoPath.Normalize(Encoding.UTF8.GetString(dir.Stdout).Trim());
            await ReloadConfigAsync(ct).ConfigureAwait(false);
            var statusTask = QueryStatusAsync(ct);
            var refsTask = QueryRefsAsync(ct);
            var logTask = QueryLogAsync(0, HistoryLimits.FirstPage, ct);
            await Task.WhenAll(statusTask, refsTask, logTask).ConfigureAwait(false);
            var status = await statusTask.ConfigureAwait(false);
            var refs = await refsTask.ConfigureAwait(false);
            var log = await logTask.ConfigureAwait(false);
            lock (_stateLock)
            {
                ApplyStatus(status);
                _refs = refs.Refs.ToList();
                _remotes = refs.Remotes.ToList();
                _tipSignature = Tips(_branch, _refs);
                _lanes.Reset();
                _commits = Build(_lanes, log.Commits);
                _historyEnded = log.Ended;
                _historyCapped = false;
                _historyGeneration = 1;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadRefsAndMaybeHistoryAsync(CancellationToken cancellationToken, bool statusAlreadyApplied)
    {
        var statusTask = statusAlreadyApplied ? null : QueryStatusAsync(cancellationToken);
        var refsTask = QueryRefsAsync(cancellationToken);
        if (statusTask is not null)
            await Task.WhenAll(statusTask, refsTask).ConfigureAwait(false);
        else
            await refsTask.ConfigureAwait(false);

        var refs = await refsTask.ConfigureAwait(false);
        var reload = false;
        lock (_stateLock)
        {
            var oldOid = _branch.Oid;
            var oldTips = _tipSignature;
            if (statusTask is not null)
                ApplyStatus(statusTask.Result);
            _refs = refs.Refs.ToList();
            _remotes = refs.Remotes.ToList();
            _tipSignature = Tips(_branch, _refs);
            reload = oldOid != _branch.Oid || oldTips != _tipSignature;
        }

        if (reload)
            await ReloadHistoryCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReloadHistoryCoreAsync(CancellationToken cancellationToken)
    {
        var page = await QueryLogAsync(0, HistoryLimits.FirstPage, cancellationToken).ConfigureAwait(false);
        lock (_stateLock)
        {
            _historyGeneration++;
            _lanes.Reset();
            _commits = Build(_lanes, page.Commits);
            _historyEnded = page.Ended;
            _historyCapped = false;
        }
    }

    private async Task<bool> LoadMoreCoreAsync(bool pastCap, CancellationToken cancellationToken)
    {
        int skip;
        int generation;
        lock (_stateLock)
        {
            if (_historyEnded)
                return false;
            if (_commits.Count >= HistoryLimits.SoftCap && !pastCap)
            {
                _historyCapped = true;
                return false;
            }

            skip = _commits.Count;
            generation = _historyGeneration;
        }

        var page = await QueryLogAsync(skip, HistoryLimits.Page, cancellationToken).ConfigureAwait(false);
        lock (_stateLock)
        {
            if (generation != _historyGeneration)
                return false;
            foreach (var commit in page.Commits)
                _commits.Add(new GraphCommit { Commit = commit, Lanes = _lanes.Assign(commit) });
            _historyEnded = page.Ended;
            _historyCapped = !page.Ended && _commits.Count >= HistoryLimits.SoftCap;
            return page.Commits.Count > 0;
        }
    }

    private async Task<StatusLoad> QueryStatusAsync(CancellationToken cancellationToken)
    {
        return await _scheduler.ReadAsync(async token =>
        {
            var output = Checked(await ExecuteAsync(GitCommands.Status(_toplevel), null, token).ConfigureAwait(false));
            return new StatusLoad(StatusParser.Parse(output.Stdout), output.Duration);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RefLoad> QueryRefsAsync(CancellationToken cancellationToken)
    {
        return await _scheduler.ReadAsync(async token =>
        {
            var refsOutput = Checked(await ExecuteAsync(GitCommands.Refs(_toplevel), null, token).ConfigureAwait(false));
            var remoteOutput = Checked(await ExecuteAsync(GitCommands.Remotes(_toplevel), null, token).ConfigureAwait(false));
            var names = Encoding.UTF8.GetString(remoteOutput.Stdout)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            return new RefLoad(RefParser.Parse(refsOutput.Stdout, _encoding), names);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LogLoad> QueryLogAsync(int skip, int count, CancellationToken cancellationToken)
    {
        return await _scheduler.ReadAsync(async token =>
        {
            var output = await ExecuteAsync(GitCommands.Log(_toplevel, skip, count), null, token).ConfigureAwait(false);
            if (output.ExitCode != 0 && LogParser.IsUnborn(output.StandardError))
            {
                // HEAD is unborn. Ask again without it so commits on other branches still appear.
                output = await ExecuteAsync(GitCommands.Log(_toplevel, skip, count, includeHead: false), null, token).ConfigureAwait(false);
            }

            if (output.ExitCode != 0)
            {
                Track(output);
                if (LogParser.IsUnborn(output.StandardError))
                    return new LogLoad([], true);
                throw new GitCommandFailedException(output);
            }

            Track(output);
            var commits = LogParser.Parse(output.Stdout, _encoding);
            return new LogLoad(commits, commits.Count < count);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReloadConfigAsync(CancellationToken cancellationToken)
    {
        await _scheduler.ReadAsync(async token =>
        {
            var output = await ExecuteAsync(GitCommands.ConfigList(_toplevel), null, token).ConfigureAwait(false);
            Track(output);
            if (output.ExitCode != 0)
                return 0;
            var config = ConfigParser.Parse(output.Stdout);
            lock (_stateLock)
            {
                _config = config;
                _encoding = ConfigParser.LogEncoding(config);
            }

            return 0;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task MutateAsync(
        IReadOnlyList<string> arguments,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? whenHeadMissing = null)
    {
        await RunAsync(async ct =>
        {
            GitCommandFailedException? failure = null;
            try
            {
                await _scheduler.WriteAsync(async token =>
                {
                    var output = await ExecuteAsync(arguments, progress, token).ConfigureAwait(false);
                    if (output.ExitCode != 0 && whenHeadMissing is not null && IsMissingHead(output.StandardError))
                        output = await ExecuteAsync(whenHeadMissing, null, token).ConfigureAwait(false);
                    Checked(output);
                    return 0;
                }, ct).ConfigureAwait(false);
            }
            catch (GitCommandFailedException exception)
            {
                failure = exception;
            }

            try
            {
                await LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: false).ConfigureAwait(false);
            }
            catch (GitCommandFailedException) when (failure is not null)
            {
            }

            if (failure is not null)
                throw failure;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await work(linked.Token).ConfigureAwait(false);
    }

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        return await work(linked.Token).ConfigureAwait(false);
    }

    private Task<GitOutput> ExecuteAsync(IReadOnlyList<string> arguments, IProgress<string>? progress, CancellationToken cancellationToken) =>
        ExecuteInAsync(arguments, _toplevel, progress, cancellationToken);

    private Task<GitOutput> ExecuteInAsync(IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken) =>
        ExecuteInAsync(arguments, workingDirectory, null, cancellationToken);

    private Task<GitOutput> ExecuteInAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken) =>
        _runner.RunAsync(new GitRequest
        {
            Executable = _executable,
            Arguments = arguments,
            WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? null : workingDirectory,
            Progress = progress,
        }, cancellationToken);

    private GitOutput Checked(GitOutput output)
    {
        Track(output);
        if (output.ExitCode != 0)
            throw new GitCommandFailedException(output);
        return output;
    }

    private void Track(GitOutput output)
    {
        var entry = new CommandLogEntry(
            DateTimeOffset.UtcNow,
            output.DisplayArguments,
            output.ExitCode,
            output.Duration,
            output.StandardError);
        lock (_stateLock)
        {
            _commands.Add(entry);
            if (_commands.Count > 200)
                _commands.RemoveAt(0);
        }
    }

    private void ApplyStatus(StatusLoad status)
    {
        _branch = status.Snapshot.Branch;
        _entries = status.Snapshot.Entries.ToList();
        _statusDuration = status.Duration;
        var mergeHead = Path.Combine(_gitDirectory, "MERGE_HEAD");
        _merge = File.Exists(mergeHead) || _entries.Exists(entry => entry.Kind == ChangeKind.Unmerged);
        var messagePath = Path.Combine(_gitDirectory, "MERGE_MSG");
        _mergeMessage = null;
        if (_merge && File.Exists(messagePath))
        {
            try
            {
                _mergeMessage = File.ReadAllText(messagePath);
            }
            catch (IOException)
            {
            }
        }
        _suggestion = PerformanceAdvisor.Evaluate(status.Duration, _config);
    }

    private bool IsUnborn()
    {
        lock (_stateLock)
            return _branch.Unborn;
    }

    private async Task<DiffDocument> DiffUntrackedAsync(string path, bool allowLarge, CancellationToken cancellationToken)
    {
        // git diff --no-index exits 1 when the files differ. That is a diff, not a failure.
        var output = await ExecuteAsync(GitCommands.DiffUntracked(_toplevel, path), null, cancellationToken).ConfigureAwait(false);
        var failed = output.ExitCode != 0
            && (output.ExitCode != 1 || output.Stdout.Length == 0 || output.StandardError.Contains("fatal:", StringComparison.OrdinalIgnoreCase));
        if (failed)
        {
            Track(output);
            throw new GitCommandFailedException(output);
        }

        Track(output);
        return ToDiff(output, allowLarge);
    }

    private static bool IsMissingHead(string standardError) =>
        standardError.Contains("could not resolve 'HEAD'", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("ambiguous argument 'HEAD'", StringComparison.OrdinalIgnoreCase);

    private DiffDocument ToDiff(GitOutput output, bool allowLarge)
    {
        if (!allowLarge && output.Stdout.Length > HistoryLimits.MaxDiffBytes)
            return DiffDocument.TooLarge;
        var document = DiffParser.Parse(_encoding.GetString(output.Stdout));
        if (!allowLarge && document.LineCount > HistoryLimits.MaxDiffLines)
            return DiffDocument.TooLarge;
        return document;
    }

    private static List<GraphCommit> Build(LaneAssigner lanes, IReadOnlyList<CommitRecord> commits)
    {
        var rows = new List<GraphCommit>(commits.Count);
        foreach (var commit in commits)
            rows.Add(new GraphCommit { Commit = commit, Lanes = lanes.Assign(commit) });
        return rows;
    }

    private static string Tips(BranchHeader branch, IReadOnlyList<GitRef> refs)
    {
        var builder = new StringBuilder(branch.Oid);
        foreach (var reference in refs.OrderBy(reference => reference.Name, StringComparer.Ordinal))
            builder.Append('|').Append(reference.Name).Append('=').Append(reference.Oid);
        return builder.ToString();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct StatusLoad(StatusSnapshot Snapshot, TimeSpan Duration);

    private readonly record struct RefLoad(IReadOnlyList<GitRef> Refs, IReadOnlyList<string> Remotes);

    private readonly record struct LogLoad(IReadOnlyList<CommitRecord> Commits, bool Ended);
}
