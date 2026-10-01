namespace Sextant.Git.Tests;

public class SessionTests
{
    [Fact]
    public async Task Open_unborn_repository_is_empty_history()
    {
        using var repo = new TempRepo();
        await using var session = await Open(repo);
        var state = session.Snapshot();
        Assert.True(state.Branch.Unborn);
        Assert.Empty(state.Commits);
        Assert.DoesNotContain(state.Commands, command => command.ExitCode != 0);
    }

    [Fact]
    public async Task Open_orphan_branch_keeps_commits_from_other_branches()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.Run("switch", "--orphan", "scratch");
        await using var session = await Open(repo);
        var state = session.Snapshot();
        Assert.True(state.Branch.Unborn);
        Assert.Contains(state.Commits, row => row.Commit.Subject == "first");
    }

    [Fact]
    public async Task Unstage_before_the_first_commit_keeps_the_worktree_file()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "hello\n");
        await using var session = await Open(repo);
        await session.StageFileAsync("a.txt", CancellationToken.None);
        repo.WriteFile("a.txt", "hello\nworld\n");
        await session.UnstageFileAsync("a.txt", CancellationToken.None);

        var path = Path.Combine(repo.Directory, "a.txt");
        Assert.Contains("world", File.ReadAllText(path), StringComparison.Ordinal);
        var state = session.Snapshot();
        Assert.Contains(state.Entries, entry => entry.Path == "a.txt" && entry.Kind == ChangeKind.Untracked);
        Assert.DoesNotContain(state.Entries, entry => entry.Staged);
        Assert.DoesNotContain(state.Commands, command => command.ExitCode != 0);
    }

    [Fact]
    public async Task Discard_before_the_first_commit_removes_the_new_file()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "hello\n");
        await using var session = await Open(repo);
        await session.StageFileAsync("a.txt", CancellationToken.None);
        await session.DiscardTrackedAsync("a.txt", CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(repo.Directory, "a.txt")));
        Assert.DoesNotContain(session.Snapshot().Entries, entry => entry.Path == "a.txt");
    }

    [Fact]
    public async Task Untracked_diff_shows_the_new_file()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "hello\n");
        await using var session = await Open(repo);
        var diff = await session.WorkingDiffAsync("a.txt", staged: false, untracked: true, allowLarge: true, CancellationToken.None);
        Assert.NotNull(diff);
        Assert.True(diff.IsNewFile);
        Assert.Contains(diff.Hunks.SelectMany(hunk => hunk.Lines), line => line.Text == "hello");
    }

    [Fact]
    public async Task Unstage_after_a_commit_leaves_the_worktree_edit()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "two\n");
        await using var session = await Open(repo);
        await session.StageFileAsync("a.txt", CancellationToken.None);
        await session.UnstageFileAsync("a.txt", CancellationToken.None);

        Assert.Contains("two", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
        var state = session.Snapshot();
        Assert.Contains(state.Entries, entry => entry.Path == "a.txt" && entry.Unstaged && !entry.Staged);
    }

    [Fact]
    public async Task Open_shows_the_working_copy_and_history()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "two\n");
        await using var session = await Open(repo);
        var state = session.Snapshot();
        Assert.Equal(repo.CurrentBranch(), state.Branch.HeadName);
        Assert.Single(state.Commits);
        Assert.Contains(state.Entries, entry => entry.Path == "a.txt" && entry.Unstaged);
    }

    [Fact]
    public async Task Commit_adds_a_history_row()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "two\n");
        await using var session = await Open(repo);
        await session.StageFileAsync("a.txt", CancellationToken.None);
        await session.CommitAsync("second\n", CancellationToken.None);
        var state = session.Snapshot();
        Assert.Equal("second", state.Commits[0].Commit.Subject);
        Assert.Equal(2, state.Commits.Count);
        Assert.Empty(state.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Staging_the_middle_hunk_leaves_the_other_two(bool autocrlf)
    {
        using var repo = new TempRepo();
        repo.Run("config", "core.autocrlf", autocrlf ? "true" : "false");
        var lines = Enumerable.Range(0, 40).Select(index => $"line {index}").ToArray();
        repo.WriteFile("a.txt", string.Join('\n', lines) + "\n");
        repo.CommitAll("base");
        lines[2] = "changed-a";
        lines[18] = "changed-b";
        lines[34] = "changed-c";
        repo.WriteFile("a.txt", string.Join('\n', lines) + "\n");

        await using var session = await Open(repo);
        var diff = await session.WorkingDiffAsync("a.txt", staged: false, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(diff);
        Assert.Equal(3, diff.Hunks.Count);
        await session.ApplyHunkAsync(diff.RawPatch, 1, reverse: false, CancellationToken.None);

        var staged = repo.RunCapture("diff", "--cached", "--", "a.txt");
        Assert.Contains("changed-b", staged, StringComparison.Ordinal);
        Assert.DoesNotContain("changed-a", staged, StringComparison.Ordinal);
        Assert.DoesNotContain("changed-c", staged, StringComparison.Ordinal);

        var after = await session.WorkingDiffAsync("a.txt", staged: true, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(after);
        await session.ApplyHunkAsync(after.RawPatch, 0, reverse: true, CancellationToken.None);
        var cleared = repo.RunCapture("diff", "--cached");
        Assert.DoesNotContain("changed-b", cleared, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conflicted_merge_can_be_aborted()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var branch = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("a.txt", "other\n");
        repo.CommitAll("other");
        repo.Run("switch", branch);
        repo.WriteFile("a.txt", "main\n");
        repo.CommitAll("main");

        await using var session = await Open(repo);
        await Assert.ThrowsAsync<GitCommandFailedException>(() => session.MergeAsync("other", CancellationToken.None));
        var conflicted = session.Snapshot();
        Assert.True(conflicted.MergeInProgress);
        Assert.Contains(conflicted.Entries, entry => entry.Kind == ChangeKind.Unmerged && entry.Path == "a.txt");

        await session.AbortMergeAsync(CancellationToken.None);
        var cleared = session.Snapshot();
        Assert.False(cleared.MergeInProgress);
        Assert.DoesNotContain(cleared.Entries, entry => entry.Kind == ChangeKind.Unmerged);
    }

    [Fact]
    public async Task Push_and_pull_against_a_local_remote()
    {
        using var origin = new TempRepo();
        origin.WriteFile("a.txt", "one\n");
        origin.CommitAll("first");
        var bare = Path.Combine(Path.GetTempPath(), "sextant-bare-" + Guid.NewGuid().ToString("N"));
        var clone = Path.Combine(Path.GetTempPath(), "sextant-clone-" + Guid.NewGuid().ToString("N"));
        try
        {
            origin.Run("clone", "--bare", origin.Directory, bare);
            var runner = new GitProcessRunner();
            await RepositoryAdmin.CloneAsync(runner, origin.Git, bare, clone, null, CancellationToken.None);
            origin.SetIdentity(clone);
            await using var session = await RepositorySession.OpenAsync(runner, origin.Git, clone, CancellationToken.None);
            var branch = session.Snapshot().Branch.HeadName!;
            using (var writer = new StreamWriter(Path.Combine(clone, "a.txt"), append: true))
                writer.Write("two\n");
            await session.StageFileAsync("a.txt", CancellationToken.None);
            await session.CommitAsync("second\n", CancellationToken.None);
            await session.PushAsync(null, CancellationToken.None);

            origin.Run("remote", "add", "origin", bare);
            origin.Run("fetch", "origin");
            origin.Run("merge", "--no-edit", "origin/" + branch);
            Assert.Contains("two", File.ReadAllText(Path.Combine(origin.Directory, "a.txt")), StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(bare);
            TryDelete(clone);
        }
    }

    [Fact]
    public async Task Stage_all_and_unstage_all_round_trip_after_a_commit()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "two\n");
        repo.WriteFile("b.txt", "new\n");
        await using var session = await Open(repo);

        await session.StageAllAsync(CancellationToken.None);
        var staged = session.Snapshot();
        Assert.Contains(staged.Entries, entry => entry.Path == "a.txt" && entry.Staged && !entry.Unstaged);
        Assert.Contains(staged.Entries, entry => entry.Path == "b.txt" && entry.Staged);

        await session.UnstageAllAsync(CancellationToken.None);
        var cleared = session.Snapshot();
        Assert.Contains(cleared.Entries, entry => entry.Path == "a.txt" && entry.Unstaged && !entry.Staged);
        Assert.Contains(cleared.Entries, entry => entry.Path == "b.txt" && entry.Kind == ChangeKind.Untracked);
        Assert.Contains("two", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
        Assert.DoesNotContain(cleared.Commands, command => command.ExitCode != 0);
    }

    [Fact]
    public async Task Unstage_all_before_the_first_commit_keeps_the_worktree_files()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "hello\n");
        repo.WriteFile("b.txt", "there\n");
        await using var session = await Open(repo);
        await session.StageAllAsync(CancellationToken.None);
        await session.UnstageAllAsync(CancellationToken.None);

        Assert.Contains("hello", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
        Assert.Contains("there", File.ReadAllText(Path.Combine(repo.Directory, "b.txt")), StringComparison.Ordinal);
        var state = session.Snapshot();
        Assert.Contains(state.Entries, entry => entry.Path == "a.txt" && entry.Kind == ChangeKind.Untracked);
        Assert.Contains(state.Entries, entry => entry.Path == "b.txt" && entry.Kind == ChangeKind.Untracked);
        Assert.DoesNotContain(state.Entries, entry => entry.Staged);
        Assert.DoesNotContain(state.Commands, command => command.ExitCode != 0);
    }

    [Fact]
    public async Task Stage_all_during_a_conflict_leaves_the_unmerged_path()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var branch = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("a.txt", "other\n");
        repo.CommitAll("other");
        repo.Run("switch", branch);
        repo.WriteFile("a.txt", "main\n");
        repo.CommitAll("main");

        await using var session = await Open(repo);
        await Assert.ThrowsAsync<GitCommandFailedException>(() => session.MergeAsync("other", CancellationToken.None));
        repo.WriteFile("b.txt", "side\n");
        await session.RefreshStatusAsync(CancellationToken.None);
        await session.StageAllAsync(CancellationToken.None);

        var state = session.Snapshot();
        Assert.Contains(state.Entries, entry => entry.Path == "a.txt" && entry.Kind == ChangeKind.Unmerged);
        Assert.Contains(state.Entries, entry => entry.Path == "b.txt" && entry.Staged);
        Assert.DoesNotContain(state.Commands, command => command.Arguments.Contains("-A"));
    }

    [Fact]
    public async Task Commit_without_hooks_skips_a_failing_pre_commit()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        var hook = Path.Combine(repo.Directory, ".empty-hooks", "pre-commit");
        File.WriteAllText(hook, "#!/bin/sh\nexit 1\n", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        repo.WriteFile("a.txt", "two\n");
        await using var session = await Open(repo);
        await session.StageFileAsync("a.txt", CancellationToken.None);

        await Assert.ThrowsAsync<GitCommandFailedException>(() => session.CommitAsync("blocked\n", CancellationToken.None));
        Assert.Contains(session.Snapshot().Entries, entry => entry.Path == "a.txt" && entry.Staged);

        await session.CommitAsync("allowed\n", CancellationToken.None, noVerify: true);
        var state = session.Snapshot();
        Assert.Equal("allowed", state.Commits[0].Commit.Subject);
        Assert.Contains(state.Commands, command => command.Arguments.Contains("--no-verify") && command.ExitCode == 0);
    }

    [Fact]
    public async Task Merge_commit_lists_files_changed_from_the_first_parent()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "feature");
        repo.WriteFile("feature.txt", "feature\n");
        repo.CommitAll("feature work");
        repo.Run("switch", trunk);
        repo.WriteFile("trunk.txt", "trunk\n");
        repo.CommitAll("trunk work");
        repo.Run("switch", "feature");
        repo.Run("merge", "--no-edit", trunk);
        var sha = repo.RunCapture("rev-parse", "HEAD").Trim();
        var parent = repo.RunCapture("rev-parse", "HEAD^1").Trim();
        Assert.True(string.IsNullOrWhiteSpace(repo.RunCapture("show", "--format=", "--name-only", sha)));

        await using var session = await Open(repo);
        var files = await session.CommitFilesAsync(sha, parent, CancellationToken.None);
        var change = Assert.Single(files!);
        Assert.Equal("trunk.txt", change.Path);
        Assert.Equal(ChangeKind.Added, change.Kind);
    }

    [Fact]
    public async Task Root_commit_lists_files_without_a_parent()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        var sha = repo.RunCapture("rev-parse", "HEAD").Trim();
        await using var session = await Open(repo);
        var files = await session.CommitFilesAsync(sha, null, CancellationToken.None);
        Assert.Equal("a.txt", Assert.Single(files!).Path);
    }

    [Fact]
    public async Task Cancelling_the_runner_kills_the_process()
    {
        var runner = new GitProcessRunner();
        using var cts = new CancellationTokenSource();
        var executable = OperatingSystem.IsWindows() ? "ping" : "sleep";
        var arguments = OperatingSystem.IsWindows()
            ? new[] { "-n", "30", "127.0.0.1" }
            : new[] { "30" };
        var started = DateTime.UtcNow;
        var task = runner.RunAsync(new GitRequest { Executable = executable, Arguments = arguments }, cts.Token);
        await Task.Delay(200);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);

    private static void TryDelete(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
