using System.Text;

namespace Sextant.Git.Parsing;

public static class NameStatusParser
{
    public static IReadOnlyList<CommitFileChange> Parse(byte[] data) => Parse(Encoding.UTF8.GetString(data));

    public static IReadOnlyList<CommitFileChange> Parse(string text)
    {
        var parts = text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var changes = new List<CommitFileChange>();
        for (var i = 0; i < parts.Length; i++)
        {
            var code = parts[i].Trim();
            if (code.Length == 0 || i + 1 >= parts.Length)
                continue;
            if (code[0] is 'R' or 'C')
            {
                var oldPath = parts[++i];
                var newPath = i + 1 < parts.Length ? parts[++i] : oldPath;
                var kind = code[0] == 'R' ? ChangeKind.Renamed : ChangeKind.Copied;
                changes.Add(new CommitFileChange(newPath, oldPath, kind));
                continue;
            }

            var path = parts[++i];
            changes.Add(new CommitFileChange(path, null, Kind(code[0])));
        }

        return changes;
    }

    private static ChangeKind Kind(char code) => code switch
    {
        'A' => ChangeKind.Added,
        'D' => ChangeKind.Deleted,
        'T' => ChangeKind.TypeChanged,
        'U' => ChangeKind.Unmerged,
        _ => ChangeKind.Modified,
    };
}
