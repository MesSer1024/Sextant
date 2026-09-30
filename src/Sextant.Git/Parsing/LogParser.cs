using System.Globalization;
using System.Text;

namespace Sextant.Git.Parsing;

public static class LogParser
{
    public static IReadOnlyList<CommitRecord> Parse(byte[] data, Encoding encoding) =>
        Parse(encoding.GetString(data));

    public static IReadOnlyList<CommitRecord> Parse(string text)
    {
        var commits = new List<CommitRecord>();
        foreach (var record in text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Split('\u001f');
            if (fields.Length < 6 || fields[0].Length == 0)
                continue;
            if (!long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
                unix = 0;
            var parents = fields[1].Length == 0
                ? []
                : fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            commits.Add(new CommitRecord(fields[0], parents, unix, fields[3], fields[4], fields[5]));
        }

        return commits;
    }

    public static bool IsUnborn(string standardError) =>
        standardError.Contains("does not have any commits yet", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("ambiguous argument 'HEAD'", StringComparison.OrdinalIgnoreCase);
}
