using System.Text.RegularExpressions;

namespace Sextant.Git;

/// <summary>
/// Builds a one-change patch from a single added or removed line inside a hunk.
/// Unselected deletions stay as context. Unselected additions are left out.
/// </summary>
public static partial class LinePatch
{
    public static string? Slice(string patch, int hunkIndex, int lineIndex)
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
        var hunkLines = body.Split('\n');
        if (hunkLines.Length == 0)
            return null;

        var hunkHeader = hunkLines[0];
        var match = HunkPattern().Match(hunkHeader);
        if (!match.Success)
            return null;
        if (lineIndex < 0 || lineIndex + 1 >= hunkLines.Length)
            return null;

        var chosen = hunkLines[lineIndex + 1];
        if (chosen.Length == 0 || (chosen[0] != '+' && chosen[0] != '-'))
            return null;

        var suffix = match.Groups[5].Value;
        var oldStart = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var newStartText = match.Groups[3].Value;
        var newStart = newStartText.Length == 0
            ? 0
            : int.Parse(newStartText, System.Globalization.CultureInfo.InvariantCulture);
        var rebuilt = new List<string>();
        var oldCount = 0;
        var newCount = 0;
        var changed = false;
        for (var i = 1; i < hunkLines.Length; i++)
        {
            var line = hunkLines[i];
            if (line.Length == 0 && i == hunkLines.Length - 1)
                continue;
            if (line.StartsWith("\\", StringComparison.Ordinal))
            {
                if (rebuilt.Count > 0)
                    rebuilt.Add(line);
                continue;
            }

            var index = i - 1;
            var marker = line.Length == 0 ? ' ' : line[0];
            var selected = index == lineIndex;
            if (marker == '+' && !selected)
                continue;
            if (marker == '-' && !selected)
            {
                rebuilt.Add(" " + (line.Length > 0 ? line[1..] : ""));
                oldCount++;
                newCount++;
                continue;
            }

            if (marker is '+' or '-' or ' ' || line.Length == 0)
            {
                var text = marker is '+' or '-' or ' ' ? line : " " + line;
                if (line.Length == 0)
                    text = " ";
                rebuilt.Add(text);
                if (text[0] != '+')
                    oldCount++;
                if (text[0] != '-')
                    newCount++;
                if (text[0] is '+' or '-')
                    changed = true;
                continue;
            }
        }

        if (!changed)
            return null;

        var result = header
            + FormattableString.Invariant($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@{suffix}")
            + "\n"
            + string.Join('\n', rebuilt)
            + "\n";
        return result;
    }

    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex HunkPattern();
}
