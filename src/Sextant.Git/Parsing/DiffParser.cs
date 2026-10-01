using System.Globalization;
using System.Text.RegularExpressions;

namespace Sextant.Git.Parsing;

public static partial class DiffParser
{
    public static DiffDocument Parse(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.Contains("GIT binary patch", StringComparison.Ordinal)
            || normalized.Contains("Binary files ", StringComparison.Ordinal))
            return new DiffDocument(true, false, false, false, false, [], normalized);

        var isNew = ContainsLine(normalized, "new file mode ");
        var isDeleted = ContainsLine(normalized, "deleted file mode ");
        var isRename = ContainsLine(normalized, "rename from ");
        var hunks = new List<DiffHunk>();
        DiffHunkBuilder? current = null;
        foreach (var line in normalized.Split('\n'))
        {
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                if (current is not null)
                    hunks.Add(current.Build());
                current = DiffHunkBuilder.Parse(line);
                continue;
            }

            if (current is null)
                continue;
            if (line.StartsWith("\\", StringComparison.Ordinal))
                current.Add(DiffLineKind.Meta, line);
            else if (line.StartsWith("+", StringComparison.Ordinal))
                current.Add(DiffLineKind.Added, line.Length > 0 ? line[1..] : "");
            else if (line.StartsWith("-", StringComparison.Ordinal))
                current.Add(DiffLineKind.Removed, line.Length > 0 ? line[1..] : "");
            else if (line.StartsWith(" ", StringComparison.Ordinal) || line.Length == 0)
                current.Add(DiffLineKind.Context, line.Length > 0 ? line[1..] : "");
            else
            {
                hunks.Add(current.Build());
                current = null;
            }
        }

        if (current is not null)
            hunks.Add(current.Build());
        return new DiffDocument(false, isNew, isDeleted, isRename, false, hunks, normalized);
    }

    public readonly record struct DiffFile(string Path, DiffDocument Document);

    public static IReadOnlyList<DiffFile> ParseFiles(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.Length == 0)
            return [];
        const string marker = "diff --git ";
        var indexes = new List<int>();
        var start = 0;
        while (start < normalized.Length)
        {
            int at;
            if (start == 0 && normalized.StartsWith(marker, StringComparison.Ordinal))
                at = 0;
            else
            {
                var found = normalized.IndexOf("\n" + marker, start, StringComparison.Ordinal);
                if (found < 0)
                    break;
                at = found + 1;
            }

            indexes.Add(at);
            start = at + marker.Length;
        }

        if (indexes.Count == 0)
            return [new DiffFile("", Parse(normalized))];

        var files = new List<DiffFile>(indexes.Count);
        for (var i = 0; i < indexes.Count; i++)
        {
            var end = i + 1 < indexes.Count ? indexes[i + 1] : normalized.Length;
            var slice = normalized[indexes[i]..end];
            files.Add(new DiffFile(PathFromHeader(slice), Parse(slice)));
        }

        return files;
    }

    private static string PathFromHeader(string slice)
    {
        var lineEnd = slice.IndexOf('\n');
        var line = lineEnd < 0 ? slice : slice[..lineEnd];
        const string marker = " b/";
        var at = line.LastIndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            return line;
        var path = line[(at + marker.Length)..];
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
            path = path[1..^1];
        return path;
    }

    private static bool ContainsLine(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.Ordinal) || text.Contains("\n" + prefix, StringComparison.Ordinal);

    private sealed class DiffHunkBuilder
    {
        private readonly int _oldStart;
        private readonly int _oldCount;
        private readonly int _newStart;
        private readonly int _newCount;
        private readonly string _header;
        private readonly List<DiffLine> _lines = [];

        private DiffHunkBuilder(int oldStart, int oldCount, int newStart, int newCount, string header)
        {
            _oldStart = oldStart;
            _oldCount = oldCount;
            _newStart = newStart;
            _newCount = newCount;
            _header = header;
        }

        public static DiffHunkBuilder Parse(string header)
        {
            var match = HunkPattern().Match(header);
            if (!match.Success)
                return new DiffHunkBuilder(0, 0, 0, 0, header);
            return new DiffHunkBuilder(
                ParseCount(match.Groups[1].Value, 0),
                ParseCount(match.Groups[2].Value, 1),
                ParseCount(match.Groups[3].Value, 0),
                ParseCount(match.Groups[4].Value, 1),
                header);
        }

        public void Add(DiffLineKind kind, string text) => _lines.Add(new DiffLine(kind, text));

        public DiffHunk Build() => new(_oldStart, _oldCount, _newStart, _newCount, _header, _lines);

        private static int ParseCount(string text, int fallback) =>
            text.Length == 0 ? fallback : int.Parse(text, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@", RegexOptions.CultureInvariant)]
    private static partial Regex HunkPattern();
}
