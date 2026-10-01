using CommunityToolkit.Mvvm.Input;
using Sextant.Git;

namespace Sextant.ViewModels;

public partial class RepositoryViewModel
{
    private readonly List<string> _selectedShas = [];
    private bool _amendAllowed;

    public bool CanAmend => !IsBusy && ShowingWorkingCopy && _amendAllowed;

    public bool CanCommitOrAmend => CanCommit || CanAmend;

    [RelayCommand]
    private Task PushForceWithLease() => PushForceWithLeaseAsync();

    [RelayCommand]
    private Task Amend() => AmendAsync();

    private async Task PushForceWithLeaseAsync()
    {
        if (_session is null || IsBusy)
            return;
        var state = _session.Snapshot();
        if (state.Branch.Detached || state.Branch.Unborn || state.Branch.Upstream is null || state.Branch.HeadName is null)
        {
            Fail("Force-with-lease needs a branch with an upstream. A normal push can set the upstream. This is not a plain force.");
            return;
        }

        if (_host.Dialogs is null)
            return;

        _holdFocusRefresh++;
        try
        {
            IReadOnlyList<(string Sha, string Subject)> replaced;
            try
            {
                replaced = await _session.ListUpstreamOnlyAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (GitCommandFailedException exception)
            {
                Fail(exception.Message);
                return;
            }

            var upstream = state.Branch.Upstream;
            var branch = state.Branch.HeadName;
            var message = DescribeLease(branch, upstream, replaced);
            var ok = await _host.Dialogs.ConfirmAsync("Push with force-with-lease", message, "Push with lease");
            if (!ok || _session is null)
                return;
            var progress = Progress();
            await RunAsync("Pushing with lease…", ct => _session.PushForceWithLeaseAsync(progress, ct));
        }
        finally
        {
            _holdFocusRefresh--;
        }
    }

    private static string DescribeLease(string branch, string upstream, IReadOnlyList<(string Sha, string Subject)> replaced)
    {
        var intro = $"Push {branch} to {upstream} with --force-with-lease.";
        var tail = "This is not a plain force. The push is refused if the remote has moved since the last fetch.";
        if (replaced.Count == 0)
            return intro + "\n\nNo commits on " + upstream + " will be removed.\n\n" + tail;

        var shown = replaced.Take(12).ToList();
        var lines = string.Join("\n", shown.Select(commit => "- " + Short(commit.Sha) + " " + commit.Subject));
        if (replaced.Count > shown.Count)
            lines += "\n- and more";
        return intro + "\n\nThis replaces commits that are already on " + upstream + ":\n" + lines + "\n\n" + tail;
    }

    private async Task AmendAsync()
    {
        if (!CanAmend || _session is null || _host.Dialogs is null)
            return;
        var state = _session.Snapshot();
        var tip = state.Commits.FirstOrDefault(commit =>
            string.Equals(commit.Commit.Sha, state.Branch.Oid, StringComparison.OrdinalIgnoreCase));
        var subject = tip?.Commit.Subject ?? "the tip";
        var sha = tip is null ? "" : Short(tip.Commit.Sha);
        var remote = state.Branch.Upstream is not null && state.Branch.Ahead == 0 && !state.Branch.Detached
            ? $"\n\n{sha} is already on {state.Branch.Upstream}. Amending rewrites a commit the remote has."
            : "";
        var message = $"Amend replaces {sha} {subject}.{remote}\n\nStaged changes become part of that commit. Unstaged changes stay in the working copy.";
        _holdFocusRefresh++;
        try
        {
            var ok = await _host.Dialogs.ConfirmAsync("Amend", message, "Amend");
            if (!ok || _session is null)
                return;
            var text = string.IsNullOrWhiteSpace(CommitMessage) ? null : CommitMessage;
            var wrote = await RunAsync("Amending…", ct => _session.AmendAsync(text, ct));
            if (wrote)
                CommitMessage = "";
        }
        finally
        {
            _holdFocusRefresh--;
        }
    }

    private Task RebaseFromRowAsync(string sha, bool reword)
    {
        var selected = _selectedShas.Exists(item => item.Equals(sha, StringComparison.OrdinalIgnoreCase))
            ? (IReadOnlyList<string>)_selectedShas
            : [sha];
        return RebaseAsync(selected, reword ? sha : null);
    }

    private async Task RebaseAsync(IReadOnlyList<string> selected, string? rewordSha)
    {
        if (_session is null || IsBusy || _host.Dialogs is null)
            return;
        var state = _session.Snapshot();
        var loaded = state.Commits.Select(commit => commit.Commit).ToList();
        if (!RebasePlan.TryRange(loaded, state.Branch.Oid, selected, out var range, out var error) || range is null)
        {
            Fail(error ?? "Those commits cannot be rebased.");
            return;
        }

        var steps = range.Steps.ToList();
        if (rewordSha is not null)
        {
            var index = steps.FindIndex(step => string.Equals(step.Sha, rewordSha, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                steps[index] = steps[index] with { Verb = RebaseVerb.Reword };
        }

        _holdFocusRefresh++;
        try
        {
            var edited = await _host.Dialogs.EditRebaseAsync(steps);
            if (edited is null || _session is null)
                return;
            await RunAsync("Rebasing…", ct => _session.RebaseInteractiveAsync(range.Upstream, edited, ct));
        }
        finally
        {
            _holdFocusRefresh--;
        }
    }

    private static bool RebaseStoppedToEdit(string? gitDirectory)
    {
        if (string.IsNullOrEmpty(gitDirectory))
            return false;
        foreach (var folder in new[] { "rebase-merge", "rebase-apply" })
        {
            var done = Path.Combine(gitDirectory, folder, "done");
            if (!File.Exists(done))
                continue;
            string text;
            try
            {
                text = File.ReadAllText(done);
            }
            catch (IOException)
            {
                return false;
            }

            var line = text.Split('\n').LastOrDefault(item => item.Length > 0 && !item.StartsWith('#'));
            return line is not null && line.StartsWith("edit ", StringComparison.Ordinal);
        }

        return false;
    }
}
