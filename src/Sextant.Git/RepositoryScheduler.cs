namespace Sextant.Git;

public sealed class RepositoryScheduler : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _readSlots = new(2, 2);
    private readonly SemaphoreSlim _write = new(1, 1);
    private CancellationTokenSource _epoch = new();
    private int _activeReads;
    private int _writeHolders;
    private TaskCompletionSource<bool> _readsDone = Completed();
    private TaskCompletionSource<bool> _noWriters = Completed();
    private bool _disposed;

    public async Task<T> ReadAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task wait;
            CancellationToken epoch;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_writeHolders > 0)
                {
                    wait = _noWriters.Task;
                    epoch = default;
                }
                else
                {
                    wait = Task.CompletedTask;
                    if (++_activeReads == 1)
                        _readsDone = NewSource();
                    epoch = _epoch.Token;
                }
            }

            if (!wait.IsCompletedSuccessfully)
            {
                await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, epoch);
            var acquired = false;
            try
            {
                await _readSlots.WaitAsync(linked.Token).ConfigureAwait(false);
                acquired = true;
                return await work(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                if (acquired)
                    _readSlots.Release();
                lock (_gate)
                {
                    if (--_activeReads == 0)
                        _readsDone.TrySetResult(true);
                }
            }
        }
    }

    public async Task<T> WriteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        Task idle;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _writeHolders++;
            if (_writeHolders == 1)
                _noWriters = NewSource();
            _epoch.Cancel();
            idle = _activeReads == 0 ? Task.CompletedTask : _readsDone.Task;
        }

        var acquired = false;
        try
        {
            await idle.WaitAsync(cancellationToken).ConfigureAwait(false);
            await _write.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            return await work(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (acquired)
                _write.Release();
            lock (_gate)
            {
                _writeHolders--;
                if (_writeHolders == 0)
                {
                    _epoch = new CancellationTokenSource();
                    _noWriters.TrySetResult(true);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _epoch.Cancel();
        }

        _readSlots.Dispose();
        _write.Dispose();
    }

    private static TaskCompletionSource<bool> Completed()
    {
        var source = NewSource();
        source.SetResult(true);
        return source;
    }

    private static TaskCompletionSource<bool> NewSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
