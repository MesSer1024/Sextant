namespace Sextant;

/// <summary>
/// Splits one logical line into rows the diff list can virtualize.
/// One text layout of a few hundred thousand characters stalls the window.
/// </summary>
public static class LineFold
{
    public const int Columns = 256;

    public static int Count(string text)
    {
        if (text.Length == 0)
            return 1;
        return (text.Length + Columns - 1) / Columns;
    }

    /// <summary>
    /// The text for one row, or null when this row is only padding for the other column.
    /// </summary>
    public static string? Piece(string text, int index)
    {
        if (index < 0)
            return null;
        if (text.Length == 0)
            return index == 0 ? "" : null;
        var start = index * Columns;
        if ((uint)start >= (uint)text.Length)
            return null;
        var length = Math.Min(Columns, text.Length - start);
        return text.Substring(start, length);
    }
}
