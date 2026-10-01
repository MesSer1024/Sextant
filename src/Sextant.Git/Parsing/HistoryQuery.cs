namespace Sextant.Git.Parsing;

/// <summary>
/// One history box. <c>branch:name</c> limits the revision. <c>author:name</c> limits the author.
/// A lone hexadecimal token of at least 7 digits is a revision. Any other text matches the subject or the author.
/// </summary>
public sealed record HistoryQuery(
    string? Revision,
    string? Grep,
    string? Author,
    string? Path,
    bool ShaLookup,
    bool MatchSubjectOrAuthor)
{
    public bool IsEmpty => Path is null && !ShaLookup && Revision is null && Grep is null && Author is null;

    public static HistoryQuery ForPath(string path) => new(null, null, null, path, false, false);

    public string Describe()
    {
        if (!string.IsNullOrEmpty(Path))
            return "File " + Path;
        if (ShaLookup)
            return Revision ?? "";
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(Revision))
            parts.Add(Revision);
        if (MatchSubjectOrAuthor && !string.IsNullOrEmpty(Grep))
            parts.Add("\"" + Grep + "\"");
        else
        {
            if (!string.IsNullOrEmpty(Author))
                parts.Add("author:" + Author);
            if (!string.IsNullOrEmpty(Grep))
                parts.Add("\"" + Grep + "\"");
        }

        return string.Join("  ", parts);
    }
}

public static class HistoryQueryParser
{
    public static HistoryQuery Parse(string? text)
    {
        string? revision = null;
        string? author = null;
        var free = new List<string>();
        foreach (var token in Tokenize(text ?? ""))
        {
            if (StartsWith(token, "branch:") && token.Length > "branch:".Length)
                revision = Unquote(token["branch:".Length..]);
            else if (StartsWith(token, "author:") && token.Length > "author:".Length)
                author = Unquote(token["author:".Length..]);
            else
                free.Add(token);
        }

        var words = string.Join(' ', free);
        if (revision is null && author is null && IsSha(words))
            return new HistoryQuery(words, null, null, null, true, false);

        var matchEither = author is null && words.Length > 0;
        return new HistoryQuery(
            revision,
            words.Length == 0 ? null : words,
            matchEither ? words : author,
            null,
            false,
            matchEither);
    }

    public static bool IsSha(string text) =>
        text.Length is >= 7 and <= 64
        && text.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F'));

    private static bool StartsWith(string token, string prefix) =>
        token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0])
            return value[1..^1];
        return value;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;
            if (index >= text.Length)
                break;
            if (text[index] is '"' or '\'')
            {
                var quote = text[index];
                index++;
                var start = index;
                while (index < text.Length && text[index] != quote)
                    index++;
                tokens.Add(text[start..index]);
                if (index < text.Length)
                    index++;
                continue;
            }

            var word = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                if (text[index] is '"' or '\'')
                {
                    var quote = text[index];
                    index++;
                    while (index < text.Length && text[index] != quote)
                        index++;
                    if (index < text.Length)
                        index++;
                    continue;
                }

                index++;
            }

            tokens.Add(text[word..index]);
        }

        return tokens;
    }
}
