namespace Sextant.Git.Parsing;

public static class SubmoduleParser
{
    public static IReadOnlyList<SubmoduleEntry> Parse(string text)
    {
        var entries = new List<SubmoduleEntry>();
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var raw in normalized.Split('\n'))
        {
            if (raw.Length < 3)
                continue;
            var state = raw[0] switch
            {
                '-' => SubmoduleState.Uninitialized,
                '+' => SubmoduleState.Modified,
                'U' => SubmoduleState.Conflict,
                _ => SubmoduleState.Matches,
            };
            var rest = raw[1..];
            var space = rest.IndexOf(' ');
            if (space <= 0)
                continue;
            var sha = rest[..space];
            if (!IsHex(sha))
                continue;
            var tail = rest[(space + 1)..];
            string? describe = null;
            var paren = tail.LastIndexOf(" (", StringComparison.Ordinal);
            if (paren >= 0 && tail.EndsWith(')'))
            {
                describe = tail[(paren + 2)..^1];
                tail = tail[..paren];
            }

            if (tail.Length == 0)
                continue;
            entries.Add(new SubmoduleEntry(tail, sha, describe, state));
        }

        return entries;
    }

    private static bool IsHex(string text)
    {
        if (text.Length < 7)
            return false;
        foreach (var character in text)
        {
            var hex = character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!hex)
                return false;
        }

        return true;
    }
}
