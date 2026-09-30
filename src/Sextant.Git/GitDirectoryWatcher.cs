namespace Sextant.Git;

public sealed class GitDirectoryWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly Action _callback;
    private int _disposed;

    public GitDirectoryWatcher(string gitDirectory, Action callback)
    {
        _callback = callback;
        _timer = new Timer(Fire, null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(gitDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.Size
                | NotifyFilters.CreationTime
                | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler onEvent = (_, args) => Queue(args.FullPath);
        _watcher.Changed += onEvent;
        _watcher.Created += onEvent;
        _watcher.Deleted += onEvent;
        _watcher.Renamed += (_, args) =>
        {
            if (IsRelevant(args.FullPath) || IsRelevant(args.OldFullPath))
                _timer.Change(200, Timeout.Infinite);
        };
        _watcher.Error += (_, _) => Failed?.Invoke();
    }

    public event Action? Failed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _timer.Dispose();
    }

    private void Queue(string? path)
    {
        if (IsRelevant(path))
            _timer.Change(200, Timeout.Infinite);
    }

    private void Fire(object? _)
    {
        if (_disposed == 1)
            return;
        try
        {
            _callback();
        }
        catch (Exception)
        {
        }
    }

    private static bool IsRelevant(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        var name = Path.GetFileName(path);
        if (name.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
            || name.Equals("index", StringComparison.OrdinalIgnoreCase)
            || name.Equals("packed-refs", StringComparison.OrdinalIgnoreCase)
            || name.Equals("MERGE_HEAD", StringComparison.OrdinalIgnoreCase))
            return true;
        return path.Replace('\\', '/').Contains("/refs/", StringComparison.Ordinal);
    }
}
