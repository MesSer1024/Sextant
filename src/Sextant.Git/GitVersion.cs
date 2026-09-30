using System.Globalization;
using System.Text.RegularExpressions;

namespace Sextant.Git;

public sealed record GitVersion(int Major, int Minor, int Patch, string Raw)
{
    public static GitVersion Unsupported { get; } = new(0, 0, 0, "");
}

public static partial class GitVersions
{
    public static bool IsSupported(GitVersion version) =>
        version.Major > 2 || (version.Major == 2 && version.Minor >= 43);

    public static GitVersion Parse(string text)
    {
        var match = VersionPattern().Match(text);
        if (!match.Success)
            return new GitVersion(0, 0, 0, text.Trim());

        return new GitVersion(
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
            text.Trim());
    }

    [GeneratedRegex(@"git version (\d+)\.(\d+)\.(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
