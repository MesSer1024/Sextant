using System.Text;

namespace Sextant.Git.Parsing;

public static class RefParser
{
    public static IReadOnlyList<GitRef> Parse(byte[] data, Encoding encoding) =>
        Parse(encoding.GetString(data));

    public static IReadOnlyList<GitRef> Parse(string text)
    {
        var refs = new List<GitRef>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
                continue;
            var fields = line.Split('\t');
            if (fields.Length < 3 || fields[0].Length == 0 || fields[1].Length == 0)
                continue;
            var upstream = fields.Length >= 4 && fields[3].Length > 0 ? fields[3] : null;
            refs.Add(new GitRef(fields[0], fields[1], fields[2].Contains('*'), upstream));
        }

        return refs;
    }
}
