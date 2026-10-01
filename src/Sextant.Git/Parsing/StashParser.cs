using System.Text;

namespace Sextant.Git.Parsing;

public static class StashParser
{
    public static IReadOnlyList<StashEntry> Parse(byte[] data, Encoding encoding) =>
        Parse(encoding.GetString(data));

    public static IReadOnlyList<StashEntry> Parse(string text)
    {
        var entries = new List<StashEntry>();
        foreach (var record in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Split('\u001f');
            if (fields.Length < 3 || fields[0].Length == 0 || fields[1].Length == 0)
                continue;
            entries.Add(new StashEntry(fields[0], fields[1], fields[2]));
        }

        return entries;
    }
}
