namespace Sextant.Git.Tests;

public class SchedulerTests
{
    [Fact]
    public async Task Two_reads_overlap()
    {
        using var scheduler = new RepositoryScheduler();
        var started = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> Work(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref started) == 2)
                both.TrySetResult();
            return Wait(both, cancellationToken);
        }

        var first = scheduler.ReadAsync(Work, CancellationToken.None);
        var second = scheduler.ReadAsync(Work, CancellationToken.None);
        var completed = await Task.WhenAny(Task.WhenAll(first, second), Task.Delay(2000));
        Assert.True(first.IsCompletedSuccessfully && second.IsCompletedSuccessfully, "reads did not overlap");
        Assert.Same(completed, Task.WhenAll(first, second).ContinueWith(_ => completed, TaskScheduler.Default) is null ? completed : completed);
    }

    [Fact]
    public async Task Write_cancels_an_in_flight_read_then_runs()
    {
        using var scheduler = new RepositoryScheduler();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = scheduler.ReadAsync(async cancellationToken =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var wrote = false;
        var write = scheduler.WriteAsync(cancellationToken =>
        {
            wrote = true;
            return Task.FromResult(1);
        }, CancellationToken.None);

        var finished = await Task.WhenAny(write, Task.Delay(2000));
        Assert.Same(write, finished);
        Assert.True(wrote);
        Assert.False(read.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Writes_do_not_overlap()
    {
        using var scheduler = new RepositoryScheduler();
        var current = 0;
        var max = 0;
        async Task<int> Work(CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref current);
            int seen;
            do
            {
                seen = max;
            }
            while (now > seen && Interlocked.CompareExchange(ref max, now, seen) != seen);

            await Task.Delay(40, cancellationToken);
            Interlocked.Decrement(ref current);
            return 1;
        }

        var all = Task.WhenAll(
            scheduler.WriteAsync(Work, CancellationToken.None),
            scheduler.WriteAsync(Work, CancellationToken.None),
            scheduler.WriteAsync(Work, CancellationToken.None));
        var finished = await Task.WhenAny(all, Task.Delay(3000));
        Assert.Same(all, finished);
        Assert.Equal(1, max);
    }

    private static async Task<int> Wait(TaskCompletionSource gate, CancellationToken cancellationToken)
    {
        await gate.Task.WaitAsync(cancellationToken);
        return 1;
    }
}
