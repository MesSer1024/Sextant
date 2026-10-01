using System.Text;
using Sextant.Git.Parsing;

namespace Sextant.Git.Tests;

public class Phase4Tests
{
    private const string PngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public void Submodule_status_reads_state_sha_path_and_describe()
    {
        var text = """
             0123456789abcdef vendor/lib (heads/main)
            -0123456789abcdef vendor/empty
            +0123456789abcdef vendor/dirty (v1)
            U0123456789abcdef vendor/bad

            """;
        var entries = SubmoduleParser.Parse(text);
        Assert.Equal(4, entries.Count);
        Assert.Equal(SubmoduleState.Matches, entries[0].State);
        Assert.Equal("vendor/lib", entries[0].Path);
        Assert.Equal("0123456789abcdef", entries[0].Sha);
        Assert.Equal("heads/main", entries[0].Describe);
        Assert.Equal(SubmoduleState.Uninitialized, entries[1].State);
        Assert.Equal("vendor/empty", entries[1].Path);
        Assert.Null(entries[1].Describe);
        Assert.Equal(SubmoduleState.Modified, entries[2].State);
        Assert.Equal("v1", entries[2].Describe);
        Assert.Equal(SubmoduleState.Conflict, entries[3].State);
    }

    [Fact]
    public void Worktree_porcelain_splits_records()
    {
        var text = """
            worktree C:/repo
            HEAD abcdef
            branch refs/heads/main

            worktree C:/other
            HEAD 1234567
            detached
            locked busy

            """;
        var entries = WorktreeParser.Parse(text);
        Assert.Equal(2, entries.Count);
        Assert.Equal("C:/repo", entries[0].Path);
        Assert.Equal("abcdef", entries[0].Head);
        Assert.Equal("refs/heads/main", entries[0].Branch);
        Assert.False(entries[0].Detached);
        Assert.Equal("C:/other", entries[1].Path);
        Assert.True(entries[1].Detached);
        Assert.True(entries[1].Locked);
        Assert.Equal("busy", entries[1].LockReason);
        Assert.Equal(["keep", "skip"], WorktreeParser.Patterns("keep\n\n skip \n"));
    }

    [Fact]
    public void Lfs_pointer_parses_and_a_diff_keeps_both_sides()
    {
        var before = "version https://git-lfs.github.com/spec/v1\noid sha256:aaaa\nsize 4\n";
        var after = "version https://git-lfs.github.com/spec/v1\noid sha256:bbbb\nsize 8\n";
        Assert.True(LfsPointers.TryParse(before, out var pointer));
        Assert.Equal("sha256:aaaa", pointer!.Oid);
        Assert.Equal(4, pointer.Size);
        Assert.Equal(before, pointer.Render());

        var document = new DiffDocument(false, false, false, false, false,
        [
            new DiffHunk(1, 3, 1, 3, "@@ -1,3 +1,3 @@",
            [
                new DiffLine(DiffLineKind.Removed, "version https://git-lfs.github.com/spec/v1"),
                new DiffLine(DiffLineKind.Removed, "oid sha256:aaaa"),
                new DiffLine(DiffLineKind.Removed, "size 4"),
                new DiffLine(DiffLineKind.Added, "version https://git-lfs.github.com/spec/v1"),
                new DiffLine(DiffLineKind.Added, "oid sha256:bbbb"),
                new DiffLine(DiffLineKind.Added, "size 8"),
                new DiffLine(DiffLineKind.Meta, "\\ No newline at end of file"),
            ]),
        ], "");
        Assert.True(LfsPointers.TryReadDiff(document, out var oldPointer, out var newPointer));
        Assert.Equal(4, oldPointer!.Size);
        Assert.Equal(8, newPointer!.Size);
        Assert.Equal(after, newPointer.Render());
    }

    [Fact]
    public void Diff_and_show_keep_lfs_pointers_and_status_does_not()
    {
        Assert.Contains("filter.lfs.process=", GitCommands.DiffWorktree("repo", false, false));
        Assert.Contains("--no-textconv", GitCommands.DiffWorktree("repo", false, false));
        Assert.DoesNotContain("diff.lfs.textconv=", GitCommands.DiffWorktree("repo", false, false));
        Assert.Contains("filter.lfs.process=", GitCommands.DiffUntracked("repo", "a.bin"));
        Assert.Contains("--no-textconv", GitCommands.DiffRange("repo", "a", "b", "file.bin"));
        Assert.Contains("--no-textconv", GitCommands.ShowPatch("repo", "abc", "a.bin"));
        Assert.Contains("--no-textconv", GitCommands.ShowStage("repo", 2, "a.bin"));
        Assert.Contains("--no-textconv", GitCommands.Blame("repo", "HEAD", "a.bin"));
        Assert.DoesNotContain("--no-textconv", GitCommands.Status("repo"));
        Assert.DoesNotContain("filter.lfs.process=", GitCommands.Status("repo"));
        Assert.DoesNotContain("filter.lfs.smudge=", GitCommands.Status("repo"));
        Assert.DoesNotContain(GitCommands.PushForceWithLease("repo"), argument => argument == "--force");

        var added = GitCommands.WorktreeAdd("repo", "C:/wt", "feature", "HEAD", noCheckout: true);
        Assert.Contains("--no-checkout", added);
        Assert.Contains("-b", added);
        Assert.DoesNotContain("--force", added);
        Assert.Contains("--no-cone", GitCommands.SparseSet("wt", false, ["keep"]));
        Assert.DoesNotContain("--no-cone", GitCommands.SparseSet("wt", true, ["keep"]));
        Assert.Contains("--", GitCommands.SparseSet("wt", true, ["keep"]));
        Assert.Equal("HEAD:./skip/gone.txt", GitCommands.ObjectSpec("HEAD", "skip\\gone.txt"));
        Assert.Equal(":./a.txt", GitCommands.ObjectSpec("", "a.txt"));
        Assert.Contains("--no-optional-locks", GitCommands.CatFileBlob("repo", "HEAD:./a"));
        Assert.DoesNotContain("--no-optional-locks", GitCommands.CatFileFiltered("repo", "HEAD:./a"));
        Assert.Equal(["-C", "wt", "checkout"], GitCommands.CheckoutCurrent("wt"));
    }

    [Fact]
    public void Preview_cap_is_eight_mebibytes()
    {
        Assert.True(PreviewLimit.Allows(HistoryLimits.MaxPreviewBytes));
        Assert.False(PreviewLimit.Allows(HistoryLimits.MaxPreviewBytes + 1));
        Assert.False(PreviewLimit.Allows(-1));
    }

    [Fact]
    public void Syntax_covers_the_visible_line_only()
    {
        const string line = "  public class Foo";
        var spans = DiffSyntax.Tokenize(line, "c");
        Assert.Contains(spans, span => span.Kind == SyntaxKind.Keyword && line.Substring(span.Start, span.Length) == "public");
        Assert.Contains(spans, span => span.Kind == SyntaxKind.Keyword && line.Substring(span.Start, span.Length) == "class");
        const string comment = "+ // note";
        Assert.Contains(DiffSyntax.Tokenize(comment, "c"), span => span.Kind == SyntaxKind.Comment && comment.Substring(span.Start, span.Length).Contains("//", StringComparison.Ordinal));
        Assert.Empty(DiffSyntax.Tokenize("public", null));
        Assert.Equal("c", DiffSyntax.Language("a.cs"));
        Assert.Null(DiffSyntax.Language("a.png"));
    }

    [Fact]
    public void Config_flag_accepts_git_true_values()
    {
        foreach (var value in new[] { "true", "yes", "on", "1", " TRUE " })
        {
            var config = new Dictionary<string, string> { ["core.sparseCheckout"] = value };
            Assert.True(ConfigParser.IsEnabled(config, "core.sparseCheckout"));
        }

        Assert.False(ConfigParser.IsEnabled(new Dictionary<string, string> { ["core.sparseCheckout"] = "false" }, "core.sparseCheckout"));
        Assert.False(ConfigParser.IsEnabled(new Dictionary<string, string>(), "core.sparseCheckout"));
    }

    [Fact]
    public async Task Sparse_checkout_reads_excluded_blobs_and_a_new_worktree_stays_sparse()
    {
        using var repo = new TempRepo();
        repo.WriteFile("keep/file.txt", "keep");
        repo.WriteFile("skip/gone.txt", "gone");
        repo.WriteFile("skip/pic.png", Encoding.ASCII.GetString(Convert.FromBase64String(PngBase64)));
        File.WriteAllBytes(Path.Combine(repo.Directory, "skip", "pic.png"), Convert.FromBase64String(PngBase64));
        repo.CommitAll("files");
        repo.Run("sparse-checkout", "set", "keep");
        var excluded = Path.Combine(repo.Directory, "skip", "gone.txt");
        var picture = Path.Combine(repo.Directory, "skip", "pic.png");
        Assert.False(File.Exists(excluded));
        Assert.False(File.Exists(picture));

        await using var session = await Open(repo);
        Assert.True(session.Snapshot().SparseCheckout);
        var bytes = await session.ReadObjectAsync("HEAD", "skip/gone.txt", CancellationToken.None);
        Assert.Equal("gone", Encoding.UTF8.GetString(bytes!));
        Assert.False(File.Exists(excluded));

        var preview = await session.PreviewImageAsync(
            new ImageRequest("skip/pic.png", null, "HEAD", false, false),
            CancellationToken.None);
        Assert.NotNull(preview);
        Assert.NotNull(preview.After);
        Assert.True(preview.After!.Length > 8);
        Assert.Equal(0x89, preview.After[0]);
        Assert.False(File.Exists(picture));

        var extra = Path.Combine(Path.GetTempPath(), "sextant-wt-" + Guid.NewGuid().ToString("N"));
        try
        {
            await session.AddWorktreeAsync(extra, "feature", null, CancellationToken.None);
            Assert.Contains(session.Snapshot().Worktrees, tree => RepoPath.Same(tree.Path, extra));
            Assert.True(File.Exists(Path.Combine(extra, "keep", "file.txt")));
            Assert.False(File.Exists(Path.Combine(extra, "skip", "gone.txt")));
            Assert.False(File.Exists(Path.Combine(extra, "skip", "pic.png")));
        }
        finally
        {
            RemoveWorktree(repo, extra);
        }
    }

    [Fact]
    public async Task Empty_sparse_patterns_do_not_create_a_worktree()
    {
        using var repo = new TempRepo();
        repo.WriteFile("keep.txt", "keep");
        repo.CommitAll("keep");
        repo.Run("config", "core.sparseCheckout", "true");
        Directory.CreateDirectory(Path.Combine(repo.Directory, ".git", "info"));
        File.WriteAllText(Path.Combine(repo.Directory, ".git", "info", "sparse-checkout"), "");
        await using var session = await Open(repo);
        var extra = Path.Combine(Path.GetTempPath(), "sextant-wt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var error = await Assert.ThrowsAsync<RepositoryActionException>(() =>
                session.AddWorktreeAsync(extra, "feature", null, CancellationToken.None));
            Assert.Contains("no patterns", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(extra));
        }
        finally
        {
            RemoveWorktree(repo, extra);
        }
    }

    [Fact]
    public async Task Worktree_add_lists_the_new_directory()
    {
        using var repo = new TempRepo();
        repo.WriteFile("keep.txt", "keep");
        repo.CommitAll("keep");
        await using var session = await Open(repo);
        Assert.Contains(session.Snapshot().Worktrees, tree => RepoPath.Same(tree.Path, repo.Directory));
        var extra = Path.Combine(Path.GetTempPath(), "sextant-wt-" + Guid.NewGuid().ToString("N"));
        try
        {
            await session.AddWorktreeAsync(extra, "feature", null, CancellationToken.None);
            Assert.Contains(session.Snapshot().Worktrees, tree => RepoPath.Same(tree.Path, extra));
            Assert.True(File.Exists(Path.Combine(extra, "keep.txt")));
            Assert.DoesNotContain(session.Snapshot().Commands, command => command.Arguments.Contains("--no-checkout"));
        }
        finally
        {
            RemoveWorktree(repo, extra);
        }
    }

    [Fact]
    public async Task Submodule_is_listed_and_not_updated()
    {
        using var child = new TempRepo();
        child.WriteFile("lib.txt", "lib");
        child.CommitAll("lib");
        using var parent = new TempRepo();
        parent.WriteFile("readme.txt", "parent");
        parent.CommitAll("parent");
        parent.Run("config", "protocol.file.allow", "always");
        parent.Run("-c", "protocol.file.allow=always", "submodule", "add", child.Directory, "vendor/lib");
        parent.CommitAll("add submodule");

        await using var session = await Open(parent);
        var entry = Assert.Single(session.Snapshot().Submodules);
        Assert.Equal("vendor/lib", entry.Path.Replace('\\', '/'));
        Assert.NotEqual(SubmoduleState.Uninitialized, entry.State);
        Assert.DoesNotContain(session.Snapshot().Commands, command =>
            command.Arguments.Contains("submodule") && command.Arguments.Contains("update"));
    }

    [Fact]
    public async Task Lfs_pointer_diff_does_not_download_and_over_cap_does_not_start_git()
    {
        using var repo = new TempRepo();
        repo.Run("config", "core.autocrlf", "false");
        repo.WriteFile(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
        var oid = "sha256:" + new string('a', 64);
        var first = "version https://git-lfs.github.com/spec/v1\noid " + oid + "\nsize 4\n";
        var second = "version https://git-lfs.github.com/spec/v1\noid " + oid + "\nsize 8\n";
        repo.WriteFile("data.bin", first);
        repo.WriteFile("note.txt", "hello filters\n");
        repo.CommitAll("pointer");
        var parent = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.WriteFile("data.bin", second);
        repo.CommitAll("pointer changed");
        var head = repo.RunCapture("rev-parse", "HEAD").Trim();
        var file = Path.Combine(repo.Directory, "data.bin");
        var onDisk = File.ReadAllBytes(file);

        await using var session = await Open(repo);
        var diff = await session.CommitDiffAsync(head, parent, "data.bin", false, CancellationToken.None);
        Assert.NotNull(diff);
        var note = Assert.Single(diff.LfsFiles);
        Assert.Equal(4, note.Before!.Size);
        Assert.Equal(8, note.After!.Size);
        Assert.Contains(session.Snapshot().Commands, command => command.Arguments.Contains("--no-textconv"));
        Assert.Contains(session.Snapshot().Commands, command => command.Arguments.Contains("filter.lfs.process="));
        Assert.Equal(onDisk, File.ReadAllBytes(file));

        var beforeCount = session.Snapshot().Commands.Count;
        var over = await session.LoadRequestedBlobAsync("note.txt", "HEAD", false, HistoryLimits.MaxPreviewBytes + 1, null, CancellationToken.None);
        Assert.True(over.TooLarge);
        Assert.Equal(beforeCount, session.Snapshot().Commands.Count);

        var text = await session.LoadRequestedBlobAsync("note.txt", "HEAD", false, 16, null, CancellationToken.None);
        Assert.False(text.TooLarge);
        Assert.Contains("hello filters", Encoding.UTF8.GetString(text.Bytes), StringComparison.Ordinal);

        try
        {
            await session.LoadRequestedBlobAsync("data.bin", null, false, 4, second, CancellationToken.None);
        }
        catch (GitCommandFailedException)
        {
        }

        Assert.Equal(onDisk, File.ReadAllBytes(file));
    }

    [Fact]
    public async Task Image_preview_loads_both_versions()
    {
        using var repo = new TempRepo();
        repo.Run("config", "core.autocrlf", "false");
        var first = Convert.FromBase64String(PngBase64);
        var second = (byte[])first.Clone();
        second[^1] ^= 0x5A;
        var path = Path.Combine(repo.Directory, "pic.png");
        File.WriteAllBytes(path, first);
        repo.CommitAll("image");
        var parent = repo.RunCapture("rev-parse", "HEAD").Trim();
        File.WriteAllBytes(path, second);

        await using var session = await Open(repo);
        var worktree = await session.PreviewImageAsync(
            new ImageRequest("pic.png", "", null, false, true),
            CancellationToken.None);
        Assert.NotNull(worktree);
        Assert.Equal(first, worktree.Before);
        Assert.Equal(second, worktree.After);
        Assert.Equal("", worktree.BeforeNotice);
        Assert.Equal("", worktree.AfterNotice);

        repo.CommitAll("image changed");
        var head = repo.RunCapture("rev-parse", "HEAD").Trim();
        var committed = await session.PreviewImageAsync(
            new ImageRequest("pic.png", parent, head, false, false),
            CancellationToken.None);
        Assert.NotNull(committed);
        Assert.Equal(first, committed.Before);
        Assert.Equal(second, committed.After);
    }

    [Fact]
    public async Task Image_preview_loads_svg_and_tiff()
    {
        using var repo = new TempRepo();
        repo.Run("config", "core.autocrlf", "false");
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect width=\"8\" height=\"8\" fill=\"#00ff00\"/></svg>\n";
        repo.WriteFile("mark.svg", svg);
        var tiff = new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00 };
        File.WriteAllBytes(Path.Combine(repo.Directory, "scan.tiff"), tiff);
        repo.CommitAll("pictures");
        var head = repo.RunCapture("rev-parse", "HEAD").Trim();

        await using var session = await Open(repo);
        var svgPreview = await session.PreviewImageAsync(
            new ImageRequest("mark.svg", null, head, false, false),
            CancellationToken.None);
        Assert.NotNull(svgPreview);
        Assert.Null(svgPreview.Before);
        Assert.Contains("fill=\"#00ff00\"", Encoding.UTF8.GetString(svgPreview.After!), StringComparison.Ordinal);

        var tiffPreview = await session.PreviewImageAsync(
            new ImageRequest("scan.tiff", null, head, false, false),
            CancellationToken.None);
        Assert.NotNull(tiffPreview);
        Assert.Equal(tiff, tiffPreview.After);
    }

    [Fact]
    public async Task Image_pointer_preview_loads_both_versions_and_leaves_the_worktree()
    {
        var scriptDir = Path.Combine(Path.GetTempPath(), "sextant-script-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scriptDir);
        var scriptPath = Path.Combine(scriptDir, "smudge.ps1");
        var scriptGit = scriptPath.Replace('\\', '/');
        File.WriteAllBytes(scriptPath, Encoding.ASCII.GetBytes("""
            $ms = New-Object System.IO.MemoryStream
            [Console]::OpenStandardInput().CopyTo($ms)
            $text = [Text.Encoding]::UTF8.GetString($ms.ToArray())
            $b64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="
            $out = [Console]::OpenStandardOutput()
            $bytes = [Convert]::FromBase64String($b64)
            if ($text.Contains("size 8")) { $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 0x5A }
            $out.Write($bytes, 0, $bytes.Length)
            $out.Flush()
            """));
        try
        {
            using var repo = new TempRepo();
            repo.Run("config", "core.autocrlf", "false");
            repo.Run("config", "filter.img.smudge", "powershell -NoProfile -ExecutionPolicy Bypass -File " + scriptGit);
            repo.Run("config", "filter.img.required", "false");
            repo.WriteFile(".gitattributes", "*.png filter=img -text\n");
            var small = Convert.FromBase64String(PngBase64);
            var changed = (byte[])small.Clone();
            changed[^1] ^= 0x5A;
            repo.WriteFile("pic.png", Pointer('a', 4));
            repo.CommitAll("pointer");
            var parent = repo.RunCapture("rev-parse", "HEAD").Trim();
            repo.WriteFile("pic.png", Pointer('b', 8));
            var file = Path.Combine(repo.Directory, "pic.png");
            var onDisk = File.ReadAllBytes(file);

            await using var session = await Open(repo);
            var unstaged = await session.PreviewImageAsync(
                new ImageRequest("pic.png", "", null, false, true),
                CancellationToken.None);
            Assert.NotNull(unstaged);
            Assert.Equal(small, unstaged.Before);
            Assert.Equal(changed, unstaged.After);
            Assert.Equal("", unstaged.BeforeNotice);
            Assert.Equal("", unstaged.AfterNotice);
            Assert.Equal(onDisk, File.ReadAllBytes(file));

            repo.CommitAll("pointer changed");
            var head = repo.RunCapture("rev-parse", "HEAD").Trim();
            var committed = await session.PreviewImageAsync(
                new ImageRequest("pic.png", parent, head, false, false),
                CancellationToken.None);
            Assert.NotNull(committed);
            Assert.Equal(small, committed.Before);
            Assert.Equal(changed, committed.After);
            Assert.Equal("", committed.BeforeNotice);
            Assert.Equal("", committed.AfterNotice);
            Assert.StartsWith("version https://git-lfs", Encoding.UTF8.GetString(File.ReadAllBytes(file)), StringComparison.Ordinal);
            Assert.Contains(session.Snapshot().Commands, command => command.Arguments.Contains("--filters"));
        }
        finally
        {
            try
            {
                if (Directory.Exists(scriptDir))
                    Directory.Delete(scriptDir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task Image_pointer_over_cap_does_not_smudge()
    {
        using var repo = new TempRepo();
        repo.Run("config", "core.autocrlf", "false");
        repo.WriteFile(".gitattributes", "*.png filter=lfs diff=lfs merge=lfs -text\n");
        var huge = HistoryLimits.MaxPreviewBytes + 1;
        repo.WriteFile("pic.png", Pointer('c', huge));
        repo.CommitAll("huge pointer");
        var head = repo.RunCapture("rev-parse", "HEAD").Trim();
        var file = Path.Combine(repo.Directory, "pic.png");
        var onDisk = File.ReadAllBytes(file);

        await using var session = await Open(repo);
        var before = session.Snapshot().Commands.Count;
        var preview = await session.PreviewImageAsync(
            new ImageRequest("pic.png", head, null, false, true),
            CancellationToken.None);
        Assert.NotNull(preview);
        Assert.Null(preview.Before);
        Assert.Null(preview.After);
        Assert.Contains("8 MB", preview.BeforeNotice, StringComparison.Ordinal);
        Assert.Contains("8 MB", preview.AfterNotice, StringComparison.Ordinal);
        var added = session.Snapshot().Commands.Skip(before);
        Assert.DoesNotContain(added, command => command.Arguments.Contains("--filters"));
        Assert.DoesNotContain(added, command => command.Arguments.Contains("lfs"));
        Assert.Equal(onDisk, File.ReadAllBytes(file));
    }

    private static string Pointer(char oid, long size) =>
        "version https://git-lfs.github.com/spec/v1\noid sha256:" + new string(oid, 64) + "\nsize " + size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);

    private static void RemoveWorktree(TempRepo repo, string path)
    {
        try
        {
            if (Directory.Exists(path))
                repo.Run("worktree", "remove", "--force", path);
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
