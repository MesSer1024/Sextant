using Sextant.Git.Parsing;

namespace Sextant.Git.Tests;

public class Phase3Tests
{
    [Fact]
    public async Task Rebase_conflict_is_a_rebase_and_abort_clears_it()
    {
        using var repo = new TempRepo();
        StartRebase(repo);
        await using var session = await Open(repo);
        Assert.Equal(SequencerKind.Rebase, session.Snapshot().Sequencer);

        await session.AbortSequencerAsync(CancellationToken.None);
        Assert.Equal(SequencerKind.None, session.Snapshot().Sequencer);
        Assert.Contains("feature", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rebase_continue_keeps_the_resolution()
    {
        using var repo = new TempRepo();
        StartRebase(repo);
        await using var session = await Open(repo);
        repo.WriteFile("a.txt", "resolved\n");
        await session.StageFileAsync("a.txt", CancellationToken.None);
        await session.ContinueSequencerAsync(CancellationToken.None);

        var state = session.Snapshot();
        Assert.Equal(SequencerKind.None, state.Sequencer);
        Assert.Contains("resolved", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
        Assert.Contains(state.Commits, row => row.Commit.Subject == "feature");
    }

    [Fact]
    public async Task Cherry_pick_continue_keeps_the_resolution()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var branch = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("a.txt", "other\n");
        repo.CommitAll("other");
        var picked = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("switch", branch);
        repo.WriteFile("a.txt", "main\n");
        repo.CommitAll("main");

        await using var session = await Open(repo);
        await Assert.ThrowsAsync<GitCommandFailedException>(() => session.CherryPickAsync(picked, CancellationToken.None));
        Assert.Equal(SequencerKind.CherryPick, session.Snapshot().Sequencer);
        repo.WriteFile("a.txt", "picked\n");
        await session.StageFileAsync("a.txt", CancellationToken.None);
        await session.ContinueSequencerAsync(CancellationToken.None);

        var state = session.Snapshot();
        Assert.Equal(SequencerKind.None, state.Sequencer);
        Assert.Contains("picked", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
        Assert.Contains(state.Commits, row => row.Commit.Subject == "other");
    }

    [Fact]
    public async Task Save_resolution_stages_the_chosen_side_and_clears_the_conflict()
    {
        using var repo = new TempRepo();
        StartMerge(repo);
        await using var session = await Open(repo);
        await Assert.ThrowsAsync<GitCommandFailedException>(() => session.MergeAsync("other", CancellationToken.None));

        var conflict = await session.ConflictAsync("a.txt", allowLarge: true, CancellationToken.None);
        Assert.NotNull(conflict);
        Assert.False(conflict!.IsBinary);
        Assert.False(conflict.Synthetic);
        Assert.Contains(conflict.Pieces, piece => piece.IsConflict && piece.Ours.Contains("main", StringComparison.Ordinal) && piece.Theirs.Contains("other", StringComparison.Ordinal));

        var resolved = ConflictParser.Compose(conflict.Pieces.Select(piece => piece.IsConflict ? piece with { Result = piece.Theirs } : piece).ToList());
        await session.SaveResolutionAsync("a.txt", resolved, CancellationToken.None);

        var state = session.Snapshot();
        Assert.Equal(SequencerKind.Merge, state.Sequencer);
        Assert.DoesNotContain(state.Entries, entry => entry.Kind == ChangeKind.Unmerged);
        Assert.Contains(state.Entries, entry => entry.Path == "a.txt" && entry.Staged);
        var text = File.ReadAllText(Path.Combine(repo.Directory, "a.txt"));
        Assert.Contains("other", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<<<<<<<", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_resolution_keeps_an_unmerged_file_when_markers_remain()
    {
        using var repo = new TempRepo();
        StartMerge(repo);
        await using var session = await Open(repo);
        await Assert.ThrowsAsync<GitCommandFailedException>(() => session.MergeAsync("other", CancellationToken.None));
        var conflict = await session.ConflictAsync("a.txt", allowLarge: true, CancellationToken.None);
        Assert.NotNull(conflict);
        var edited = "kept\n" + ConflictParser.Compose(conflict!.Pieces);
        var exception = await Assert.ThrowsAsync<RepositoryActionException>(() => session.SaveResolutionAsync("a.txt", edited, CancellationToken.None));
        Assert.Contains("stays unmerged", exception.Message, StringComparison.Ordinal);

        var state = session.Snapshot();
        Assert.Contains(state.Entries, entry => entry.Path == "a.txt" && entry.Kind == ChangeKind.Unmerged);
        var text = File.ReadAllText(Path.Combine(repo.Directory, "a.txt"));
        Assert.StartsWith("kept\n", text, StringComparison.Ordinal);
        Assert.Contains("<<<<<<<", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Modify_delete_conflict_uses_the_index_stages()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.Run("rm", "a.txt");
        repo.CommitAll("delete");
        repo.Run("switch", trunk);
        repo.WriteFile("a.txt", "main\n");
        repo.CommitAll("main");

        await using var session = await Open(repo);
        await Assert.ThrowsAsync<GitCommandFailedException>(() => session.MergeAsync("other", CancellationToken.None));
        var conflict = await session.ConflictAsync("a.txt", allowLarge: true, CancellationToken.None);
        Assert.NotNull(conflict);
        Assert.True(conflict!.Synthetic);
        Assert.False(conflict.IsBinary);
        var piece = Assert.Single(conflict.Pieces);
        Assert.Contains("main", piece.Ours, StringComparison.Ordinal);
        Assert.Equal("", piece.Theirs);
        Assert.NotNull(piece.Base);
        Assert.Contains("base", piece.Base, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_resolution_rejects_a_path_outside_the_repository()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        await using var session = await Open(repo);
        await Assert.ThrowsAsync<RepositoryActionException>(() => session.SaveResolutionAsync("../outside.txt", "x\n", CancellationToken.None));
        await Assert.ThrowsAsync<RepositoryActionException>(() => session.SaveResolutionAsync("dir/../../outside.txt", "x\n", CancellationToken.None));
        var parent = Path.GetDirectoryName(repo.Directory)!;
        Assert.False(File.Exists(Path.Combine(parent, "outside.txt")));
    }

    private static void StartMerge(TempRepo repo)
    {
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("a.txt", "other\n");
        repo.CommitAll("other");
        repo.Run("switch", trunk);
        repo.WriteFile("a.txt", "main\n");
        repo.CommitAll("main");
    }

    private static void StartRebase(TempRepo repo)
    {
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "feature");
        repo.WriteFile("a.txt", "feature\n");
        repo.CommitAll("feature");
        repo.Run("switch", trunk);
        repo.WriteFile("a.txt", "main\n");
        repo.CommitAll("main");
        repo.Run("switch", "feature");
        Assert.Throws<InvalidOperationException>(() => repo.Run("rebase", trunk));
    }

    [Fact]
    public async Task Interactive_rebase_rewords_fixup_and_drop_without_an_editor()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("one");
        repo.WriteFile("b.txt", "two\n");
        repo.CommitAll("two");
        repo.WriteFile("b.txt", "two-fix\n");
        repo.CommitAll("fix two");
        repo.WriteFile("c.txt", "drop\n");
        repo.CommitAll("drop me");

        await using var session = await Open(repo);
        var bySubject = session.Snapshot().Commits.ToDictionary(row => row.Commit.Subject, row => row.Commit);
        var steps = new[]
        {
            new RebaseStep(bySubject["one"].Sha, "one", RebaseVerb.Reword, "one renamed"),
            new RebaseStep(bySubject["two"].Sha, "two", RebaseVerb.Pick, null),
            new RebaseStep(bySubject["fix two"].Sha, "fix two", RebaseVerb.Fixup, null),
            new RebaseStep(bySubject["drop me"].Sha, "drop me", RebaseVerb.Drop, null),
        };
        await session.RebaseInteractiveAsync(bySubject["one"].Parents[0], steps, CancellationToken.None);

        var subjects = session.Snapshot().Commits.Select(row => row.Commit.Subject).ToList();
        Assert.Equal("two", subjects[0]);
        Assert.Equal("one renamed", subjects[1]);
        Assert.DoesNotContain(subjects, subject => subject is "drop me" or "fix two" or "one");
        Assert.Equal("two-fix\n", File.ReadAllText(Path.Combine(repo.Directory, "b.txt")).Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(repo.Directory, "c.txt")));
        Assert.False(File.Exists(Path.Combine(session.GitDirectory, RebaseEditor.PointerName)));
    }

    [Fact]
    public async Task Interactive_rebase_squash_keeps_the_message_you_type()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("one");
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("two");

        await using var session = await Open(repo);
        var bySubject = session.Snapshot().Commits.ToDictionary(row => row.Commit.Subject, row => row.Commit);
        var steps = new[]
        {
            new RebaseStep(bySubject["one"].Sha, "one", RebaseVerb.Pick, null),
            new RebaseStep(bySubject["two"].Sha, "two", RebaseVerb.Squash, "squashed together"),
        };
        await session.RebaseInteractiveAsync(bySubject["one"].Parents[0], steps, CancellationToken.None);

        var state = session.Snapshot();
        Assert.Equal(SequencerKind.None, state.Sequencer);
        Assert.Equal("squashed together", state.Commits[0].Commit.Subject);
        Assert.Equal("two\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Interactive_rebase_edit_amends_the_stopped_commit()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        repo.WriteFile("a.txt", "edit\n");
        repo.CommitAll("edit me");

        await using var session = await Open(repo);
        var tip = session.Snapshot().Commits[0].Commit;
        await session.RebaseInteractiveAsync(
            tip.Parents[0],
            [new RebaseStep(tip.Sha, tip.Subject, RebaseVerb.Edit, null)],
            CancellationToken.None);
        Assert.Equal(SequencerKind.Rebase, session.Snapshot().Sequencer);

        repo.WriteFile("a.txt", "edited\n");
        await session.StageFileAsync("a.txt", CancellationToken.None);
        await session.AmendAsync("edited message\n", CancellationToken.None);
        await session.ContinueSequencerAsync(CancellationToken.None);

        var state = session.Snapshot();
        Assert.Equal(SequencerKind.None, state.Sequencer);
        Assert.Equal("edited message", state.Commits[0].Commit.Subject);
        Assert.Contains("edited", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(session.GitDirectory, RebaseEditor.PointerName)));
    }

    [Fact]
    public async Task Amend_replaces_the_tip_message_and_keeps_unstaged_work()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("second");
        repo.WriteFile("a.txt", "three\n");
        repo.WriteFile("note.txt", "leave me\n");

        await using var session = await Open(repo);
        await session.StageFileAsync("a.txt", CancellationToken.None);
        await session.AmendAsync("second rewritten", CancellationToken.None);

        var state = session.Snapshot();
        Assert.Equal("second rewritten", state.Commits[0].Commit.Subject);
        Assert.Equal("three\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")).Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Contains(state.Entries, entry => entry.Path == "note.txt" && entry.Kind == ChangeKind.Untracked);
        Assert.DoesNotContain(state.Entries, entry => entry.Path == "a.txt");
    }

    [Fact]
    public async Task Force_with_lease_updates_a_rewritten_tip_and_names_the_remote_commit()
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
            File.WriteAllText(Path.Combine(clone, "a.txt"), "two\n");
            await session.StageFileAsync("a.txt", CancellationToken.None);
            await session.CommitAsync("second\n", CancellationToken.None);
            await session.PushAsync(null, CancellationToken.None);

            File.WriteAllText(Path.Combine(clone, "a.txt"), "three\n");
            await session.StageFileAsync("a.txt", CancellationToken.None);
            await session.AmendAsync("second rewritten\n", CancellationToken.None);
            var replaced = await session.ListUpstreamOnlyAsync(CancellationToken.None);
            Assert.Contains(replaced, commit => commit.Subject == "second");

            await session.PushForceWithLeaseAsync(null, CancellationToken.None);
            Assert.Equal("second rewritten", GitSubject(origin.Git, bare));
        }
        finally
        {
            TryDelete(bare);
            TryDelete(clone);
        }
    }

    private static string GitSubject(string git, string gitDirectory)
    {
        var info = new System.Diagnostics.ProcessStartInfo(git)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("--git-dir");
        info.ArgumentList.Add(gitDirectory);
        info.ArgumentList.Add("log");
        info.ArgumentList.Add("-1");
        info.ArgumentList.Add("--format=%s");
        using var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        var text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return text.Trim();
    }

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

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
