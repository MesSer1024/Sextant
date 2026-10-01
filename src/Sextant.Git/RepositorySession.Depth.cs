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
            var notice = new StringBuilder();
            var before = await ImageSideAsync(request.Path, request.BeforeRevision, request.BeforeIsWorktree, notice, ct).ConfigureAwait(false);
            var after = await ImageSideAsync(request.Path, request.AfterRevision, request.AfterIsWorktree, notice, ct).ConfigureAwait(false);
            if (before is null && after is null && notice.Length == 0)
                return null;
            return new ImagePreview(before, after, notice.ToString().Trim());
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

    private async Task<byte[]?> ImageSideAsync(
        string path,
        string? revision,
        bool worktree,
        StringBuilder notice,
        CancellationToken cancellationToken)
    {
        if (worktree)
        {
            var full = RepoPath.CombineUnder(_toplevel, path);
            if (full is null || !File.Exists(full))
            {
                if (Sparse())
                    notice.Append("This path is not in the working tree. Sparse checkout was left as it is. ");
                return null;
            }

            if (!TryInspectLocal(full, out var pointer, out var length))
                return null;
            if (pointer is not null)
            {
                notice.Append("Git LFS pointer. The image was not downloaded. ");
                return null;
            }

            if (!PreviewLimit.Allows(length))
            {
                notice.Append("This image is larger than 8 MB, so it was not loaded. ");
                return null;
            }

            return await File.ReadAllBytesAsync(full, cancellationToken).ConfigureAwait(false);
        }

        if (revision is null)
            return null;
        var bytes = await _scheduler.ReadAsync(
            inner => ReadRawBlobAsync(GitCommands.ObjectSpec(revision, path), inner),
            cancellationToken).ConfigureAwait(false);
        if (bytes is null)
            return null;
        if (LfsPointers.TryParseBytes(bytes, out var lfs) && lfs is not null)
        {
            notice.Append("Git LFS pointer ");
            notice.Append(lfs.Oid);
            notice.Append(". The image was not downloaded. ");
            return null;
        }

        return bytes;
    }

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
