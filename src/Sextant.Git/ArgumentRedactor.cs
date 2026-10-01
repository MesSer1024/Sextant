using System.Text.RegularExpressions;

namespace Sextant.Git;

public static partial class ArgumentRedactor
{
    [GeneratedRegex(@"(?i)([a-z][a-z0-9+.-]*://)(?:[^/\s:@]+:[^/\s@]*@|[^/\s:@]+@)", RegexOptions.CultureInvariant)]
    private static partial Regex UserInfoPattern();

    public static string Redact(string argument)
    {
        if (!argument.Contains("://", StringComparison.Ordinal))
            return argument;
        if (!Uri.TryCreate(argument, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo))
            return RedactText(argument);

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
        };
        return builder.Uri.AbsoluteUri;
    }

    public static string RedactText(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("://", StringComparison.Ordinal))
            return text;
        return UserInfoPattern().Replace(text, "$1");
    }

    public static IReadOnlyList<string> RedactAll(IEnumerable<string> arguments)
    {
        var list = new List<string>();
        foreach (var argument in arguments)
            list.Add(Redact(argument));
        return list;
    }
}
