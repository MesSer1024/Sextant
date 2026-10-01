namespace Sextant.Git;

public static class RepoPath
{
    public static string Normalize(string path)
    {
        var full = ResolveFinal(Path.GetFullPath(path));
        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) && string.Equals(full, root, Comparison))
            return full;
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool Same(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), Comparison);

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
        var full = Normalize(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, Comparison))
            return null;
        return full;
    }

    private static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// git worktree list prints the resolved path. On macOS, /var is /private/var,
    /// so a temp path and the listed worktree are the same directory.
    /// </summary>
    private static string ResolveFinal(string full, int depth = 0)
    {
        if (depth > 32)
            return full;
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
            return full;

        var current = root;
        foreach (var segment in full[root.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0)
                continue;
            current = Path.Combine(current, segment);
            var linked = TryResolveLink(current);
            if (linked is null || string.Equals(linked, current, Comparison))
                continue;
            var rest = full[(current.Length)..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var continued = rest.Length == 0 ? linked : Path.Combine(linked, rest);
            return ResolveFinal(Path.GetFullPath(continued), depth + 1);
        }

        return current;
    }

    private static string? TryResolveLink(string path)
    {
        try
        {
            var link = Directory.ResolveLinkTarget(path, returnFinalTarget: true);
            if (link is null)
                return null;
            var target = link.FullName;
            if (!Path.IsPathRooted(target))
            {
                var parent = Path.GetDirectoryName(path);
                target = parent is null ? target : Path.GetFullPath(Path.Combine(parent, target));
            }

            return Path.GetFullPath(target);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
