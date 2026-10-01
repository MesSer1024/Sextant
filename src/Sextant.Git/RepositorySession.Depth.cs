using System.Globalization;
using System.Text;
using Sextant.Git.Parsing;

namespace Sextant.Git;

public sealed partial class RepositorySession
{
    private const int LfsProbeLimit = 30;

    public Task AddWorktreeAsync(string path, string? newBranch, string? startPoint, CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            var sparse = Sparse();
            var cone = false;
            lock (_stateLock)
                cone = ConfigParser.IsEnabled(_config, "core.sparseCheckoutCone");

            IReadOnlyList<string> patterns = [];
            if (sparse)
            {
                patterns = await _scheduler.ReadAsync(async token =>
                {
                    var listed = Checked(await ExecuteAsync(GitCommands.SparseList(_toplevel), null, token).ConfigureAwait(false));
                    return WorktreeParser.Patterns(Encoding.UTF8.GetString(listed.Stdout));
                }, ct).ConfigureAwait(false);
                if (patterns.Count == 0)
                    throw new RepositoryActionException("Sparse checkout is on, but it has no patterns. The worktree was not created, so excluded paths stay out of the working tree.");
            }

            await _scheduler.WriteAsync(async token =>
            {
                Checked(await ExecuteAsync(GitCommands.WorktreeAdd(_toplevel, path, newBranch, startPoint, sparse), null, token).ConfigureAwait(false));
                if (!sparse)
                    return 0;
                try
                {
                    Checked(await ExecuteAsync(GitCommands.SparseSet(path, cone, patterns), null, token).ConfigureAwait(false));
                    // set records the patterns. A --no-checkout worktree stays empty until checkout, which leaves excluded paths out.
                    Checked(await ExecuteAsync(GitCommands.CheckoutCurrent(path), null, token).ConfigureAwait(false));
                }
                catch (GitCommandFailedException)
                {
                    try
                    {
                        Checked(await ExecuteAsync(GitCommands.WorktreeRemove(_toplevel, path), null, token).ConfigureAwait(false));
                    }
                    catch (GitCommandFailedException)
                    {
                    }

                    throw;
                }

                return 0;
            }, ct).ConfigureAwait(false);
            await LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: false).ConfigureAwait(false);
        }, cancellationToken);

    public Task<byte[]?> ReadObjectAsync(string revision, string path, CancellationToken cancellationToken) =>
        RunAsync(ct => _scheduler.ReadAsync(inner => ReadRawBlobAsync(GitCommands.ObjectSpec(revision, path), inner), ct), cancellationToken);

    public Task<ImagePreview?> PreviewImageAsync(ImageRequest request, CancellationToken cancellationToken)
    {
        if (!ImageFiles.IsImagePath(request.Path))
            return Task.FromResult<ImagePreview?>(null);
        return RunAsync(async ct =>
        {
            var beforePath = string.IsNullOrEmpty(request.BeforePath) ? request.Path : request.BeforePath;
            var before = await ImageSideAsync(beforePath, request.BeforeRevision, request.BeforeIsWorktree, ct).ConfigureAwait(false);
            var after = await ImageSideAsync(request.Path, request.AfterRevision, request.AfterIsWorktree, ct).ConfigureAwait(false);
            if (before.Bytes is null && after.Bytes is null && before.Notice.Length == 0 && after.Notice.Length == 0)
                return null;
            var notice = new StringBuilder();
            if (before.Notice.Length > 0)
                notice.Append(before.Notice).Append(' ');
            if (after.Notice.Length > 0)
                notice.Append(after.Notice);
            return new ImagePreview(before.Bytes, after.Bytes, notice.ToString().Trim())
            {
                BeforeNotice = before.Bytes is null ? before.Notice : "",
                AfterNotice = after.Bytes is null ? after.Notice : "",
            };
        }, cancellationToken);
    }

    public Task<BlobLoad> LoadRequestedBlobAsync(
        string path,
        string? revision,
        bool localFile,
        long declaredSize,
        string? pointerText,
        CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            if (!PreviewLimit.Allows(declaredSize))
                return BlobLoad.OverLimit;
            if (localFile)
                return await ReadLocalFileAsync(path, ct).ConfigureAwait(false);
            return await _scheduler.ReadAsync(async inner =>
            {
                if (revision is not null)
                {
                    var output = Checked(await ExecuteAsync(GitCommands.CatFileFiltered(_toplevel, GitCommands.ObjectSpec(revision, path)), null, inner).ConfigureAwait(false));
                    if (output.Stdout.LongLength > HistoryLimits.MaxPreviewBytes)
                        return BlobLoad.OverLimit;
                    return new BlobLoad(output.Stdout, false);
                }

                if (string.IsNullOrEmpty(pointerText))
                    return BlobLoad.Empty;
                var smudged = Checked(await ExecuteAsync(
                    GitCommands.LfsSmudge(_toplevel),
                    null,
                    inner,
                    standardInput: Encoding.UTF8.GetBytes(pointerText)).ConfigureAwait(false));
                if (smudged.Stdout.LongLength > HistoryLimits.MaxPreviewBytes)
                    return BlobLoad.OverLimit;
                return new BlobLoad(smudged.Stdout, false);
            }, ct).ConfigureAwait(false);
        }, cancellationToken);

    private async Task<DiffDocument> AnnotateAsync(
        DiffDocument document,
        string? path,
        string? beforeRevision,
        string? afterRevision,
        bool afterIsWorktree,
        CancellationToken cancellationToken)
    {
        if (document.IsTooLarge)
            return document;
        var notes = new List<LfsFileNote>();
        if (!string.IsNullOrEmpty(path))
        {
            var note = await NoteAsync(document, path, beforeRevision, afterRevision, afterIsWorktree, cancellationToken).ConfigureAwait(false);
            if (note is not null)
                notes.Add(note);
        }
        else if (!string.IsNullOrEmpty(document.RawPatch))
        {
            var probes = 0;
            foreach (var file in DiffParser.ParseFiles(document.RawPatch))
            {
                if (string.IsNullOrEmpty(file.Path) || !MayBePointer(file.Document))
                    continue;
                if (file.Document.IsBinary)
                {
                    if (probes >= LfsProbeLimit)
                        continue;
                    probes++;
                }

                var note = await NoteAsync(file.Document, file.Path, beforeRevision, afterRevision, afterIsWorktree, cancellationToken).ConfigureAwait(false);
                if (note is not null)
                    notes.Add(note);
            }
        }

        return notes.Count == 0 ? document : document with { LfsFiles = notes };
    }

    private async Task<LfsFileNote?> NoteAsync(
        DiffDocument document,
        string path,
        string? beforeRevision,
        string? afterRevision,
        bool afterIsWorktree,
        CancellationToken cancellationToken)
    {
        if (!document.IsBinary && LfsPointers.TryReadDiff(document, out var beforeText, out var afterText))
            return new LfsFileNote(path, beforeText, afterText, null);
        if (!document.IsBinary)
            return null;

        var before = await ReadPointerAsync(beforeRevision, path, cancellationToken).ConfigureAwait(false);
        LfsPointer? after = null;
        long? local = null;
        if (afterIsWorktree)
        {
            var full = RepoPath.CombineUnder(_toplevel, path);
            if (full is not null && TryInspectLocal(full, out var localPointer, out var length))
            {
                if (localPointer is not null)
                    after = localPointer;
                else if (before is not null)
                    local = length;
            }
        }
        else
        {
            after = await ReadPointerAsync(afterRevision, path, cancellationToken).ConfigureAwait(false);
        }

        if (before is null && after is null)
            return null;
        return new LfsFileNote(path, before, after, local);
    }

    private async Task<LfsPointer?> ReadPointerAsync(string? revision, string path, CancellationToken cancellationToken)
    {
        if (revision is null || path.Length == 0)
            return null;
        var spec = GitCommands.ObjectSpec(revision, path);
        var sizeOutput = await ExecuteAsync(GitCommands.CatFileSize(_toplevel, spec), null, cancellationToken).ConfigureAwait(false);
        Track(sizeOutput);
        if (sizeOutput.ExitCode != 0 || !TrySize(sizeOutput.Stdout, out var size) || size > HistoryLimits.LfsPointerProbeBytes)
            return null;
        var blob = await ExecuteAsync(GitCommands.CatFileBlob(_toplevel, spec), null, cancellationToken).ConfigureAwait(false);
        Track(blob);
        return blob.ExitCode == 0 && LfsPointers.TryParseBytes(blob.Stdout, out var pointer) ? pointer : null;
    }

    private async Task<byte[]?> ReadRawBlobAsync(string spec, CancellationToken cancellationToken)
    {
        var sizeOutput = await ExecuteAsync(GitCommands.CatFileSize(_toplevel, spec), null, cancellationToken).ConfigureAwait(false);
        Track(sizeOutput);
        if (sizeOutput.ExitCode != 0 || !TrySize(sizeOutput.Stdout, out var size) || !PreviewLimit.Allows(size))
            return null;
        var blob = await ExecuteAsync(GitCommands.CatFileBlob(_toplevel, spec), null, cancellationToken).ConfigureAwait(false);
        Track(blob);
        if (blob.ExitCode != 0 || blob.Stdout.LongLength > HistoryLimits.MaxPreviewBytes)
            return null;
        return blob.Stdout;
    }

    private async Task<ImageBytes> ImageSideAsync(
        string path,
        string? revision,
        bool worktree,
        CancellationToken cancellationToken)
    {
        if (worktree)
        {
            var full = RepoPath.CombineUnder(_toplevel, path);
            if (full is null || !File.Exists(full))
            {
                return Sparse()
                    ? new ImageBytes(null, "This path is not in the working tree. Sparse checkout was left as it is.")
                    : new ImageBytes(null, "No file in this version.");
            }

            if (!TryInspectLocal(full, out var pointer, out var length))
                return new ImageBytes(null, "No file in this version.");
            if (pointer is not null)
                return await ExpandWorktreePointerAsync(full, path, pointer, cancellationToken).ConfigureAwait(false);
            if (!PreviewLimit.Allows(length))
                return new ImageBytes(null, "This image is larger than 8 MB, so it was not loaded.");
            return new ImageBytes(await File.ReadAllBytesAsync(full, cancellationToken).ConfigureAwait(false), "");
        }

        if (revision is null)
            return new ImageBytes(null, "No file in this version.");
        return await _scheduler.ReadAsync(inner => ReadImageBlobAsync(revision, path, inner), cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImageBytes> ReadImageBlobAsync(string revision, string path, CancellationToken cancellationToken)
    {
        var spec = GitCommands.ObjectSpec(revision, path);
        var sizeOutput = await ExecuteAsync(GitCommands.CatFileSize(_toplevel, spec), null, cancellationToken).ConfigureAwait(false);
        Track(sizeOutput);
        if (sizeOutput.ExitCode != 0 || !TrySize(sizeOutput.Stdout, out var size))
            return new ImageBytes(null, "No file in this version.");
        if (!PreviewLimit.Allows(size))
            return new ImageBytes(null, "This image is larger than 8 MB, so it was not loaded.");
        var blob = await ExecuteAsync(GitCommands.CatFileBlob(_toplevel, spec), null, cancellationToken).ConfigureAwait(false);
        Track(blob);
        if (blob.ExitCode != 0 || blob.Stdout.LongLength > HistoryLimits.MaxPreviewBytes)
            return new ImageBytes(null, "No file in this version.");
        if (LfsPointers.TryParseBytes(blob.Stdout, out var lfs) && lfs is not null)
            return await ExpandPointerAsync(revision, path, lfs, blob.Stdout, cancellationToken).ConfigureAwait(false);
        return new ImageBytes(blob.Stdout, "");
    }

    private async Task<ImageBytes> ExpandWorktreePointerAsync(string full, string path, LfsPointer pointer, CancellationToken cancellationToken)
    {
        if (!PreviewLimit.Allows(pointer.Size))
            return new ImageBytes(null, "This image is larger than 8 MB, so it was not loaded.");
        byte[] pointerBytes;
        try
        {
            pointerBytes = await File.ReadAllBytesAsync(full, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return new ImageBytes(null, "The image could not be downloaded.");
        }
        catch (UnauthorizedAccessException)
        {
            return new ImageBytes(null, "The image could not be downloaded.");
        }

        return await _scheduler.ReadAsync(
            inner => ExpandPointerAsync(null, path, pointer, pointerBytes, inner),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Turns a pointer into image bytes on stdout. The size is checked before any smudge, and nothing is written into the worktree.
    /// </summary>
    private async Task<ImageBytes> ExpandPointerAsync(
        string? revision,
        string path,
        LfsPointer pointer,
        byte[] pointerBytes,
        CancellationToken cancellationToken)
    {
        if (!PreviewLimit.Allows(pointer.Size))
            return new ImageBytes(null, "This image is larger than 8 MB, so it was not loaded.");

        if (revision is not null)
        {
            var filtered = await ExecuteAsync(
                GitCommands.CatFileFiltered(_toplevel, GitCommands.ObjectSpec(revision, path)),
                null,
                cancellationToken).ConfigureAwait(false);
            Track(filtered);
            if (ImageBytesOf(filtered) is { } fromFilter)
                return fromFilter;
        }

        // A worktree pointer has no revision. A revision whose rev:path form did not smudge still has the pointer bytes.
        // hash-object stores that pointer, then --path runs the attribute filter. The worktree file is not written.
        if (path.Length > 0)
        {
            var stored = await ExecuteAsync(
                GitCommands.HashObject(_toplevel),
                null,
                cancellationToken,
                standardInput: pointerBytes).ConfigureAwait(false);
            Track(stored);
            var id = Encoding.UTF8.GetString(stored.Stdout).Trim();
            if (stored.ExitCode == 0 && id.Length > 0)
            {
                var filtered = await ExecuteAsync(
                    GitCommands.CatFileFilteredPath(_toplevel, path, id),
                    null,
                    cancellationToken).ConfigureAwait(false);
                Track(filtered);
                if (ImageBytesOf(filtered) is { } fromPath)
                    return fromPath;
            }
        }

        var smudged = await ExecuteAsync(
            GitCommands.LfsSmudge(_toplevel),
            null,
            cancellationToken,
            standardInput: pointerBytes).ConfigureAwait(false);
        Track(smudged);
        return ImageBytesOf(smudged) ?? new ImageBytes(null, "The image could not be downloaded.");
    }

    private static ImageBytes? ImageBytesOf(GitOutput output)
    {
        if (output.ExitCode != 0 || output.Stdout.Length == 0)
            return null;
        if (output.Stdout.LongLength > HistoryLimits.MaxPreviewBytes)
            return new ImageBytes(null, "This image is larger than 8 MB, so it was not loaded.");
        if (LfsPointers.TryParseBytes(output.Stdout, out var still) && still is not null)
            return null;
        return new ImageBytes(output.Stdout, "");
    }

    private readonly record struct ImageBytes(byte[]? Bytes, string Notice);

    private async Task<BlobLoad> ReadLocalFileAsync(string path, CancellationToken cancellationToken)
    {
        var full = RepoPath.CombineUnder(_toplevel, path);
        if (full is null || !File.Exists(full))
        {
            if (Sparse())
                throw new RepositoryActionException("Sparse checkout is on. Sextant will not check this path out.");
            throw new RepositoryActionException("That file is not in the working tree.");
        }

        var length = new FileInfo(full).Length;
        if (!PreviewLimit.Allows(length))
            return BlobLoad.OverLimit;
        return new BlobLoad(await File.ReadAllBytesAsync(full, cancellationToken).ConfigureAwait(false), false);
    }

    private bool Sparse()
    {
        lock (_stateLock)
            return ConfigParser.IsEnabled(_config, "core.sparseCheckout");
    }

    private static bool MayBePointer(DiffDocument document) =>
        document.IsBinary
        || document.RawPatch.Contains("version https://git-lfs.github.com/spec/v1", StringComparison.Ordinal);

    private static bool TrySize(byte[] data, out long size)
    {
        var text = Encoding.UTF8.GetString(data).Trim();
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out size);
    }

    private static bool TryInspectLocal(string full, out LfsPointer? pointer, out long length)
    {
        pointer = null;
        length = 0;
        try
        {
            var info = new FileInfo(full);
            if (!info.Exists)
                return false;
            length = info.Length;
            var take = (int)Math.Min(length, HistoryLimits.LfsPointerProbeBytes);
            if (take == 0)
                return true;
            var buffer = new byte[take];
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var read = stream.Read(buffer, 0, take);
            if (read > 0)
                LfsPointers.TryParseBytes(buffer.AsSpan(0, read), out pointer);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
