namespace Sextant;

/// <summary>
/// Builds the clipboard text for a selection that can cross lines.
/// A null entry is a row that is not part of this column. An empty string is a blank line.
/// </summary>
public static class LineCopy
{
    public static string Join(
        IReadOnlyList<string?> lines,
        int anchorLine,
        int anchorChar,
        int focusLine,
        int focusChar,
        IReadOnlyList<bool>? continues = null)
    {
        if (!Ordered(lines, ref anchorLine, ref anchorChar, ref focusLine, ref focusChar))
            return "";

        var builder = new System.Text.StringBuilder();
        var any = false;
        for (var line = anchorLine; line <= focusLine; line++)
        {
            var text = lines[line];
            if (text is null)
                continue;
            var start = line == anchorLine ? Math.Clamp(anchorChar, 0, text.Length) : 0;
            var end = line == focusLine ? Math.Clamp(focusChar, 0, text.Length) : text.Length;
            if (end < start)
                end = start;
            var soft = continues is not null && (uint)line < (uint)continues.Count && continues[line];
            if (any && !soft)
                builder.Append('\n');
            any = true;
            builder.Append(text, start, end - start);
        }

        return builder.ToString();
    }

    public static (int Start, int End)? Slice(
        IReadOnlyList<string?> lines,
        int anchorLine,
        int anchorChar,
        int focusLine,
        int focusChar,
        int line)
    {
        if (!Ordered(lines, ref anchorLine, ref anchorChar, ref focusLine, ref focusChar))
            return null;
        if (line < anchorLine || line > focusLine)
            return null;
        var text = lines[line];
        if (text is null)
            return null;
        var start = line == anchorLine ? Math.Clamp(anchorChar, 0, text.Length) : 0;
        var end = line == focusLine ? Math.Clamp(focusChar, 0, text.Length) : text.Length;
        if (end < start)
            end = start;
        if (start == end)
            return null;
        return (start, end);
    }

    private static bool Ordered(IReadOnlyList<string?> lines, ref int anchorLine, ref int anchorChar, ref int focusLine, ref int focusChar)
    {
        if (lines.Count == 0)
            return false;
        if (focusLine < anchorLine || (focusLine == anchorLine && focusChar < anchorChar))
        {
            (anchorLine, focusLine) = (focusLine, anchorLine);
            (anchorChar, focusChar) = (focusChar, anchorChar);
        }

        if (focusLine < 0 || anchorLine >= lines.Count)
            return false;
        if (anchorLine < 0)
            anchorLine = 0;
        if (focusLine >= lines.Count)
            focusLine = lines.Count - 1;
        return anchorLine < focusLine || anchorChar != focusChar;
    }
}
