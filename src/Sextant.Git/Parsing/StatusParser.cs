using System.Globalization;
using System.Text;

namespace Sextant.Git.Parsing;

public static class StatusParser
{
    public static StatusSnapshot Parse(byte[] data) => Parse(Encoding.UTF8.GetString(data));

    public static StatusSnapshot Parse(string text)
    {
        var records = text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        string? oid = null;
        string? head = null;
        string? upstream = null;
        var ahead = 0;
        var behind = 0;
        var entries = new List<StatusEntry>();

        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.StartsWith("# ", StringComparison.Ordinal))
            {
                ReadHeader(record, ref oid, ref head, ref upstream, ref ahead, ref behind);
                continue;
            }

            if (record.StartsWith("? ", StringComparison.Ordinal))
            {
                entries.Add(new StatusEntry(record[2..], null, ChangeKind.Untracked, false, true, '?', '?'));
                continue;
            }

            if (record.StartsWith("! ", StringComparison.Ordinal))
                continue;

            if (record.StartsWith("1 ", StringComparison.Ordinal))
            {
                entries.Add(ParseOrdinary(record));
                continue;
            }

            if (record.StartsWith("2 ", StringComparison.Ordinal))
            {
                var original = i + 1 < records.Length ? records[++i] : null;
                entries.Add(ParseRename(record, original));
                continue;
            }

            if (record.StartsWith("u ", StringComparison.Ordinal))
                entries.Add(ParseUnmerged(record));
        }

        var unborn = oid is null || oid == "(initial)";
        var detached = string.Equals(head, "(detached)", StringComparison.Ordinal);
        var branch = new BranchHeader(
            unborn ? null : oid,
            detached ? null : head,
            detached,
            unborn,
            upstream,
            ahead,
            behind);
        return new StatusSnapshot(branch, entries);
    }

    private static void ReadHeader(
        string record,
        ref string? oid,
        ref string? head,
        ref string? upstream,
        ref int ahead,
        ref int behind)
    {
        const string oidPrefix = "# branch.oid ";
        const string headPrefix = "# branch.head ";
        const string upstreamPrefix = "# branch.upstream ";
        const string abPrefix = "# branch.ab ";
        if (record.StartsWith(oidPrefix, StringComparison.Ordinal))
            oid = record[oidPrefix.Length..];
        else if (record.StartsWith(headPrefix, StringComparison.Ordinal))
            head = record[headPrefix.Length..];
        else if (record.StartsWith(upstreamPrefix, StringComparison.Ordinal))
            upstream = record[upstreamPrefix.Length..];
        else if (record.StartsWith(abPrefix, StringComparison.Ordinal))
        {
            var parts = record[abPrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && int.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedAhead))
                ahead = Math.Abs(parsedAhead);
            if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedBehind))
                behind = Math.Abs(parsedBehind);
        }
    }

    private static StatusEntry ParseOrdinary(string record)
    {
        var fields = record.Split(' ', 9);
        var (index, work) = Codes(fields);
        var path = fields.Length >= 9 ? fields[8] : "";
        return new StatusEntry(path, null, KindOf(index, work), Staged(index), Unstaged(work), index, work);
    }

    private static StatusEntry ParseRename(string record, string? original)
    {
        var fields = record.Split(' ', 10);
        var (index, work) = Codes(fields);
        var path = fields.Length >= 10 ? fields[9] : "";
        return new StatusEntry(path, original, ChangeKind.Renamed, Staged(index), Unstaged(work), index, work);
    }

    private static StatusEntry ParseUnmerged(string record)
    {
        var fields = record.Split(' ', 11);
        var (index, work) = Codes(fields);
        var path = fields.Length >= 11 ? fields[10] : "";
        return new StatusEntry(path, null, ChangeKind.Unmerged, false, false, index, work);
    }

    private static (char Index, char Work) Codes(string[] fields)
    {
        var xy = fields.Length > 1 ? fields[1] : "..";
        var index = xy.Length > 0 ? xy[0] : '.';
        var work = xy.Length > 1 ? xy[1] : '.';
        return (index, work);
    }

    private static bool Staged(char index) => index is not '.' and not ' ';

    private static bool Unstaged(char work) => work is not '.' and not ' ';

    private static ChangeKind KindOf(char index, char work)
    {
        var code = index is '.' or ' ' ? work : index;
        return code switch
        {
            'A' => ChangeKind.Added,
            'D' => ChangeKind.Deleted,
            'R' => ChangeKind.Renamed,
            'C' => ChangeKind.Copied,
            'T' => ChangeKind.TypeChanged,
            'U' => ChangeKind.Unmerged,
            '?' => ChangeKind.Untracked,
            _ => ChangeKind.Modified,
        };
    }
}
