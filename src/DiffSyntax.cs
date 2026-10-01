namespace Sextant;

public enum SyntaxKind
{
    Plain,
    Keyword,
    String,
    Comment,
    Number,
}

public readonly record struct SyntaxSpan(int Start, int Length, SyntaxKind Kind);

/// <summary>
/// Line tokenizer. Callers pass one visible line. Nothing here reads the rest of the file.
/// </summary>
public static class DiffSyntax
{
    private static readonly HashSet<string> CLike = new(StringComparer.Ordinal)
    {
        "if", "else", "for", "while", "do", "return", "break", "continue", "switch", "case", "default",
        "class", "struct", "enum", "interface", "namespace", "using", "public", "private", "protected",
        "internal", "static", "void", "int", "long", "short", "byte", "bool", "float", "double", "char",
        "string", "var", "new", "this", "null", "true", "false", "async", "await", "const", "let",
        "function", "func", "fn", "import", "package", "extends", "implements", "yield", "throw",
        "try", "catch", "finally", "override", "virtual", "abstract", "sealed", "partial", "record",
        "required", "get", "set", "in", "out", "ref", "typeof", "sizeof", "where", "when", "is", "as",
        "not", "and", "or", "readonly", "volatile", "extern", "operator", "explicit", "implicit",
        "nameof", "goto", "defer", "select", "go", "chan", "map", "type", "range",
    };

    private static readonly HashSet<string> Python = new(StringComparer.Ordinal)
    {
        "def", "class", "if", "elif", "else", "for", "while", "return", "yield", "import", "from",
        "as", "try", "except", "finally", "with", "lambda", "pass", "break", "continue", "True",
        "False", "None", "and", "or", "not", "in", "is", "async", "await", "raise", "global", "nonlocal",
    };

    private static readonly HashSet<string> Shell = new(StringComparer.Ordinal)
    {
        "if", "then", "else", "elif", "fi", "for", "while", "do", "done", "case", "esac", "in",
        "function", "return", "local", "echo", "exit", "set",
    };

    public static string? Language(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        return Path.GetExtension(path).TrimStart('.').ToLowerInvariant() switch
        {
            "cs" or "java" or "js" or "ts" or "tsx" or "jsx" or "c" or "h" or "cc" or "cpp" or "hpp"
                or "go" or "rs" or "swift" or "kt" or "kts" or "scala" or "php" or "css" or "sql" => "c",
            "py" => "py",
            "rb" => "rb",
            "sh" or "bash" or "zsh" or "ps1" => "sh",
            "json" => "json",
            "xml" or "html" or "htm" or "axaml" or "xaml" or "csproj" or "fsproj" or "svg" or "config" => "markup",
            "yml" or "yaml" or "toml" => "hash",
            _ => null,
        };
    }

    public static List<SyntaxSpan> Tokenize(string? line, string? language)
    {
        if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(language))
            return [];
        var keywords = language switch
        {
            "py" or "rb" => Python,
            "sh" => Shell,
            "c" or "json" => CLike,
            _ => null,
        };
        var hashComment = language is "py" or "rb" or "sh" or "hash";
        var slashComment = language is "c" or "json";
        var markup = language == "markup";
        var spans = new List<SyntaxSpan>();
        var index = 0;
        if (line.Length >= 2 && line[1] == ' ' && line[0] is '+' or '-' or ' ')
        {
            spans.Add(new SyntaxSpan(0, 2, SyntaxKind.Plain));
            index = 2;
        }

        while (index < line.Length)
        {
            var rest = line.AsSpan(index);
            if (slashComment && rest.StartsWith("//"))
            {
                spans.Add(new SyntaxSpan(index, line.Length - index, SyntaxKind.Comment));
                break;
            }

            if (slashComment && rest.StartsWith("/*"))
            {
                var close = line.IndexOf("*/", index + 2, StringComparison.Ordinal);
                var end = close < 0 ? line.Length : close + 2;
                spans.Add(new SyntaxSpan(index, end - index, SyntaxKind.Comment));
                index = end;
                continue;
            }

            if (hashComment && line[index] == '#')
            {
                spans.Add(new SyntaxSpan(index, line.Length - index, SyntaxKind.Comment));
                break;
            }

            if (markup && rest.StartsWith("<!--"))
            {
                var close = line.IndexOf("-->", index + 4, StringComparison.Ordinal);
                var end = close < 0 ? line.Length : close + 3;
                spans.Add(new SyntaxSpan(index, end - index, SyntaxKind.Comment));
                index = end;
                continue;
            }

            if (line[index] is '"' or '\'')
            {
                var end = ScanString(line, index);
                spans.Add(new SyntaxSpan(index, end - index, SyntaxKind.String));
                index = end;
                continue;
            }

            if (char.IsDigit(line[index]))
            {
                var end = index + 1;
                while (end < line.Length && char.IsDigit(line[end]))
                    end++;
                if (end < line.Length && line[end] == '.' && end + 1 < line.Length && char.IsDigit(line[end + 1]))
                {
                    end += 2;
                    while (end < line.Length && char.IsDigit(line[end]))
                        end++;
                }

                spans.Add(new SyntaxSpan(index, end - index, SyntaxKind.Number));
                index = end;
                continue;
            }

            if (IsIdentStart(line[index]))
            {
                var end = index + 1;
                while (end < line.Length && IsIdent(line[end]))
                    end++;
                var word = line[index..end];
                var kind = keywords is not null && keywords.Contains(word) ? SyntaxKind.Keyword : SyntaxKind.Plain;
                spans.Add(new SyntaxSpan(index, end - index, kind));
                index = end;
                continue;
            }

            spans.Add(new SyntaxSpan(index, 1, SyntaxKind.Plain));
            index++;
        }

        return spans;
    }

    private static int ScanString(string line, int start)
    {
        var quote = line[start];
        var index = start + 1;
        while (index < line.Length)
        {
            if (line[index] == '\\' && index + 1 < line.Length)
            {
                index += 2;
                continue;
            }

            if (line[index] == quote)
                return index + 1;
            index++;
        }

        return line.Length;
    }

    private static bool IsIdentStart(char character) =>
        character is '_' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsIdent(char character) =>
        IsIdentStart(character) || char.IsDigit(character);
}
