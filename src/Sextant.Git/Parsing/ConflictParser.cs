using System.Text;

namespace Sextant.Git.Parsing;

public sealed record ConflictPiece(bool IsConflict, string Context, string Ours, string Theirs, string? Base, string Result)
{
    public static ConflictPiece FromContext(string text) => new(false, text, "", "", null, "");
}

public sealed record ConflictDocument(bool IsBinary, bool IsTooLarge, bool Synthetic, IReadOnlyList<ConflictPiece> Pieces)
{
    public static ConflictDocument Binary { get; } = new(true, false, false, []);

    public static ConflictDocument TooLarge { get; } = new(false, true, false, []);
}

/// <summary>
/// Splits a working-tree file on git conflict markers and keeps the original line endings.
/// An unclosed start marker stays ordinary text.
/// </summary>
public static class ConflictParser
{
    public static IReadOnlyList<ConflictPiece> Parse(string text)
    {
        var pieces = new List<ConflictPiece>();
        var context = new StringBuilder();
        var index = 0;
        while (index < text.Length)
        {
            var line = ReadLine(text, index);
            if (IsStart(text, line))
            {
                if (TryReadConflict(text, index, out var conflict, out var next))
                {
                    Flush();
                    pieces.Add(conflict);
                    index = next;
                    continue;
                }

                // No complete conflict remains after this start, so the rest is context.
                context.Append(text, index, text.Length - index);
                break;
            }

            context.Append(text, index, line.Next - index);
            index = line.Next;
        }

        Flush();
        return pieces;

        void Flush()
        {
            if (context.Length == 0)
                return;
            pieces.Add(ConflictPiece.FromContext(context.ToString()));
            context.Clear();
        }
    }

    public static string Compose(IReadOnlyList<ConflictPiece> pieces)
    {
        var builder = new StringBuilder();
        foreach (var piece in pieces)
            builder.Append(piece.IsConflict ? piece.Result : piece.Context);
        return builder.ToString();
    }

    public static bool ContainsMarkers(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var line = ReadLine(text, index);
            if (IsStart(text, line))
                return true;
            index = line.Next;
        }

        return false;
    }

    private static bool TryReadConflict(string text, int start, out ConflictPiece piece, out int next)
    {
        piece = ConflictPiece.FromContext("");
        next = start;
        var startLine = ReadLine(text, start);
        if (!IsStart(text, startLine))
            return false;

        var oursStart = startLine.Next;
        int? baseLine = null;
        int? baseContent = null;
        var separator = -1;
        var cursor = startLine.Next;
        while (cursor < text.Length)
        {
            var line = ReadLine(text, cursor);
            if (baseLine is null && separator < 0 && StartsWith(text, line, "||||||| "))
            {
                baseLine = cursor;
                baseContent = line.Next;
                cursor = line.Next;
                continue;
            }

            if (separator < 0 && IsExact(text, line, "======="))
            {
                separator = cursor;
                cursor = line.Next;
                break;
            }

            cursor = line.Next;
        }

        if (separator < 0)
            return false;

        var theirsStart = cursor;
        var end = -1;
        while (cursor < text.Length)
        {
            var line = ReadLine(text, cursor);
            if (StartsWith(text, line, ">>>>>>> "))
            {
                end = cursor;
                next = line.Next;
                break;
            }

            cursor = line.Next;
        }

        if (end < 0)
            return false;

        var oursEnd = baseLine ?? separator;
        var ours = text.Substring(oursStart, oursEnd - oursStart);
        string? baseText = null;
        if (baseContent is int content)
            baseText = text.Substring(content, separator - content);
        var theirs = text.Substring(theirsStart, end - theirsStart);
        var result = text.Substring(start, next - start);
        piece = new ConflictPiece(true, "", ours, theirs, baseText, result);
        return true;
    }

    private readonly record struct Line(int Start, int ContentEnd, int Next);

    private static Line ReadLine(string text, int start)
    {
        var index = start;
        while (index < text.Length && text[index] != '\n' && text[index] != '\r')
            index++;
        var contentEnd = index;
        if (index < text.Length && text[index] == '\r')
            index++;
        if (index < text.Length && text[index] == '\n')
            index++;
        return new Line(start, contentEnd, index);
    }

    private static bool IsStart(string text, Line line) => StartsWith(text, line, "<<<<<<< ");

    private static bool StartsWith(string text, Line line, string prefix)
    {
        if (line.ContentEnd - line.Start < prefix.Length)
            return false;
        return text.AsSpan(line.Start, prefix.Length).SequenceEqual(prefix);
    }

    private static bool IsExact(string text, Line line, string value)
    {
        if (line.ContentEnd - line.Start != value.Length)
            return false;
        return text.AsSpan(line.Start, value.Length).SequenceEqual(value);
    }
}
