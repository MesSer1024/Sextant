using Sextant.Git;
using Sextant.ViewModels;

namespace Sextant.Services;

public sealed class BadgeWorker
{
    private readonly GitProcessRunner _runner;
    private CancellationTokenSource _delay = new();

    public BadgeWorker(GitProcessRunner runner) => _runner = runner;

    public void Pulse()
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _delay, next);
        previous.Cancel();
        previous.Dispose();
    }

    public async Task RunAsync(
        Func<IReadOnlyList<PinViewModel>> pins,
        Func<RepositoryViewModel?> active,
        Func<string?> git,
        Action<PinViewModel, RepoBadge> apply,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var executable = git();
            if (!string.IsNullOrEmpty(executable))
            {
                foreach (var pin in pins())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var current = active();
                    if (current is not null && (RepoPath.Same(current.RequestedPath, pin.Path)
                        || (current.Toplevel is not null && RepoPath.Same(current.Toplevel, pin.Path))))
                        continue;
                    var badge = await RepositoryAdmin.ReadBadgeAsync(_runner, executable, pin.Path, cancellationToken)
                        .ConfigureAwait(false);
                    apply(pin, badge);
                }
            }

            var wait = _delay;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, wait.Token);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}
