using System.Globalization;

namespace Sextant.Git;

/// <summary>
/// Argument lists for the system git binary. Reads pass --no-optional-locks. Writes do not.
/// for-each-ref on Git for Windows 2.55 has no -z switch, so refs use a tab-separated format.
/// </summary>
public static class GitCommands
{
    public static IReadOnlyList<string> Version() => ["--version"];

    public static IReadOnlyList<string> TopLevel(string path) =>
        ["-C", path, "--no-optional-locks", "rev-parse", "--show-toplevel"];

    public static IReadOnlyList<string> GitDir(string path) =>
        ["-C", path, "--no-optional-locks", "rev-parse", "--absolute-git-dir"];

    public static IReadOnlyList<string> Status(string toplevel) =>
        ["-C", toplevel, "--no-optional-locks", "status", "--porcelain=v2", "-z", "-b"];

    public static IReadOnlyList<string> Refs(string toplevel) =>
    [
        "-C", toplevel, "--no-optional-locks", "for-each-ref",
        "--format=%(objectname)\t%(refname)\t%(HEAD)\t%(upstream:short)",
    ];

    public static IReadOnlyList<string> Remotes(string toplevel) =>
        ["-C", toplevel, "--no-optional-locks", "remote"];

    public static IReadOnlyList<string> ConfigList(string toplevel) =>
        ["-C", toplevel, "--no-optional-locks", "config", "--null", "--list"];

    public static IReadOnlyList<string> Log(string toplevel, int skip, int count, bool includeHead = true)
    {
        // An unborn HEAD (fresh init, or an orphan branch) is not a revision. Passing it
        // makes log exit 128 with "ambiguous argument 'HEAD'" before --branches is considered.
        var arguments = new List<string>
        {
            "-C", toplevel, "--no-optional-locks", "log", "-z", "--date-order",
        };
        if (includeHead)
            arguments.Add("HEAD");
        arguments.Add("--branches");
        arguments.Add("--tags");
        arguments.Add("--remotes");
        arguments.Add("--format=%H%x1f%P%x1f%at%x1f%an%x1f%ae%x1f%s");
        arguments.Add("-n");
        arguments.Add(count.ToString(CultureInfo.InvariantCulture));
        arguments.Add("--skip");
        arguments.Add(skip.ToString(CultureInfo.InvariantCulture));
        return arguments;
    }

    public static IReadOnlyList<string> NameStatus(string toplevel, string sha) =>
        ["-C", toplevel, "--no-optional-locks", "show", "-z", "--format=", "--name-status", sha];

    public static IReadOnlyList<string> DiffUnstaged(string toplevel, string path) =>
        ["-C", toplevel, "--no-optional-locks", "diff", "--", path];

    public static IReadOnlyList<string> DiffUntracked(string toplevel, string path) =>
        ["-C", toplevel, "--no-optional-locks", "diff", "--no-index", "--", "/dev/null", path];

    public static IReadOnlyList<string> DiffStaged(string toplevel, string path) =>
        ["-C", toplevel, "--no-optional-locks", "diff", "--cached", "--", path];

    public static IReadOnlyList<string> DiffRange(string toplevel, string older, string newer, string path) =>
        ["-C", toplevel, "--no-optional-locks", "diff", older, newer, "--", path];

    public static IReadOnlyList<string> ShowPatch(string toplevel, string sha, string path) =>
        ["-C", toplevel, "--no-optional-locks", "show", "--format=", "-p", sha, "--", path];

    public static IReadOnlyList<string> Stage(string toplevel, string path) =>
        ["-C", toplevel, "add", "--", path];

    public static IReadOnlyList<string> Unstage(string toplevel, string path) =>
        ["-C", toplevel, "restore", "--staged", "--", path];

    /// <summary>
    /// Unstage before the first commit. restore --staged cannot resolve HEAD, so the
    /// index entry is removed and the worktree file stays.
    /// </summary>
    public static IReadOnlyList<string> UnstageUnborn(string toplevel, string path) =>
        ["-C", toplevel, "rm", "--cached", "-f", "--", path];

    public static IReadOnlyList<string> DiscardTracked(string toplevel, string path) =>
        ["-C", toplevel, "restore", "--source=HEAD", "--worktree", "--staged", "--", path];

    /// <summary>
    /// Discard a file that has never been committed. Removes it from the index and the worktree.
    /// </summary>
    public static IReadOnlyList<string> DiscardUnborn(string toplevel, string path) =>
        ["-C", toplevel, "rm", "-f", "--", path];

    public static IReadOnlyList<string> DiscardUntracked(string toplevel, string path) =>
        ["-C", toplevel, "clean", "-f", "--", path];

    public static IReadOnlyList<string> ApplyCached(string toplevel, string patchFile) =>
        ["-C", toplevel, "apply", "--cached", patchFile];

    public static IReadOnlyList<string> ApplyCachedReverse(string toplevel, string patchFile) =>
        ["-C", toplevel, "apply", "--cached", "--reverse", patchFile];

    public static IReadOnlyList<string> Commit(string toplevel, string messageFile) =>
        ["-C", toplevel, "commit", "-F", messageFile];

    public static IReadOnlyList<string> Switch(string toplevel, string branch) =>
        ["-C", toplevel, "switch", branch];

    public static IReadOnlyList<string> SwitchTrack(string toplevel, string remoteBranch) =>
        ["-C", toplevel, "switch", "--track", remoteBranch];

    public static IReadOnlyList<string> CreateBranch(string toplevel, string name) =>
        ["-C", toplevel, "switch", "-c", name];

    public static IReadOnlyList<string> DeleteBranch(string toplevel, string name) =>
        ["-C", toplevel, "branch", "-d", name];

    public static IReadOnlyList<string> SetUpstream(string toplevel, string branch, string upstream) =>
        ["-C", toplevel, "branch", "--set-upstream-to=" + upstream, branch];

    public static IReadOnlyList<string> Merge(string toplevel, string branch) =>
        ["-C", toplevel, "merge", "--no-edit", branch];

    public static IReadOnlyList<string> AbortMerge(string toplevel) =>
        ["-C", toplevel, "merge", "--abort"];

    public static IReadOnlyList<string> Fetch(string toplevel) =>
        ["-C", toplevel, "fetch", "--progress"];

    public static IReadOnlyList<string> Pull(string toplevel) =>
        ["-C", toplevel, "pull", "--progress", "--no-edit"];

    public static IReadOnlyList<string> Push(string toplevel) =>
        ["-C", toplevel, "push", "--progress"];

    public static IReadOnlyList<string> PushUpstream(string toplevel, string remote, string branch) =>
        ["-C", toplevel, "push", "--progress", "-u", remote, branch];

    public static IReadOnlyList<string> Mergetool(string toplevel, string path) =>
        ["-C", toplevel, "mergetool", "--no-prompt", "--", path];

    public static IReadOnlyList<string> Init(string path) =>
        ["init", path];

    public static IReadOnlyList<string> Clone(string url, string path) =>
        ["clone", "--progress", url, path];

    public static IReadOnlyList<string> SetLocal(string toplevel, string key, string value) =>
        ["-C", toplevel, "config", "--local", key, value];

    public static IReadOnlyList<string> AddSafeDirectory(string path) =>
        ["config", "--global", "--add", "safe.directory", path];
}
