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
