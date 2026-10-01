namespace Sextant.Git.Parsing;

public static class WorktreeParser
{
    public static IReadOnlyList<WorktreeEntry> Parse(string text)
    {
        var entries = new List<WorktreeEntry>();
        string? path = null;
        string? head = null;
        string? branch = null;
        var detached = false;
        var bare = false;
        var locked = false;
        string? reason = null;

        void Flush()
        {
            if (path is null)
                return;
            entries.Add(new WorktreeEntry(path, head, branch, detached, bare, locked, reason));
            path = null;
            head = null;
            branch = null;
            detached = false;
            bare = false;
            locked = false;
            reason = null;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var raw in normalized.Split('\n'))
        {
            if (raw.Length == 0)
            {
                Flush();
                continue;
            }

            if (raw.StartsWith("worktree ", StringComparison.Ordinal))
            {
                Flush();
                path = raw["worktree ".Length..];
            }
            else if (raw.StartsWith("HEAD ", StringComparison.Ordinal))
            {
                head = raw["HEAD ".Length..].Trim();
            }
            else if (raw.StartsWith("branch ", StringComparison.Ordinal))
            {
                branch = raw["branch ".Length..].Trim();
            }
            else if (raw.Equals("detached", StringComparison.Ordinal))
            {
                detached = true;
            }
            else if (raw.Equals("bare", StringComparison.Ordinal))
            {
                bare = true;
            }
            else if (raw.Equals("locked", StringComparison.Ordinal) || raw.StartsWith("locked ", StringComparison.Ordinal))
            {
                locked = true;
                if (raw.StartsWith("locked ", StringComparison.Ordinal))
                    reason = raw["locked ".Length..];
            }
        }

        Flush();
        return entries;
    }

    public static IReadOnlyList<string> Patterns(string text)
    {
        var patterns = new List<string>();
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var raw in normalized.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 0)
                patterns.Add(line);
        }

        return patterns;
    }
}
