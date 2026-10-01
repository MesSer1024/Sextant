namespace Sextant.Git;

public enum ChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied,
    Untracked,
    Unmerged,
    TypeChanged,
}

public sealed record BranchHeader(
    string? Oid,
    string? HeadName,
    bool Detached,
    bool Unborn,
    string? Upstream,
    int Ahead,
    int Behind);

public sealed record StatusEntry(
    string Path,
    string? OriginalPath,
    ChangeKind Kind,
    bool Staged,
    bool Unstaged,
    char IndexStatus,
    char WorkTreeStatus);

public sealed record StatusSnapshot(BranchHeader Branch, IReadOnlyList<StatusEntry> Entries);

public sealed record CommitRecord(
    string Sha,
    IReadOnlyList<string> Parents,
    long AuthorUnixSeconds,
    string AuthorName,
    string AuthorEmail,
    string Subject);

public sealed record GitRef(string Oid, string Name, bool IsHead, string? Upstream);

public sealed record CommitFileChange(string Path, string? OriginalPath, ChangeKind Kind);

public sealed class GraphCommit
{
    public required CommitRecord Commit { get; init; }

    public required LaneGeometry Lanes { get; init; }
}

public readonly record struct LaneEdge(int From, int To);

public sealed class LaneGeometry
{
    public required int NodeLane { get; init; }

    public required int LaneCount { get; init; }

    public required IReadOnlyList<int> IncomingLanes { get; init; }

    public required IReadOnlyList<int> ThroughLanes { get; init; }

    public required IReadOnlyList<LaneEdge> Edges { get; init; }
}

public enum DiffLineKind
{
    Context,
    Added,
    Removed,
    Meta,
}

public sealed record DiffLine(DiffLineKind Kind, string Text);

public sealed record DiffHunk(
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    string Header,
    IReadOnlyList<DiffLine> Lines);

public sealed record DiffDocument(
    bool IsBinary,
    bool IsNewFile,
    bool IsDeleted,
    bool IsRename,
    bool IsTooLarge,
    IReadOnlyList<DiffHunk> Hunks,
    string RawPatch)
{
    public int LineCount => Hunks.Sum(hunk => hunk.Lines.Count);

    public static DiffDocument Empty { get; } = new(false, false, false, false, false, [], "");

    public static DiffDocument TooLarge { get; } = new(false, false, false, false, true, [], "");

    public static DiffDocument Binary { get; } = new(true, false, false, false, false, [], "");
}

public enum SequencerKind
{
    None,
    Merge,
    CherryPick,
    Revert,
}

public sealed record BlameLine(int Number, string Sha, string Author, string Summary, string Text, bool Uncommitted);

public sealed record BlameDocument(bool IsTooLarge, IReadOnlyList<BlameLine> Lines)
{
    public static BlameDocument TooLarge { get; } = new(true, []);
}

public sealed record StashEntry(string Ref, string Sha, string Subject);

public sealed record CommandLogEntry(
    DateTimeOffset At,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    TimeSpan Duration,
    string StandardError);

public sealed record RepoBadge(
    bool Available,
    string? Branch,
    bool Detached,
    bool Dirty,
    bool Conflicted,
    int Ahead,
    int Behind,
    string? Error)
{
    public static RepoBadge Failed(string error) => new(false, null, false, false, false, 0, 0, error);
}

public readonly record struct PerformanceSuggestion(bool ManyFiles, bool FileSystemMonitor);

public sealed class SessionState
{
    public required BranchHeader Branch { get; init; }

    public required IReadOnlyList<StatusEntry> Entries { get; init; }

    public required IReadOnlyList<GitRef> Refs { get; init; }

    public required IReadOnlyList<GraphCommit> Commits { get; init; }

    public required bool HistoryEnded { get; init; }

    public required bool HistoryCapped { get; init; }

    public required int HistoryGeneration { get; init; }

    public required bool MergeInProgress { get; init; }

    public required SequencerKind Sequencer { get; init; }

    public required string? MergeMessage { get; init; }

    public required string? HistoryLabel { get; init; }

    public required IReadOnlyList<StashEntry> Stashes { get; init; }

    public required TimeSpan LastStatusDuration { get; init; }

    public required IReadOnlyDictionary<string, string> Config { get; init; }

    public required IReadOnlyList<string> Remotes { get; init; }

    public required IReadOnlyList<CommandLogEntry> Commands { get; init; }

    public required PerformanceSuggestion? Suggestion { get; init; }
}

public static class HistoryLimits
{
    public const int FirstPage = 300;

    public const int Page = 500;

    public const int SoftCap = 50_000;

    public const int MaxDiffBytes = 1_000_000;

    public const int MaxDiffLines = 20_000;
}
