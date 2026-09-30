namespace Sextant.Git;

public static class ArgumentRedactor
{
    public static string Redact(string argument)
    {
        if (!argument.Contains("://", StringComparison.Ordinal))
            return argument;
        if (!Uri.TryCreate(argument, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo))
            return argument;

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
        };
        return builder.Uri.AbsoluteUri;
    }

    public static IReadOnlyList<string> RedactAll(IEnumerable<string> arguments)
    {
        var list = new List<string>();
        foreach (var argument in arguments)
            list.Add(Redact(argument));
        return list;
    }
}
