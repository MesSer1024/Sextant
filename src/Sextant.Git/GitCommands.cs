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

    public static IReadOnlyList<string> Log(
        string toplevel,
        int skip,
        int count,
        bool includeHead = true,
        bool includeStash = false,
        string? revision = null,
        string? grep = null,
        string? author = null,
        string? path = null)
    {
        // An unborn HEAD (fresh init, or an orphan branch) is not a revision. Passing it
        // makes log exit 128 with "ambiguous argument 'HEAD'" before --branches is considered.
        var arguments = new List<string>
        {
            "-C", toplevel, "--no-optional-locks", "log", "-z", "--date-order",
        };
        if (!string.IsNullOrEmpty(revision))
        {
            arguments.Add(revision);
        }
        else
        {
            if (includeHead)
                arguments.Add("HEAD");
            arguments.Add("--branches");
            arguments.Add("--tags");
            arguments.Add("--remotes");
            if (includeStash)
                arguments.Add("refs/stash");
        }

        arguments.Add("--format=%H%x1f%P%x1f%at%x1f%an%x1f%ae%x1f%s");
        if (!string.IsNullOrEmpty(grep))
            arguments.Add("--grep=" + grep);
        if (!string.IsNullOrEmpty(author))
            arguments.Add("--author=" + author);
        if (!string.IsNullOrEmpty(grep) || !string.IsNullOrEmpty(author))
        {
            arguments.Add("--fixed-strings");
            arguments.Add("--regexp-ignore-case");
        }
        arguments.Add("-n");
        arguments.Add(count.ToString(CultureInfo.InvariantCulture));
        arguments.Add("--skip");
        arguments.Add(skip.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(path))
        {
            arguments.Add("--");
            arguments.Add(path);
        }

        return arguments;
    }

    public static IReadOnlyList<string> RevParseCommit(string toplevel, string revision) =>
        ["-C", toplevel, "--no-optional-locks", "rev-parse", "--verify", "--quiet", revision + "^{commit}"];

    public static IReadOnlyList<string> Blame(string toplevel, string? revision, string path)
    {
        var arguments = new List<string> { "-C", toplevel, "--no-optional-locks", "blame", "--line-porcelain" };
        if (!string.IsNullOrEmpty(revision))
            arguments.Add(revision);
        arguments.Add("--");
        arguments.Add(path);
        return arguments;
    }

    public static IReadOnlyList<string> StashList(string toplevel) =>
        ["-C", toplevel, "--no-optional-locks", "stash", "list", "--format=%gd%x1f%H%x1f%gs"];

    public static IReadOnlyList<string> StashPush(string toplevel, string? message)
    {
        var arguments = new List<string> { "-C", toplevel, "stash", "push" };
        if (!string.IsNullOrWhiteSpace(message))
        {
            arguments.Add("-m");
            arguments.Add(message);
        }

        return arguments;
    }

    public static IReadOnlyList<string> StashPop(string toplevel, string stashRef) =>
        ["-C", toplevel, "stash", "pop", stashRef];

    public static IReadOnlyList<string> StashApply(string toplevel, string stashRef) =>
        ["-C", toplevel, "stash", "apply", stashRef];

    public static IReadOnlyList<string> StashDrop(string toplevel, string stashRef) =>
        ["-C", toplevel, "stash", "drop", stashRef];

    public static IReadOnlyList<string> Reset(string toplevel, string mode, string sha) =>
        ["-C", toplevel, "reset", mode, sha];

    public static IReadOnlyList<string> CherryPick(string toplevel, string sha) =>
        ["-C", toplevel, "cherry-pick", "--no-edit", sha];

    public static IReadOnlyList<string> Revert(string toplevel, string sha) =>
        ["-C", toplevel, "revert", "--no-edit", sha];

    public static IReadOnlyList<string> AbortCherryPick(string toplevel) =>
        ["-C", toplevel, "cherry-pick", "--abort"];

    public static IReadOnlyList<string> AbortRevert(string toplevel) =>
        ["-C", toplevel, "revert", "--abort"];

    public static IReadOnlyList<string> AbortRebase(string toplevel) =>
        ["-C", toplevel, "rebase", "--abort"];

    public static IReadOnlyList<string> ContinueMerge(string toplevel) =>
        ["-C", toplevel, "merge", "--continue"];

    public static IReadOnlyList<string> ContinueCherryPick(string toplevel) =>
        ["-C", toplevel, "cherry-pick", "--continue"];

    public static IReadOnlyList<string> ContinueRevert(string toplevel) =>
        ["-C", toplevel, "revert", "--continue"];

    public static IReadOnlyList<string> ContinueRebase(string toplevel) =>
        ["-C", toplevel, "rebase", "--continue"];

    public static IReadOnlyList<string> CreateTag(string toplevel, string name, string sha) =>
        ["-C", toplevel, "tag", name, sha];

    public static IReadOnlyList<string> DeleteTag(string toplevel, string name) =>
        ["-C", toplevel, "tag", "-d", name];

    public static IReadOnlyList<string> AddRemote(string toplevel, string name, string url) =>
        ["-C", toplevel, "remote", "add", name, url];

    public static IReadOnlyList<string> RemoveRemote(string toplevel, string name) =>
        ["-C", toplevel, "remote", "remove", name];

    public static IReadOnlyList<string> RenameRemote(string toplevel, string name, string newName) =>
        ["-C", toplevel, "remote", "rename", name, newName];

    public static IReadOnlyList<string> NameStatus(string toplevel, string sha) =>
        ["-C", toplevel, "--no-optional-locks", "show", "-z", "--format=", "--name-status", sha];

    public static IReadOnlyList<string> DiffUnstaged(string toplevel, string path, bool ignoreWhitespace = false) =>
        DiffWorktree(toplevel, staged: false, ignoreWhitespace, path);

    public static IReadOnlyList<string> DiffUntracked(string toplevel, string path, bool ignoreWhitespace = false)
    {
        var arguments = new List<string> { "-C", toplevel, "--no-optional-locks", "diff", "--no-index" };
        if (ignoreWhitespace)
            arguments.Add("-w");
        arguments.Add("--");
        arguments.Add("/dev/null");
        arguments.Add(path);
        return arguments;
    }

    public static IReadOnlyList<string> DiffStaged(string toplevel, string path, bool ignoreWhitespace = false) =>
        DiffWorktree(toplevel, staged: true, ignoreWhitespace, path);

    public static IReadOnlyList<string> DiffWorktree(string toplevel, bool staged, bool ignoreWhitespace, string? path = null)
    {
        var arguments = new List<string> { "-C", toplevel, "--no-optional-locks", "diff" };
        if (staged)
            arguments.Add("--cached");
        if (ignoreWhitespace)
            arguments.Add("-w");
        if (!string.IsNullOrEmpty(path))
        {
            arguments.Add("--");
            arguments.Add(path);
        }

        return arguments;
    }

    public static IReadOnlyList<string> DiffRange(string toplevel, string older, string newer, string? path, bool ignoreWhitespace = false)
    {
        var arguments = new List<string> { "-C", toplevel, "--no-optional-locks", "diff" };
        if (ignoreWhitespace)
            arguments.Add("-w");
        arguments.Add(older);
        arguments.Add(newer);
        if (!string.IsNullOrEmpty(path))
        {
            arguments.Add("--");
            arguments.Add(path);
        }

        return arguments;
    }

    public static IReadOnlyList<string> RangeNameStatus(string toplevel, string older, string newer) =>
        ["-C", toplevel, "--no-optional-locks", "diff", "-z", "--name-status", older, newer];

    public static IReadOnlyList<string> ShowPatch(string toplevel, string sha, string? path, bool ignoreWhitespace = false)
    {
        var arguments = new List<string> { "-C", toplevel, "--no-optional-locks", "show" };
        if (ignoreWhitespace)
            arguments.Add("-w");
        arguments.Add("--format=");
        arguments.Add("-p");
        arguments.Add(sha);
        if (!string.IsNullOrEmpty(path))
        {
            arguments.Add("--");
            arguments.Add(path);
        }

        return arguments;
    }

    public static IReadOnlyList<string> ShowStage(string toplevel, int stage, string path) =>
        ["-C", toplevel, "--no-optional-locks", "show", ":" + stage.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + path];

    public static IReadOnlyList<string> Stage(string toplevel, string path) =>
        ["-C", toplevel, "add", "--", path];

    public static IReadOnlyList<string> StageAll(string toplevel) =>
        ["-C", toplevel, "add", "-A"];

    public static IReadOnlyList<string> StagePaths(string toplevel, IReadOnlyList<string> paths)
    {
        var arguments = new List<string> { "-C", toplevel, "add", "--" };
        arguments.AddRange(paths);
        return arguments;
    }

    public static IReadOnlyList<string> Unstage(string toplevel, string path) =>
        ["-C", toplevel, "restore", "--staged", "--", path];

    public static IReadOnlyList<string> UnstageAll(string toplevel) =>
        ["-C", toplevel, "restore", "--staged", ":"];

    /// <summary>
    /// Unstage every index entry before the first commit. restore --staged cannot resolve HEAD.
    /// </summary>
    public static IReadOnlyList<string> UnstageAllUnborn(string toplevel) =>
        ["-C", toplevel, "rm", "-r", "--cached", "-f", "--", "."];

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

    public static IReadOnlyList<string> Commit(string toplevel, string messageFile, bool noVerify = false)
    {
        var arguments = new List<string> { "-C", toplevel, "commit" };
        if (noVerify)
            arguments.Add("--no-verify");
        arguments.Add("-F");
        arguments.Add(messageFile);
        return arguments;
    }

    public static IReadOnlyList<string> Switch(string toplevel, string branch) =>
        ["-C", toplevel, "switch", branch];

    public static IReadOnlyList<string> SwitchTrack(string toplevel, string remoteBranch) =>
        ["-C", toplevel, "switch", "--track", remoteBranch];

    public static IReadOnlyList<string> CreateBranch(string toplevel, string name) =>
        ["-C", toplevel, "switch", "-c", name];

    public static IReadOnlyList<string> DeleteBranch(string toplevel, string name) =>
        ["-C", toplevel, "branch", "-d", name];

    public static IReadOnlyList<string> ForceDeleteBranch(string toplevel, string name) =>
        ["-C", toplevel, "branch", "-D", name];

    public static IReadOnlyList<string> SetUpstream(string toplevel, string branch, string upstream) =>
        ["-C", toplevel, "branch", "--set-upstream-to=" + upstream, branch];

    public static IReadOnlyList<string> Merge(string toplevel, string branch) =>
        ["-C", toplevel, "merge", "--no-edit", branch];

    public static IReadOnlyList<string> AbortMerge(string toplevel) =>
        ["-C", toplevel, "merge", "--abort"];

    public static IReadOnlyList<string> Fetch(string toplevel) =>
        ["-C", toplevel, "fetch", "--progress"];

    public static IReadOnlyList<string> Pull(string toplevel) =>
        ["-C", toplevel, "pull", "--rebase", "--progress", "--no-edit"];

    public static IReadOnlyList<string> Push(string toplevel, bool noVerify = false)
    {
        var arguments = new List<string> { "-C", toplevel, "push", "--progress" };
        if (noVerify)
            arguments.Add("--no-verify");
        return arguments;
    }

    public static IReadOnlyList<string> PushUpstream(string toplevel, string remote, string branch, bool noVerify = false)
    {
        var arguments = new List<string> { "-C", toplevel, "push", "--progress" };
        if (noVerify)
            arguments.Add("--no-verify");
        arguments.Add("-u");
        arguments.Add(remote);
        arguments.Add(branch);
        return arguments;
    }

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
