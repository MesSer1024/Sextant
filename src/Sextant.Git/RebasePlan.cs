using System.Globalization;
using System.Text;

namespace Sextant.Git;

public enum RebaseVerb
{
    Pick,
    Reword,
    Edit,
    Squash,
    Fixup,
    Drop,
}

public sealed record RebaseStep(string Sha, string Subject, RebaseVerb Verb, string? Message);

public sealed record RebaseRange(string? Upstream, IReadOnlyList<RebaseStep> Steps);

/// <summary>
/// Builds the commit list and the todo file for an interactive rebase.
/// The todo is installed by a sequence editor so git never opens its own editor.
/// A reword, or a squash with a message, is a pick or fixup followed by
/// <c>exec git commit --amend -F</c>. That amend runs later without an editor.
/// </summary>
public static class RebasePlan
{
    public static bool TryRange(
        IReadOnlyList<CommitRecord> loaded,
        string? headSha,
        IReadOnlyCollection<string> selected,
        out RebaseRange? range,
        out string? error)
    {
        range = null;
        error = null;
        if (string.IsNullOrEmpty(headSha))
        {
            error = "This branch has no commits to rebase.";
            return false;
        }

        if (selected.Count == 0)
        {
            error = "Select a commit to rebase.";
            return false;
        }

        var bySha = new Dictionary<string, CommitRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var commit in loaded)
            bySha[commit.Sha] = commit;

        var needed = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        var newestFirst = new List<CommitRecord>();
        var current = headSha;
        while (!string.IsNullOrEmpty(current))
        {
            if (!bySha.TryGetValue(current, out var commit))
            {
                error = newestFirst.Count == 0
                    ? "HEAD is not in the loaded history."
                    : "Load more history, or choose commits on a straight line from HEAD.";
                return false;
            }

            if (commit.Parents.Count > 1)
            {
                error = "This history has a merge. Interactive rebase here only rewrites a straight line of commits.";
                return false;
            }

            newestFirst.Add(commit);
            needed.Remove(commit.Sha);
            if (needed.Count == 0)
                break;
            current = commit.Parents.Count == 0 ? null : commit.Parents[0];
        }

        if (needed.Count > 0)
        {
            error = "Those commits are not on a straight line from HEAD.";
            return false;
        }

        var oldestFirst = new List<RebaseStep>(newestFirst.Count);
        for (var i = newestFirst.Count - 1; i >= 0; i--)
        {
            var commit = newestFirst[i];
            oldestFirst.Add(new RebaseStep(commit.Sha, commit.Subject, RebaseVerb.Pick, null));
        }

        var oldest = newestFirst[^1];
        var upstream = oldest.Parents.Count == 0 ? null : oldest.Parents[0];
        range = new RebaseRange(upstream, oldestFirst);
        return true;
    }

    public static string? Validate(IReadOnlyList<RebaseStep> steps)
    {
        if (steps.Count == 0)
            return "There are no commits to rebase.";

        var seen = false;
        foreach (var step in steps)
        {
            if (step.Verb == RebaseVerb.Drop)
                continue;
            if ((step.Verb == RebaseVerb.Squash || step.Verb == RebaseVerb.Fixup) && !seen)
                return "Squash and fixup need a commit before them.";
            if (step.Verb == RebaseVerb.Reword && string.IsNullOrWhiteSpace(step.Message))
                return "Reword needs a message.";
            seen = true;
        }

        return null;
    }

    public static string Render(IReadOnlyList<RebaseStep> steps, IReadOnlyDictionary<int, string> messageFiles)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var verb = step.Verb switch
            {
                RebaseVerb.Reword => "pick",
                RebaseVerb.Squash when messageFiles.ContainsKey(i) => "fixup",
                RebaseVerb.Pick => "pick",
                RebaseVerb.Edit => "edit",
                RebaseVerb.Squash => "squash",
                RebaseVerb.Fixup => "fixup",
                RebaseVerb.Drop => "drop",
                _ => "pick",
            };
            builder.Append(verb).Append(' ').Append(step.Sha);
            var subject = Sanitize(step.Subject);
            if (subject.Length > 0)
                builder.Append(' ').Append(subject);
            builder.Append('\n');
            if (messageFiles.TryGetValue(i, out var path))
                builder.Append("exec git commit --amend -F ").Append(ShellSingleQuote(path)).Append('\n');
        }

        return builder.ToString();
    }

    public static string ShellSingleQuote(string path)
    {
        var slash = path.Replace('\\', '/');
        return "'" + slash.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    public static IReadOnlyList<(string Sha, string Subject)> ParseSubjects(string text)
    {
        var commits = new List<(string Sha, string Subject)>();
        foreach (var record in text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = record.IndexOf('\u001f');
            if (split <= 0)
                continue;
            var subject = record[(split + 1)..].Trim();
            if (record[..split].Length == 0)
                continue;
            commits.Add((record[..split], subject));
        }

        return commits;
    }

    private static string Sanitize(string subject)
    {
        var text = subject.Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' ').Trim();
        return text.Length <= 72 ? text : text[..72];
    }
}

public static class RebaseEditor
{
    public const string PointerName = "sextant-rebase.path";

    public static string Create(string gitDirectory, IReadOnlyList<RebaseStep> steps)
    {
        Cleanup(gitDirectory);
        var directory = Path.Combine(Path.GetTempPath(), "sextant-rebase-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "msgs"));
        var messages = new Dictionary<int, string>();
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var custom = step.Verb == RebaseVerb.Reword
                || (step.Verb == RebaseVerb.Squash && !string.IsNullOrWhiteSpace(step.Message));
            if (!custom)
                continue;
            var path = Path.Combine(directory, "msgs", i.ToString(CultureInfo.InvariantCulture));
            var text = (step.Message ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            if (!text.EndsWith('\n'))
                text += "\n";
            File.WriteAllText(path, text, new UTF8Encoding(false));
            messages[i] = path;
        }

        var todo = RebasePlan.Render(steps, messages);
        File.WriteAllText(Path.Combine(directory, "todo"), todo, new UTF8Encoding(false));
        var script = Path.Combine(directory, "seq.sh");
        File.WriteAllText(script, "#!/bin/sh\ncp \"$(dirname \"$0\")/todo\" \"$1\"\n", new UTF8Encoding(false));
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (PlatformNotSupportedException)
        {
        }

        File.WriteAllText(Path.Combine(gitDirectory, PointerName), directory, new UTF8Encoding(false));
        return Quote(script);
    }

    public static string Quote(string path)
    {
        var slash = path.Replace('\\', '/');
        return "\"" + slash.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    public static void Cleanup(string gitDirectory)
    {
        var pointer = Path.Combine(gitDirectory, PointerName);
        if (!File.Exists(pointer))
            return;

        string directory;
        try
        {
            directory = File.ReadAllText(pointer).Trim();
        }
        catch (IOException)
        {
            return;
        }

        var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.StartsWith("sextant-rebase-", StringComparison.Ordinal) && Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        try
        {
            File.Delete(pointer);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
