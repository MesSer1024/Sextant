namespace Sextant.Git;

public sealed class RequestGate
{
    private int _current;

    public int Next() => Interlocked.Increment(ref _current);

    public bool IsCurrent(int token) => Volatile.Read(ref _current) == token;
}
