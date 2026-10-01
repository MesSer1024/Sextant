using System.Text;

namespace Sextant.Git.Parsing;

public static class LfsPointers
{
    private static ReadOnlySpan<byte> Prefix => "version https://git-lfs.github.com/spec/v1"u8;

    public static bool TryParse(string text, out LfsPointer? pointer)
    {
        pointer = null;
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (!normalized.StartsWith("version https://git-lfs.github.com/spec/v1", StringComparison.Ordinal))
            return false;
        var firstBreak = normalized.IndexOf('\n');
        var firstLine = firstBreak < 0 ? normalized : normalized[..firstBreak];
        if (!firstLine.Equals("version https://git-lfs.github.com/spec/v1", StringComparison.Ordinal))
            return false;

        string? oid = null;
        long? size = null;
        foreach (var line in normalized.Split('\n'))
        {
            if (line.StartsWith("oid ", StringComparison.Ordinal))
                oid = line[4..].Trim();
            else if (line.StartsWith("size ", StringComparison.Ordinal)
                && long.TryParse(line[5..].Trim(), out var parsed)
                && parsed >= 0)
                size = parsed;
        }

        if (oid is not { Length: >= 7 } || size is null)
            return false;
        pointer = new LfsPointer(oid, size.Value);
        return true;
    }

    public static bool TryParseBytes(ReadOnlySpan<byte> data, out LfsPointer? pointer)
    {
        pointer = null;
        if (!data.StartsWith(Prefix))
            return false;
        return TryParse(Encoding.UTF8.GetString(data), out pointer);
    }

    public static bool TryReadDiff(DiffDocument document, out LfsPointer? before, out LfsPointer? after)
    {
        before = null;
        after = null;
        var oldText = new StringBuilder();
        var newText = new StringBuilder();
        var any = false;
        foreach (var hunk in document.Hunks)
        {
            foreach (var line in hunk.Lines)
            {
                if (line.Kind == DiffLineKind.Meta)
                    continue;
                any = true;
                if (line.Kind != DiffLineKind.Added)
                    oldText.Append(line.Text).Append('\n');
                if (line.Kind != DiffLineKind.Removed)
                    newText.Append(line.Text).Append('\n');
            }
        }

        if (!any)
            return false;
        var oldOk = TryParse(oldText.ToString(), out before);
        var newOk = TryParse(newText.ToString(), out after);
        if (!oldOk)
            before = null;
        if (!newOk)
            after = null;
        return oldOk || newOk;
    }
}
