using Sextant.Git;
using Sextant.Git.Parsing;

namespace Sextant.Git.Tests;

public class Phase2Tests
{
    [Fact]
    public void History_query_reads_branch_author_and_sha()
    {
        var branch = HistoryQueryParser.Parse("branch:\"feature work\" author:Ada fix");
        Assert.Equal("feature work", branch.Revision);
        Assert.Equal("Ada", branch.Author);
        Assert.Equal("fix", branch.Grep);
        Assert.False(branch.MatchSubjectOrAuthor);

        var plain = HistoryQueryParser.Parse("icon fix");
        Assert.True(plain.MatchSubjectOrAuthor);
        Assert.Equal("icon fix", plain.Grep);
        Assert.Equal("icon fix", plain.Author);

        var sha = HistoryQueryParser.Parse("abc1234");
        Assert.True(sha.ShaLookup);
        Assert.Equal("abc1234", sha.Revision);
        Assert.True(HistoryQueryParser.Parse("   ").IsEmpty);
    }

    [Fact]
    public void Blame_parser_reads_porcelain_groups()
    {
        var text = """
            abcdef1234567890abcdef1234567890abcdef12 1 1 1
            author Ada
            author-mail <ada@example.com>
            author-time 1700000000
            author-tz +0000
            committer Ada
            committer-mail <ada@example.com>
            committer-time 1700000000
            committer-tz +0000
            summary First
            filename a.txt
            	hello
            0000000000000000000000000000000000000000 2 2 1
            author Not Committed Yet
            author-mail <not.committed.yet>
            author-time 1700000001
            author-tz +0000
            committer Not Committed Yet
            committer-mail <not.committed.yet>
            committer-time 1700000001
            committer-tz +0000
            summary 
            filename a.txt
            	world
            """;
        var lines = BlameParser.Parse(text);
        Assert.Equal(2, lines.Count);
        Assert.Equal("Ada", lines[0].Author);
        Assert.Equal("hello", lines[0].Text);
        Assert.False(lines[0].Uncommitted);
        Assert.Equal("world", lines[1].Text);
        Assert.True(lines[1].Uncommitted);
    }

    [Fact]
    public void Line_patch_keeps_only_the_selected_change()
    {
        var patch = """
            diff --git a/a.txt b/a.txt
            --- a/a.txt
            +++ b/a.txt
            @@ -1,3 +1,4 @@
             line1
            -line2
            +line2b
             line3
            +line4
            """;
        var sliced = LinePatch.Slice(patch.Replace("\r\n", "\n", StringComparison.Ordinal), 0, 4);
        Assert.NotNull(sliced);
        Assert.Contains("@@ -1,3 +1,4 @@", sliced, StringComparison.Ordinal);
        Assert.Contains("+line4", sliced, StringComparison.Ordinal);
        Assert.DoesNotContain("+line2b", sliced, StringComparison.Ordinal);
        Assert.DoesNotContain("-line2", sliced, StringComparison.Ordinal);
        Assert.Contains(" line2", sliced, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_finds_a_subject_and_an_author()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("alpha unique");
        repo.Run("config", "user.name", "Ada Lovelace");
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("ordinary message");

        await using var session = await Open(repo);
        await session.SetHistoryAsync(HistoryQueryParser.Parse("alpha unique"), CancellationToken.None);
        var bySubject = session.Snapshot();
        Assert.Contains(bySubject.Commits, row => row.Commit.Subject == "alpha unique");
        Assert.DoesNotContain(bySubject.Commits, row => row.Commit.Subject == "ordinary message");

        await session.SetHistoryAsync(HistoryQueryParser.Parse("Ada"), CancellationToken.None);
        var byAuthor = session.Snapshot();
        Assert.Contains(byAuthor.Commits, row => row.Commit.Subject == "ordinary message");
        Assert.DoesNotContain(byAuthor.Commits, row => row.Commit.Subject == "alpha unique");

        var sha = bySubject.Commits[0].Commit.Sha;
        await session.SetHistoryAsync(HistoryQueryParser.Parse(sha), CancellationToken.None);
        var bySha = session.Snapshot();
        Assert.Single(bySha.Commits);
        Assert.Equal(sha, bySha.Commits[0].Commit.Sha);
        Assert.True(bySha.HistoryEnded);

        await session.SetHistoryAsync(null, CancellationToken.None);
        Assert.Null(session.Snapshot().HistoryLabel);
        Assert.Equal(2, session.Snapshot().Commits.Count);
    }

    [Fact]
    public async Task File_history_lists_commits_that_touch_the_path()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("touch a");
        repo.WriteFile("b.txt", "other\n");
        repo.CommitAll("touch b");
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("touch a again");

        await using var session = await Open(repo);
        await session.SetHistoryAsync(HistoryQuery.ForPath("a.txt"), CancellationToken.None);
        var state = session.Snapshot();
        Assert.Equal(2, state.Commits.Count);
        Assert.Contains(state.Commits, row => row.Commit.Subject == "touch a");
        Assert.Contains(state.Commits, row => row.Commit.Subject == "touch a again");
        Assert.Equal("File a.txt", state.HistoryLabel);
    }

    [Fact]
    public async Task Blame_names_the_commit_that_wrote_the_line()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        var sha = repo.RunCapture("rev-parse", "HEAD").Trim();
        await using var session = await Open(repo);
        var blame = await session.BlameAsync(sha, "a.txt", allowLarge: true, CancellationToken.None);
        Assert.NotNull(blame);
        Assert.False(blame.IsTooLarge);
        Assert.Contains(blame.Lines, line => line.Text == "one" && line.Sha.StartsWith(sha[..7], StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stash_push_pop_and_drop_round_trip()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "two\n");
        await using var session = await Open(repo);
        await session.StashPushAsync("wip note", CancellationToken.None);
        var stashed = session.Snapshot();
        Assert.Empty(stashed.Entries);
        Assert.Single(stashed.Stashes);
        Assert.Contains("wip note", stashed.Stashes[0].Subject, StringComparison.Ordinal);
        Assert.Contains(stashed.Commits, row => row.Commit.Subject.Contains("wip note", StringComparison.Ordinal));

        await session.StashPopAsync(stashed.Stashes[0].Ref, CancellationToken.None);
        var restored = session.Snapshot();
        Assert.Empty(restored.Stashes);
        Assert.Contains(restored.Entries, entry => entry.Path == "a.txt");
        Assert.Contains("two", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mixed_reset_keeps_the_later_change_unstaged()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        var first = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("second");
        await using var session = await Open(repo);
        await session.ResetAsync("--mixed", first, CancellationToken.None);
        var state = session.Snapshot();
        Assert.Equal(first, state.Branch.Oid);
        Assert.Contains(state.Entries, entry => entry.Path == "a.txt" && entry.Unstaged && !entry.Staged);
        Assert.Contains("two", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cherry_pick_applies_the_commit_and_abort_clears_a_conflict()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var branch = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("b.txt", "from-other\n");
        repo.CommitAll("add b");
        var picked = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("switch", branch);
        await using var session = await Open(repo);
        await session.CherryPickAsync(picked, CancellationToken.None);
        Assert.Contains("from-other", File.ReadAllText(Path.Combine(repo.Directory, "b.txt")), StringComparison.Ordinal);
        Assert.Equal(SequencerKind.None, session.Snapshot().Sequencer);

        repo.WriteFile("a.txt", "main\n");
        repo.CommitAll("main");
        repo.Run("switch", "other");
        repo.WriteFile("a.txt", "other\n");
        repo.CommitAll("other");
        var conflict = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("switch", branch);
        await Assert.ThrowsAsync<GitCommandFailedException>(() => session.CherryPickAsync(conflict, CancellationToken.None));
        Assert.Equal(SequencerKind.CherryPick, session.Snapshot().Sequencer);
        await session.AbortSequencerAsync(CancellationToken.None);
        Assert.Equal(SequencerKind.None, session.Snapshot().Sequencer);
        Assert.DoesNotContain(session.Snapshot().Entries, entry => entry.Kind == ChangeKind.Unmerged);
    }

    [Fact]
    public async Task Tag_and_remote_commands_update_refs()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        var sha = repo.RunCapture("rev-parse", "HEAD").Trim();
        var other = Path.Combine(Path.GetTempPath(), "sextant-remote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(other);
        try
        {
            await using var session = await Open(repo);
            await session.CreateTagAsync("v1", sha, CancellationToken.None);
            Assert.Contains(session.Snapshot().Refs, reference => reference.Name == "refs/tags/v1");
            await session.DeleteTagAsync("v1", CancellationToken.None);
            Assert.DoesNotContain(session.Snapshot().Refs, reference => reference.Name == "refs/tags/v1");

            await session.AddRemoteAsync("origin", other, CancellationToken.None);
            Assert.Contains("origin", session.Snapshot().Remotes);
            await session.RenameRemoteAsync("origin", "upstream", CancellationToken.None);
            Assert.Contains("upstream", session.Snapshot().Remotes);
            Assert.DoesNotContain("origin", session.Snapshot().Remotes);
            await session.RemoveRemoteAsync("upstream", CancellationToken.None);
            Assert.DoesNotContain("upstream", session.Snapshot().Remotes);
        }
        finally
        {
            if (Directory.Exists(other))
                Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public async Task Ignore_whitespace_hides_a_space_only_change()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "alpha \n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "alpha\n");
        await using var session = await Open(repo);
        var shown = await session.WorkingDiffAsync("a.txt", staged: false, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(shown);
        Assert.NotEmpty(shown.Hunks);
        var ignored = await session.WorkingDiffAsync("a.txt", staged: false, untracked: false, allowLarge: true, CancellationToken.None, ignoreWhitespace: true);
        Assert.NotNull(ignored);
        Assert.Empty(ignored.Hunks);
    }

    [Fact]
    public async Task Staging_one_added_line_leaves_the_other_unstaged()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "one\nalpha\nbeta\n");
        await using var session = await Open(repo);
        var diff = await session.WorkingDiffAsync("a.txt", staged: false, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(diff);
        Assert.Single(diff.Hunks);
        var beta = diff.Hunks[0].Lines.ToList().FindIndex(line => line.Kind == DiffLineKind.Added && line.Text == "beta");
        Assert.True(beta >= 0);
        await session.ApplyLineAsync(diff.RawPatch, 0, beta, reverse: false, CancellationToken.None);
        var staged = repo.RunCapture("diff", "--cached", "--", "a.txt");
        Assert.Contains("beta", staged, StringComparison.Ordinal);
        Assert.DoesNotContain("alpha", staged, StringComparison.Ordinal);
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
