namespace Sextant.Git.Parsing;

public static class BlameParser
{
    public static IReadOnlyList<BlameLine> Parse(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = new List<BlameLine>();
        string? sha = null;
        string author = "";
        string summary = "";
        var number = 0;
        foreach (var line in normalized.Split('\n'))
        {
            if (line.Length == 0)
                continue;
            if (line[0] == '\t')
            {
                if (sha is null)
                    continue;
                var uncommitted = sha.All(character => character == '0');
                lines.Add(new BlameLine(number, sha, author, summary, line[1..], uncommitted));
                sha = null;
                author = "";
                summary = "";
                continue;
            }

            if (sha is null && TryHeader(line, out var headerSha, out var finalLine))
            {
                sha = headerSha;
                number = finalLine;
                continue;
            }

            if (sha is null)
                continue;
            if (line.StartsWith("author ", StringComparison.Ordinal))
                author = line["author ".Length..];
            else if (line.StartsWith("summary ", StringComparison.Ordinal))
                summary = line["summary ".Length..];
        }

        return lines;
    }

    private static bool TryHeader(string line, out string sha, out int finalLine)
    {
        sha = "";
        finalLine = 0;
        var parts = line.Split(' ');
        if (parts.Length < 3)
            return false;
        if (parts[0].Length < 4 || !parts[0].All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
            return false;
        if (!int.TryParse(parts[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out finalLine))
            return false;
        sha = parts[0];
        return true;
    }
}
