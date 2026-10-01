namespace Sextant.Git;

public static class RepoPath
{
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool Same(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Normalize(left), Normalize(right), comparison);
    }

    public static string? CombineUnder(string toplevel, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains('\0') || relative.Contains('\n') || relative.Contains('\r'))
            return null;
        if (Path.IsPathRooted(relative))
            return null;
        var segments = relative.Split('/', '\\');
        foreach (var segment in segments)
        {
            if (segment == "..")
                return null;
        }

        var root = Normalize(toplevel);
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var prefix = root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, comparison))
            return null;
        return full;
    }
}
