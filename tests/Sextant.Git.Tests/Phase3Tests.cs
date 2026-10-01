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

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
