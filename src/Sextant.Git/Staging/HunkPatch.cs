namespace Sextant.Git;

public static class HunkPatch
{
    public static string Slice(string patch, int hunkIndex)
    {
        var normalized = patch.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var starts = new List<int>();
        for (var i = 0; i < normalized.Length; i++)
        {
            if ((i == 0 || normalized[i - 1] == '\n')
                && i + 1 < normalized.Length
                && normalized[i] == '@'
                && normalized[i + 1] == '@')
                starts.Add(i);
        }

        if (hunkIndex < 0 || hunkIndex >= starts.Count)
            throw new ArgumentOutOfRangeException(nameof(hunkIndex), "That hunk is not in the patch.");

        var header = normalized[..starts[0]];
        var end = hunkIndex + 1 < starts.Count ? starts[hunkIndex + 1] : normalized.Length;
        var body = normalized[starts[hunkIndex]..end];
        var result = header + body;
        if (!result.EndsWith('\n'))
            result += "\n";
        return result;
    }
}
