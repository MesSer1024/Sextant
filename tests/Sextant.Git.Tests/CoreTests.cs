using Sextant;

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
    public void Redacts_userinfo_inside_command_output()
    {
        var redacted = ArgumentRedactor.RedactText("fatal: unable to access 'https://user:token@github.com/a/b.git/': 403");
        Assert.DoesNotContain("token", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("user:", redacted, StringComparison.Ordinal);
        Assert.Contains("github.com/a/b.git", redacted, StringComparison.Ordinal);
        Assert.Contains("403", redacted, StringComparison.Ordinal);
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
        Assert.Equal(["-C", "repo", "push", "--progress", "origin", "--delete", "topic"], GitCommands.DeleteRemoteBranch("repo", "origin", "topic"));
        Assert.Equal(["-C", "repo", "--no-optional-locks", "rev-list", "--count", "HEAD..origin/topic"], GitCommands.NotInHeadCount("repo", "origin/topic"));
        Assert.Contains("--no-edit", GitCommands.Merge("repo", "topic"));
        Assert.Equal(["-C", "repo", "rebase", "topic"], GitCommands.Rebase("repo", "topic"));
        Assert.Contains("--no-edit", GitCommands.Pull("repo"));
        Assert.Contains("--rebase", GitCommands.Pull("repo"));
        Assert.DoesNotContain("--no-verify", GitCommands.Push("repo"));
        Assert.Contains("--no-verify", GitCommands.Push("repo", noVerify: true));
        Assert.DoesNotContain("--no-verify", GitCommands.PushUpstream("repo", "origin", "topic"));
        Assert.Contains("--no-verify", GitCommands.PushUpstream("repo", "origin", "topic", noVerify: true));
        Assert.Equal(["-u", "origin", "topic"], GitCommands.PushUpstream("repo", "origin", "topic", noVerify: true).TakeLast(3));
        Assert.Contains("--force-with-lease", GitCommands.PushForceWithLease("repo"));
        Assert.DoesNotContain(GitCommands.PushForceWithLease("repo"), argument => argument == "--force");
        Assert.Equal(["-C", "repo", "commit", "--amend", "-C", "HEAD"], GitCommands.Amend("repo", null));
        Assert.Contains("-F", GitCommands.Amend("repo", "msg"));
        Assert.Contains("--root", GitCommands.RebaseInteractive("repo", null));
        Assert.Equal("abc", GitCommands.RebaseInteractive("repo", "abc")[^1]);
        Assert.DoesNotContain("--root", GitCommands.RebaseInteractive("repo", "abc"));
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

        config["core.fsmonitor"] = "true";
        Assert.Null(PerformanceAdvisor.Evaluate(TimeSpan.FromSeconds(2), config));
    }

    [Fact]
    public void Rebase_range_is_the_straight_line_from_head_through_the_selection()
    {
        var commits = new[]
        {
            Commit("c3", "c2"),
            Commit("side", "c1"),
            Commit("c2", "c1"),
            Commit("c1"),
        };
        Assert.True(RebasePlan.TryRange(commits, "c3", ["c2"], out var range, out var error));
        Assert.Null(error);
        Assert.Equal("c1", range!.Upstream);
        Assert.Equal(["c2", "c3"], range.Steps.Select(step => step.Sha).ToArray());
        Assert.All(range.Steps, step => Assert.Equal(RebaseVerb.Pick, step.Verb));

        Assert.True(RebasePlan.TryRange(commits, "c3", ["c1"], out var rooted, out _));
        Assert.Null(rooted!.Upstream);
        Assert.Equal(["c1", "c2", "c3"], rooted.Steps.Select(step => step.Sha).ToArray());

        Assert.False(RebasePlan.TryRange(commits, "c3", ["side"], out _, out error));
        Assert.Contains("straight line", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rebase_todo_rewords_with_amend_and_refuses_a_leading_fixup()
    {
        var steps = new[]
        {
            new RebaseStep("aaa", "one", RebaseVerb.Reword, "next"),
            new RebaseStep("bbb", "two\nbad", RebaseVerb.Fixup, null),
            new RebaseStep("ccc", "three", RebaseVerb.Drop, null),
        };
        var todo = RebasePlan.Render(steps, new Dictionary<int, string> { [0] = "/tmp/my msg" });
        Assert.Contains("pick aaa one\nexec git commit --amend -F '/tmp/my msg'\n", todo, StringComparison.Ordinal);
        Assert.Contains("fixup bbb two bad\n", todo, StringComparison.Ordinal);
        Assert.Contains("drop ccc three\n", todo, StringComparison.Ordinal);
        Assert.Equal("Squash and fixup need a commit before them.", RebasePlan.Validate([new RebaseStep("bbb", "two", RebaseVerb.Fixup, null)]));
        Assert.Equal("Reword needs a message.", RebasePlan.Validate([new RebaseStep("aaa", "one", RebaseVerb.Reword, "  ")]));
    }

    [Fact]
    public void Subject_list_keeps_the_sha_and_subject()
    {
        var commits = RebasePlan.ParseSubjects("abc\u001ffirst\0def\u001fsecond\0");
        Assert.Equal([("abc", "first"), ("def", "second")], commits);
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
                WindowWidth = 1440,
                WindowHeight = 900,
                WindowX = 40,
                WindowY = 20,
                WindowMaximized = true,
            };
            store.SaveWorkspace(state);
            store.SaveSettings(new AppSettings { GitExecutable = @"C:\git\git.exe", ReopenTabs = false });

            var loaded = store.LoadWorkspace();
            var settings = store.LoadSettings();
            Assert.Equal(@"C:\repos\sextant", loaded.ActiveTab);
            Assert.Equal([@"C:\repos\sextant", @"C:\repos\other"], loaded.OpenTabs);
            Assert.Equal(200, loaded.LocationsWidth);
            Assert.Equal(480, loaded.GraphWidth);
            Assert.Equal(160, loaded.FilesHeight);
            Assert.Equal(1440, loaded.WindowWidth);
            Assert.Equal(900, loaded.WindowHeight);
            Assert.Equal(40, loaded.WindowX);
            Assert.Equal(20, loaded.WindowY);
            Assert.True(loaded.WindowMaximized);
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
            Assert.Equal(0, legacy.WindowWidth);
            Assert.Equal(0, legacy.WindowHeight);
            Assert.Null(legacy.WindowX);
            Assert.Null(legacy.WindowY);
            Assert.False(legacy.WindowMaximized);
            var migrated = RepoLayouts.Resolve(legacy, @"C:\repos\kept");
            Assert.Equal(220, migrated.LocationsWidth);
            Assert.Equal(520, migrated.GraphWidth);
            Assert.Equal(180, migrated.FilesHeight);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Pane_sizes_are_stored_per_repository()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sextant-ws-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WorkspaceStore(directory);
            var state = new WorkspaceState
            {
                LocationsWidth = 200,
                GraphWidth = 480,
                FilesHeight = 160,
            };
            var untouched = RepoLayouts.Resolve(state, @"C:\repos\new");
            Assert.Equal(200, untouched.LocationsWidth);
            Assert.Equal(480, untouched.GraphWidth);
            Assert.Equal(160, untouched.FilesHeight);

            RepoLayouts.Remember(state, @"C:\repos\sextant", 310, 430, 190);
            RepoLayouts.Remember(state, @"C:\repos\other", 180, 640, 220);
            store.SaveWorkspace(state);
            var loaded = store.LoadWorkspace();
            var sextant = RepoLayouts.Resolve(loaded, @"C:\repos\sextant");
            var other = RepoLayouts.Resolve(loaded, @"C:\repos\other");
            var third = RepoLayouts.Resolve(loaded, @"C:\repos\third");
            Assert.Equal(310, sextant.LocationsWidth);
            Assert.Equal(430, sextant.GraphWidth);
            Assert.Equal(190, sextant.FilesHeight);
            Assert.Equal(180, other.LocationsWidth);
            Assert.Equal(640, other.GraphWidth);
            Assert.Equal(220, other.FilesHeight);
            Assert.Equal(200, third.LocationsWidth);
            Assert.Equal(480, third.GraphWidth);
            Assert.Equal(160, third.FilesHeight);
            if (OperatingSystem.IsWindows())
                Assert.Equal(310, RepoLayouts.Resolve(loaded, @"c:\repos\sextant").LocationsWidth);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Digit_zero_selects_the_tenth_tab_and_nine_selects_the_ninth()
    {
        Assert.Equal(0, TabShortcut.IndexFromDigit(1));
        Assert.Equal(8, TabShortcut.IndexFromDigit(9));
        Assert.Equal(9, TabShortcut.IndexFromDigit(0));
        Assert.Null(TabShortcut.IndexFromDigit(11));
        Assert.Null(TabShortcut.IndexFromDigit(-1));
        Assert.Equal(OperatingSystem.IsMacOS() ? "⌘1" : "Ctrl+1", TabShortcut.Hint(0));
        Assert.Equal(OperatingSystem.IsMacOS() ? "⌘0" : "Ctrl+0", TabShortcut.Hint(9));
        Assert.Null(TabShortcut.Hint(10));
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

    [Fact]
    public void Repo_paths_match_through_a_directory_symlink()
    {
        var root = Path.Combine(Path.GetTempPath(), "sextant-links-" + Guid.NewGuid().ToString("N"));
        var real = Path.Combine(root, "real");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(real);
        try
        {
            Directory.CreateSymbolicLink(link, real);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Directory.Delete(root, recursive: true);
            return;
        }

        try
        {
            var viaLink = Path.Combine(link, "repo");
            var viaReal = Path.Combine(real, "repo");
            Directory.CreateDirectory(viaReal);
            Assert.True(RepoPath.Same(viaLink, viaReal));
            Assert.True(RepoPath.Same(Path.Combine(link, "missing"), Path.Combine(real, "missing")));
            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(outside);
            var escape = Path.Combine(real, "escape");
            Directory.CreateSymbolicLink(escape, outside);
            Assert.Null(RepoPath.CombineUnder(real, "escape/a.txt"));
        }
        finally
        {
            var escape = Path.Combine(real, "escape");
            if (Directory.Exists(escape))
                Directory.Delete(escape, recursive: false);
            if (Directory.Exists(link))
                Directory.Delete(link, recursive: false);
            Directory.Delete(root, recursive: true);
        }
    }

    private static CommitRecord Commit(string sha, params string[] parents) =>
        new(sha, parents, 0, "A", "a@b", sha);
}
