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
