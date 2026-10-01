namespace Sextant.Git.Tests;

public class CoreTests
{
    [Theory]
    [InlineData("git version 2.42.0", false)]
    [InlineData("git version 2.43.0", true)]
    [InlineData("git version 2.55.0.windows.5", true)]
    public void Version_floor_is_2_43(string text, bool supported)
    {
        var version = GitVersions.Parse(text);
        Assert.Equal(supported, GitVersions.IsSupported(version));
    }

    [Fact]
    public void Redacts_url_userinfo()
    {
        var redacted = ArgumentRedactor.Redact("https://user:token@github.com/a/b.git");
        Assert.DoesNotContain("token", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("user", redacted, StringComparison.Ordinal);
        Assert.Contains("github.com", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_use_no_optional_locks_and_writes_do_not()
    {
        Assert.Contains("--no-optional-locks", GitCommands.Status("repo"));
        Assert.Contains("--no-optional-locks", GitCommands.Log("repo", 0, 10));
        Assert.DoesNotContain("--no-optional-locks", GitCommands.Commit("repo", "msg"));
        Assert.DoesNotContain("--no-verify", GitCommands.Commit("repo", "msg"));
        Assert.Contains("--no-verify", GitCommands.Commit("repo", "msg", noVerify: true));
        Assert.Contains("-A", GitCommands.StageAll("repo"));
        Assert.Equal(":", GitCommands.UnstageAll("repo")[^1]);
        Assert.Equal(["rm", "-r", "--cached", "-f", "--", "."], GitCommands.UnstageAllUnborn("repo").Skip(2));
        Assert.DoesNotContain("--no-optional-locks", GitCommands.Fetch("repo"));
        Assert.Equal(["-C", "repo", "branch", "-d", "topic"], GitCommands.DeleteBranch("repo", "topic"));
        Assert.Equal(["-C", "repo", "branch", "-D", "topic"], GitCommands.ForceDeleteBranch("repo", "topic"));
        Assert.Contains("--no-edit", GitCommands.Merge("repo", "topic"));
        Assert.Contains("--no-edit", GitCommands.Pull("repo"));
        Assert.Contains("--", GitCommands.Stage("repo", "a file.txt"));
        Assert.Equal(["rm", "--cached", "-f", "--", "a.txt"], GitCommands.UnstageUnborn("repo", "a.txt").Skip(2));
        Assert.Equal(["rm", "-f", "--", "a.txt"], GitCommands.DiscardUnborn("repo", "a.txt").Skip(2));
        Assert.Contains("restore", GitCommands.Unstage("repo", "a.txt"));
        Assert.Equal("-f", GitCommands.DiscardUntracked("repo", "a.txt")[3]);
        Assert.DoesNotContain("-d", GitCommands.DiscardUntracked("repo", "a.txt"));
        Assert.DoesNotContain("-x", GitCommands.DiscardUntracked("repo", "a.txt"));
    }

    [Fact]
    public void Lane_assigner_keeps_a_line_and_a_diamond()
    {
        var lanes = new LaneAssigner();
        var linear = new[]
        {
            Commit("c", "b"),
            Commit("b", "a"),
            Commit("a"),
        };
        var nodes = linear.Select(commit => lanes.Assign(commit).NodeLane).ToArray();
        Assert.Equal([0, 0, 0], nodes);

        lanes.Reset();
        var diamond = new[]
        {
            Commit("m", "a", "b"),
            Commit("b", "r"),
            Commit("a", "r"),
            Commit("r"),
        };
        Assert.Equal([0, 1, 0, 0], diamond.Select(commit => lanes.Assign(commit).NodeLane).ToArray());

        lanes.Reset();
        var fork = new[]
        {
            Commit("tipB", "base"),
            Commit("tipA", "base"),
            Commit("base"),
        };
        Assert.Equal([0, 1, 0], fork.Select(commit => lanes.Assign(commit).NodeLane).ToArray());
    }

    [Fact]
    public void Hunk_slice_keeps_only_the_selected_hunk()
    {
        var patch = "diff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ b/a.txt\n@@ -1,1 +1,1 @@\n-a\n+b\n@@ -8,1 +8,1 @@\n-c\n+d\n";
        var slice = HunkPatch.Slice(patch, 1);
        Assert.Contains("@@ -8,1 +8,1 @@", slice, StringComparison.Ordinal);
        Assert.DoesNotContain("@@ -1,1 +1,1 @@", slice, StringComparison.Ordinal);
        Assert.StartsWith("diff --git", slice, StringComparison.Ordinal);
    }

    [Fact]
    public void Stale_generation_is_dropped()
    {
        var gate = new RequestGate();
        var first = gate.Next();
        var second = gate.Next();
        Assert.False(gate.IsCurrent(first));
        Assert.True(gate.IsCurrent(second));
    }

    [Fact]
    public void Slow_status_offers_unset_performance_keys()
    {
        var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["feature.manyfiles"] = "true",
        };
        var suggestion = PerformanceAdvisor.Evaluate(TimeSpan.FromSeconds(2), config);
        Assert.NotNull(suggestion);
        Assert.False(suggestion.Value.ManyFiles);
        Assert.True(suggestion.Value.FileSystemMonitor);
        Assert.Null(PerformanceAdvisor.Evaluate(TimeSpan.FromMilliseconds(10), config));
    }

    [Fact]
    public void Workspace_round_trip_preserves_tabs_and_settings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sextant-ws-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WorkspaceStore(directory);
            var state = new WorkspaceState
            {
                ActiveTab = @"C:\repos\sextant",
                OpenTabs = [@"C:\repos\sextant", @"C:\repos\other"],
                LocationsWidth = 200,
                GraphWidth = 480,
                FilesHeight = 160,
            };
            store.SaveWorkspace(state);
            store.SaveSettings(new AppSettings { GitExecutable = @"C:\git\git.exe", ReopenTabs = false });

            var loaded = store.LoadWorkspace();
            var settings = store.LoadSettings();
            Assert.Equal(@"C:\repos\sextant", loaded.ActiveTab);
            Assert.Equal(2, loaded.OpenTabs.Count);
            Assert.Equal(200, loaded.LocationsWidth);
            Assert.Equal(480, loaded.GraphWidth);
            Assert.Equal(160, loaded.FilesHeight);
            Assert.Equal(@"C:\git\git.exe", settings.GitExecutable);
            Assert.False(settings.ReopenTabs);

            File.WriteAllText(Path.Combine(directory, "workspace.json"), """
                {
                  "pins": [{ "path": "C:\\old", "name": "old" }],
                  "pinsWidth": 240,
                  "openTabs": ["C:\\repos\\kept"],
                  "activeTab": "C:\\repos\\kept"
                }
                """);
            var legacy = store.LoadWorkspace();
            Assert.Equal(@"C:\repos\kept", legacy.ActiveTab);
            Assert.Equal([@"C:\repos\kept"], legacy.OpenTabs);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Repo_paths_ignore_case_only_on_windows()
    {
        var left = Path.Combine(Path.GetTempPath(), "SextantRepo");
        var right = Path.Combine(Path.GetTempPath(), "sextantrepo");
        if (OperatingSystem.IsWindows())
            Assert.True(RepoPath.Same(left, right));
        else
            Assert.False(RepoPath.Same(left + "A", left + "a"));
    }

    private static CommitRecord Commit(string sha, params string[] parents) =>
        new(sha, parents, 0, "A", "a@b", sha);
}
