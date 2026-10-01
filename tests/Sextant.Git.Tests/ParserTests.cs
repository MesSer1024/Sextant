using System.Text;
using Sextant.Git.Parsing;

namespace Sextant.Git.Tests;

public class StatusParserTests
{
    [Fact]
    public void Parses_headers_ordinary_rename_unmerged_and_untracked()
    {
        var hash = new string('a', 40);
        var other = new string('b', 40);
        var text =
            "# branch.oid " + hash + "\0" +
            "# branch.head main\0" +
            "# branch.upstream origin/main\0" +
            "# branch.ab +2 -1\0" +
            "1 .M N... 100644 100644 100644 " + hash + " " + other + " file name.txt\0" +
            "2 R. N... 100644 100644 100644 " + hash + " " + other + " R100 new file.txt\0old name.txt\0" +
            "u UU N... 100644 100644 100644 100644 " + hash + " " + other + " " + hash + " conflict.txt\0" +
            "? untracked.txt\0" +
            "! ignored.txt\0";

        var snapshot = StatusParser.Parse(text);

        Assert.Equal(hash, snapshot.Branch.Oid);
        Assert.Equal("main", snapshot.Branch.HeadName);
        Assert.Equal("origin/main", snapshot.Branch.Upstream);
        Assert.Equal(2, snapshot.Branch.Ahead);
        Assert.Equal(1, snapshot.Branch.Behind);
        Assert.Equal(4, snapshot.Entries.Count);

        var modified = snapshot.Entries[0];
        Assert.Equal("file name.txt", modified.Path);
        Assert.Equal(ChangeKind.Modified, modified.Kind);
        Assert.False(modified.Staged);
        Assert.True(modified.Unstaged);

        var renamed = snapshot.Entries[1];
        Assert.Equal("new file.txt", renamed.Path);
        Assert.Equal("old name.txt", renamed.OriginalPath);
        Assert.Equal(ChangeKind.Renamed, renamed.Kind);
        Assert.True(renamed.Staged);

        Assert.Equal(ChangeKind.Unmerged, snapshot.Entries[2].Kind);
        Assert.Equal("conflict.txt", snapshot.Entries[2].Path);
        Assert.Equal(ChangeKind.Untracked, snapshot.Entries[3].Kind);
    }

    [Fact]
    public void Marks_an_unborn_branch()
    {
        var snapshot = StatusParser.Parse("# branch.oid (initial)\0# branch.head main\0");
        Assert.True(snapshot.Branch.Unborn);
        Assert.Null(snapshot.Branch.Oid);
        Assert.Equal("main", snapshot.Branch.HeadName);
    }

    [Fact]
    public async Task Parses_live_status()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\ntwo\nthree\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "one\nTWO\nthree\n");
        repo.WriteFile("new.txt", "hello\n");
        var runner = new GitProcessRunner();
        var output = await runner.RunAsync(new GitRequest
        {
            Executable = repo.Git,
            Arguments = GitCommands.Status(repo.Directory),
            WorkingDirectory = repo.Directory,
        }, CancellationToken.None);

        Assert.Equal(0, output.ExitCode);
        var snapshot = StatusParser.Parse(output.Stdout);
        Assert.Contains(snapshot.Entries, entry => entry.Path == "a.txt" && entry.Kind == ChangeKind.Modified && entry.Unstaged);
        Assert.Contains(snapshot.Entries, entry => entry.Path == "new.txt" && entry.Kind == ChangeKind.Untracked);
    }
}

public class LogAndRefParserTests
{
    [Fact]
    public void Parses_a_root_commit_and_a_merge()
    {
        var root = "aaa\u001f\u001f100\u001fAda\u001fa@b\u001froot";
        var merge = "ccc\u001faaa bbb\u001f300\u001fAda\u001fa@b\u001fmerge";
        var text = root + "\0" + merge + "\0";
        var commits = LogParser.Parse(text);
        Assert.Equal(2, commits.Count);
        Assert.Empty(commits[0].Parents);
        Assert.Equal(["aaa", "bbb"], commits[1].Parents);
        Assert.Equal("merge", commits[1].Subject);
    }

    [Fact]
    public void Parses_head_marker_and_upstream()
    {
        var text = "abc\trefs/heads/main\t*\torigin/main\n" +
                   "def\trefs/remotes/origin/main\t \t\n";
        var refs = RefParser.Parse(Encoding.UTF8.GetBytes(text), Encoding.UTF8);
        Assert.Equal(2, refs.Count);
        Assert.True(refs[0].IsHead);
        Assert.Equal("origin/main", refs[0].Upstream);
        Assert.False(refs[1].IsHead);
        Assert.Null(refs[1].Upstream);
    }

    [Fact]
    public async Task Parses_live_log_and_refs()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var branch = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("a.txt", "side\n");
        repo.CommitAll("side");
        repo.Run("switch", branch);
        repo.WriteFile("b.txt", "main\n");
        repo.CommitAll("main");
        repo.Run("merge", "--no-edit", "other");
        repo.Run("tag", "v1");

        var runner = new GitProcessRunner();
        var log = await runner.RunAsync(new GitRequest
        {
            Executable = repo.Git,
            Arguments = GitCommands.Log(repo.Directory, 0, 20),
            WorkingDirectory = repo.Directory,
        }, CancellationToken.None);
        Assert.Equal(0, log.ExitCode);
        var commits = LogParser.Parse(log.Stdout, Encoding.UTF8);
        Assert.Contains(commits, commit => commit.Parents.Count == 2);
        Assert.Contains(commits, commit => commit.Parents.Count == 0);

        var refs = await runner.RunAsync(new GitRequest
        {
            Executable = repo.Git,
            Arguments = GitCommands.Refs(repo.Directory),
            WorkingDirectory = repo.Directory,
        }, CancellationToken.None);
        var parsed = RefParser.Parse(refs.Stdout, Encoding.UTF8);
        Assert.Contains(parsed, reference => reference.Name == "refs/heads/" + branch && reference.IsHead);
        Assert.Contains(parsed, reference => reference.Name == "refs/tags/v1");
    }
}

public class NameStatusAndDiffTests
{
    [Fact]
    public void Name_status_keeps_old_then_new_for_renames()
    {
        var text = "M\0src/a.txt\0R100\0old name.txt\0new name.txt\0";
        var changes = NameStatusParser.Parse(text);
        Assert.Equal("src/a.txt", changes[0].Path);
        Assert.Equal(ChangeKind.Modified, changes[0].Kind);
        Assert.Equal("new name.txt", changes[1].Path);
        Assert.Equal("old name.txt", changes[1].OriginalPath);
        Assert.Equal(ChangeKind.Renamed, changes[1].Kind);
    }

    [Fact]
    public void Parses_two_hunks_and_a_binary_notice()
    {
        var patch = """
            diff --git a/a.txt b/a.txt
            index 111..222 100644
            --- a/a.txt
            +++ b/a.txt
            @@ -1,3 +1,3 @@
             one
            -two
            +TWO
             three
            @@ -10,2 +10,2 @@
             keep
            -old
            +new
            """;
        var document = DiffParser.Parse(patch.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal(2, document.Hunks.Count);
        Assert.Equal(1, document.Hunks[0].OldStart);
        Assert.Contains(document.Hunks[0].Lines, line => line.Kind == DiffLineKind.Added && line.Text == "TWO");
        Assert.True(DiffParser.Parse("diff --git a/a.bin b/a.bin\nBinary files a/a.bin and b/a.bin differ\n").IsBinary);
        Assert.True(DiffParser.Parse("diff --git a/a.bin b/a.bin\nGIT binary patch\nliteral 4\n").IsBinary);
    }

    [Fact]
    public void Text_that_mentions_a_binary_notice_stays_text()
    {
        var patch = """
            diff --git a/notes.md b/notes.md
            --- a/notes.md
            +++ b/notes.md
            @@ -1,1 +1,3 @@
             notes
            +A binary notice is the line `Binary files a and b differ` or `GIT binary patch`.
            +code.Contains("Binary files ");
            """;
        var document = DiffParser.Parse(patch.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.False(document.IsBinary);
        var hunk = Assert.Single(document.Hunks);
        Assert.Contains(hunk.Lines, line => line.Kind == DiffLineKind.Added && line.Text.Contains("GIT binary patch", StringComparison.Ordinal));
        Assert.Contains(hunk.Lines, line => line.Kind == DiffLineKind.Added && line.Text.Contains("Binary files ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Parses_a_live_rename()
    {
        using var repo = new TempRepo();
        repo.WriteFile("old name.txt", "hello\n");
        repo.CommitAll("base");
        repo.Run("mv", "old name.txt", "new name.txt");
        repo.CommitAll("renamed");
        var sha = repo.RunCapture("rev-parse", "HEAD").Trim();
        var runner = new GitProcessRunner();
        var output = await runner.RunAsync(new GitRequest
        {
            Executable = repo.Git,
            Arguments = GitCommands.NameStatus(repo.Directory, sha),
            WorkingDirectory = repo.Directory,
        }, CancellationToken.None);
        var changes = NameStatusParser.Parse(output.Stdout);
        var renamed = Assert.Single(changes);
        Assert.Equal(ChangeKind.Renamed, renamed.Kind);
        Assert.Equal("old name.txt", renamed.OriginalPath);
        Assert.Equal("new name.txt", renamed.Path);
    }
}

public class ConflictParserTests
{
    [Fact]
    public void Simple_conflict_round_trips_and_take_ours_drops_the_markers()
    {
        var text = "before\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> other\nafter\n";
        var pieces = ConflictParser.Parse(text);
        Assert.Equal(3, pieces.Count);
        Assert.Equal("before\n", pieces[0].Context);
        Assert.True(pieces[1].IsConflict);
        Assert.Equal("ours\n", pieces[1].Ours);
        Assert.Equal("theirs\n", pieces[1].Theirs);
        Assert.Null(pieces[1].Base);
        Assert.Equal("after\n", pieces[2].Context);
        Assert.Equal(text, ConflictParser.Compose(pieces));
        Assert.True(ConflictParser.ContainsMarkers(text));

        var taken = pieces.Select(piece => piece.IsConflict ? piece with { Result = piece.Ours } : piece).ToList();
        var composed = ConflictParser.Compose(taken);
        Assert.Equal("before\nours\nafter\n", composed);
        Assert.DoesNotContain("<<<<<<<", composed, StringComparison.Ordinal);
        Assert.False(ConflictParser.ContainsMarkers(composed));
    }

    [Fact]
    public void Diff3_keeps_the_base_and_the_original_line_endings()
    {
        var text = "<<<<<<< HEAD\r\nours\r\n||||||| parent\r\nbase\r\n=======\r\ntheirs\r\n>>>>>>> other\r\n";
        var pieces = ConflictParser.Parse(text);
        var conflict = Assert.Single(pieces);
        Assert.Equal("ours\r\n", conflict.Ours);
        Assert.Equal("base\r\n", conflict.Base);
        Assert.Equal("theirs\r\n", conflict.Theirs);
        Assert.Equal(text, ConflictParser.Compose(pieces));

        var taken = ConflictParser.Compose([conflict with { Result = conflict.Theirs }]);
        Assert.Equal("theirs\r\n", taken);
        Assert.False(ConflictParser.ContainsMarkers(taken));
    }

    [Fact]
    public void Unclosed_marker_stays_context()
    {
        var text = "<<<<<<< HEAD\nstuff\n";
        var piece = Assert.Single(ConflictParser.Parse(text));
        Assert.False(piece.IsConflict);
        Assert.Equal(text, piece.Context);
        Assert.True(ConflictParser.ContainsMarkers(text));
    }

    [Fact]
    public void Empty_side_and_a_second_region_stay_separate()
    {
        var text = "<<<<<<< HEAD\n=======\ntheirs\n>>>>>>> other\nmiddle\n<<<<<<< HEAD\nours\n=======\n>>>>>>> other\n";
        var pieces = ConflictParser.Parse(text);
        Assert.Equal(3, pieces.Count);
        Assert.Equal("", pieces[0].Ours);
        Assert.Equal("theirs\n", pieces[0].Theirs);
        Assert.Equal("middle\n", pieces[1].Context);
        Assert.Equal("ours\n", pieces[2].Ours);
        Assert.Equal("", pieces[2].Theirs);
        Assert.Equal(text, ConflictParser.Compose(pieces));
    }

    [Fact]
    public void A_path_must_stay_inside_the_repository()
    {
        var root = Path.Combine(Path.GetTempPath(), "sextant-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.NotNull(RepoPath.CombineUnder(root, "a.txt"));
            Assert.NotNull(RepoPath.CombineUnder(root, "dir/a.txt"));
            Assert.Null(RepoPath.CombineUnder(root, "../a.txt"));
            Assert.Null(RepoPath.CombineUnder(root, "dir/../../a.txt"));
            Assert.Null(RepoPath.CombineUnder(root, "a\0.txt"));
            Assert.Null(RepoPath.CombineUnder(root, "a\n.txt"));
            Assert.Null(RepoPath.CombineUnder(root, Path.Combine(root, "a.txt")));
        }
        finally
        {
            Directory.Delete(root);
        }
    }
}
