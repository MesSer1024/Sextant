namespace Sextant.Git;

public sealed class LaneAssigner
{
    private readonly List<string?> _expected = [];

    public void Reset() => _expected.Clear();

    public LaneGeometry Assign(CommitRecord commit)
    {
        var incoming = new List<int>();
        for (var i = 0; i < _expected.Count; i++)
        {
            if (_expected[i] == commit.Sha)
                incoming.Add(i);
        }

        var before = _expected.ToArray();
        var node = incoming.Count > 0 ? incoming[0] : FirstFree();
        foreach (var lane in incoming)
            _expected[lane] = null;

        var edges = new List<LaneEdge>();
        if (commit.Parents.Count > 0)
        {
            _expected[node] = commit.Parents[0];
            edges.Add(new LaneEdge(node, node));
            for (var parent = 1; parent < commit.Parents.Count; parent++)
            {
                var lane = IndexOf(commit.Parents[parent]);
                if (lane < 0)
                    lane = FirstFree();
                _expected[lane] = commit.Parents[parent];
                edges.Add(new LaneEdge(node, lane));
            }
        }

        while (_expected.Count > 0 && _expected[^1] is null)
            _expected.RemoveAt(_expected.Count - 1);

        var through = new List<int>();
        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] is not null && before[i] != commit.Sha)
                through.Add(i);
        }

        var laneCount = Math.Max(_expected.Count, node + 1);
        foreach (var lane in incoming)
            laneCount = Math.Max(laneCount, lane + 1);
        foreach (var edge in edges)
            laneCount = Math.Max(laneCount, Math.Max(edge.From, edge.To) + 1);

        return new LaneGeometry
        {
            NodeLane = node,
            LaneCount = laneCount,
            IncomingLanes = incoming,
            ThroughLanes = through,
            Edges = edges,
        };
    }

    private int FirstFree()
    {
        for (var i = 0; i < _expected.Count; i++)
        {
            if (_expected[i] is null)
                return i;
        }

        _expected.Add(null);
        return _expected.Count - 1;
    }

    private int IndexOf(string sha)
    {
        for (var i = 0; i < _expected.Count; i++)
        {
            if (_expected[i] == sha)
                return i;
        }

        return -1;
    }
}
