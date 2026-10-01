namespace Sextant.Git;

public static class PathGrouping
{
    public sealed record Node(string Label, string FullName, string? RefPath, IReadOnlyList<Node> Children);

    public static IReadOnlyList<Node> Group(IEnumerable<string> paths)
    {
        var list = paths
            .Where(path => !string.IsNullOrEmpty(path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        return GroupLevel(list, "");
    }

    private static List<Node> GroupLevel(List<string> paths, string prefix)
    {
        var result = new List<Node>();
        var index = 0;
        while (index < paths.Count)
        {
            var path = paths[index];
            var slash = path.IndexOf('/');
            var head = slash < 0 ? path : path[..slash];
            if (head.Length == 0)
            {
                result.Add(Leaf(path, prefix));
                index++;
                continue;
            }

            var folderPrefix = head + "/";
            var end = index + 1;
            while (end < paths.Count && (paths[end] == head || paths[end].StartsWith(folderPrefix, StringComparison.Ordinal)))
                end++;

            var exact = false;
            var nested = new List<string>();
            for (var cursor = index; cursor < end; cursor++)
            {
                if (paths[cursor] == head)
                    exact = true;
                else if (paths[cursor].StartsWith(folderPrefix, StringComparison.Ordinal))
                    nested.Add(paths[cursor][folderPrefix.Length..]);
            }

            var full = Join(prefix, head);
            if (nested.Count == 0)
                result.Add(new Node(head, full, full, []));
            else if (nested.Count == 1 && !exact)
                result.Add(Leaf(paths[index], prefix));
            else
                result.Add(new Node(head, full, exact ? full : null, GroupLevel(nested, full)));

            index = end;
        }

        return result;
    }

    private static Node Leaf(string relative, string prefix)
    {
        var full = Join(prefix, relative);
        return new Node(relative, full, full, []);
    }

    private static string Join(string prefix, string relative) =>
        prefix.Length == 0 ? relative : prefix + "/" + relative;
}
