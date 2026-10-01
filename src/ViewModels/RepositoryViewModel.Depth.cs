using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;
using Sextant.Git.Parsing;
using System.Text;

namespace Sextant.ViewModels;

public partial class RepositoryViewModel
{
    private string? _objectBefore;
    private string? _objectAfter;
    private bool _afterIsWorktree;
    private string? _lineLanguage;
    private bool _lfsLocal;
    private string? _lfsRevision;
    private string? _lfsPointer;
    private string _lfsPath = "";
    private long _lfsSize;

    [ObservableProperty]
    public partial bool SparseCheckout { get; set; }

    [ObservableProperty]
    public partial string LfsNotice { get; set; } = "";

    [ObservableProperty]
    public partial bool HasLfsNotice { get; set; }

    [ObservableProperty]
    public partial bool ShowLfsDownload { get; set; }

    [ObservableProperty]
    public partial string ImageNotice { get; set; } = "";

    [ObservableProperty]
    public partial bool HasImageNotice { get; set; }

    [ObservableProperty]
    public partial Bitmap? ImageBefore { get; set; }

    [ObservableProperty]
    public partial Bitmap? ImageAfter { get; set; }

    [ObservableProperty]
    public partial bool HasImageBefore { get; set; }

    [ObservableProperty]
    public partial bool HasImageAfter { get; set; }

    public bool ShowingImage => HasImageBefore || HasImageAfter;

    public bool CanOpenLocation => SelectedLocation?.ShowOpen == true;

    partial void OnHasImageBeforeChanged(bool value) => OnPropertyChanged(nameof(ShowingImage));

    partial void OnHasImageAfterChanged(bool value) => OnPropertyChanged(nameof(ShowingImage));

    [RelayCommand]
    private async Task AddWorktree()
    {
        if (_session is null || IsBusy || _host.Dialogs is null)
            return;
        var path = await _host.Dialogs.PromptAsync("Add worktree", "Folder for the new worktree. It must not exist yet.");
        if (string.IsNullOrWhiteSpace(path))
            return;
        var branch = await _host.Dialogs.PromptAsync(
            "Add worktree",
            "New branch name. Leave blank to check out an existing branch, or to let git name one from the folder.",
            allowEmpty: true);
        if (branch is null)
            return;
        string? start = null;
        if (string.IsNullOrWhiteSpace(branch))
        {
            branch = null;
            start = await _host.Dialogs.PromptAsync(
                "Add worktree",
                "Existing branch or commit. Leave blank to start at HEAD.",
                allowEmpty: true);
            if (start is null)
                return;
            if (string.IsNullOrWhiteSpace(start))
                start = null;
        }

        await RunAsync("Adding worktree…", ct => _session.AddWorktreeAsync(path, branch, start, ct));
    }

    [RelayCommand]
    private async Task ShowLfs()
    {
        if (_session is null || IsBusy || _lfsPath.Length == 0)
            return;
        var path = _lfsPath;
        var revision = _lfsRevision;
        var local = _lfsLocal;
        var size = _lfsSize;
        var pointer = _lfsPointer;
        try
        {
            IsBusy = true;
            BusyText = local ? "Reading file…" : "Loading file…";
            var loaded = await _session.LoadRequestedBlobAsync(path, revision, local, size, pointer, _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
                return;
            if (!Dispatcher.UIThread.CheckAccess())
            {
                await Dispatcher.UIThread.InvokeAsync(() => ShowLoaded(path, loaded));
                return;
            }

            ShowLoaded(path, loaded);
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
        }
        catch (RepositoryActionException exception)
        {
            Fail(exception.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }
    }

    private Task OpenSubmoduleAsync(SubmoduleEntry module)
    {
        if (module.State == SubmoduleState.Uninitialized)
        {
            Fail("This submodule is not checked out. Sextant will not download it.");
            return Task.CompletedTask;
        }

        var root = Toplevel ?? _session?.Toplevel;
        var full = root is null ? null : RepoPath.CombineUnder(root, module.Path);
        if (full is null)
        {
            Fail("That submodule path is outside the repository.");
            return Task.CompletedTask;
        }

        return _host.OpenRepositoryAsync(full);
    }

    private void RememberObjects(bool range, bool workingCopy, FileRowViewModel? file)
    {
        if (range)
        {
            _objectBefore = _rangeOlder;
            _objectAfter = _rangeNewer;
            _afterIsWorktree = false;
            return;
        }

        if (workingCopy && file?.Untracked == true && !AllFiles && !_viewingStaged)
        {
            _objectBefore = null;
            _objectAfter = null;
            _afterIsWorktree = true;
            return;
        }

        if (workingCopy && _viewingStaged)
        {
            _objectBefore = "HEAD";
            _objectAfter = "";
            _afterIsWorktree = false;
            return;
        }

        if (workingCopy)
        {
            _objectBefore = "";
            _objectAfter = null;
            _afterIsWorktree = true;
            return;
        }

        _objectBefore = _diffParent ?? (SelectedGraphRow?.Commit?.Parents.Count > 0 ? SelectedGraphRow.Commit.Parents[0] : null);
        _objectAfter = SelectedGraphRow?.Sha;
        _afterIsWorktree = false;
    }

    private void NoteLfs(DiffDocument document, bool singleFile)
    {
        ShowLfsDownload = false;
        _lfsLocal = false;
        _lfsRevision = null;
        _lfsPointer = null;
        _lfsPath = "";
        _lfsSize = 0;
        if (document.LfsFiles.Count == 0)
        {
            HasLfsNotice = false;
            LfsNotice = "";
            return;
        }

        var builder = new StringBuilder();
        foreach (var note in document.LfsFiles)
        {
            if (builder.Length > 0)
                builder.AppendLine();
            if (note.Path.Length > 0)
                builder.Append(note.Path).Append(": ");
            builder.Append("Git LFS pointer");
            if (note.Before is { } before && note.After is { } after && !string.Equals(before.Oid, after.Oid, StringComparison.Ordinal))
            {
                builder.Append(". Before ").Append(before.Oid).Append(" (").Append(ImageFiles.FormatBytes(before.Size)).Append(')');
                builder.Append(", after ").Append(after.Oid).Append(" (").Append(ImageFiles.FormatBytes(after.Size)).Append(')');
            }
            else if ((note.After ?? note.Before) is { } pointer)
            {
                builder.Append(' ').Append(pointer.Oid).Append(" (").Append(ImageFiles.FormatBytes(pointer.Size)).Append(')');
            }

            builder.Append(". Not downloaded.");
            if (note.LocalBytes is { } local)
                builder.Append(" The working copy already has ").Append(ImageFiles.FormatBytes(local)).Append(" on disk.");
        }

        var offer = singleFile ? document.LfsFiles.Count == 1 ? document.LfsFiles[0] : null : null;
        if (offer is not null)
        {
            var chosen = offer.After ?? offer.Before;
            if (offer.LocalBytes is { } onDisk && PreviewLimit.Allows(onDisk))
            {
                ShowLfsDownload = true;
                _lfsLocal = true;
                _lfsPath = offer.Path;
                _lfsSize = onDisk;
            }
            else if (chosen is not null && PreviewLimit.Allows(chosen.Size))
            {
                ShowLfsDownload = true;
                _lfsPath = offer.Path;
                _lfsSize = chosen.Size;
                if (offer.After is not null && !_afterIsWorktree && _objectAfter is not null)
                    _lfsRevision = _objectAfter;
                else if (offer.Before is not null && _objectBefore is not null)
                    _lfsRevision = _objectBefore;
                else if (_objectBefore is not null)
                    _lfsRevision = _objectBefore;
                else
                    _lfsPointer = chosen.Render();
            }
            else if (chosen is not null)
            {
                builder.Append(" It is over 8 MB, so it stays a pointer.");
            }
        }

        LfsNotice = builder.ToString();
        HasLfsNotice = true;
    }

    private async Task LoadImageAsync(FileRowViewModel? file, bool workingCopy, bool range, CancellationToken token)
    {
        if (_session is null || AllFiles || file is null || !ImageFiles.IsImagePath(file.Path))
            return;
        ImageRequest request;
        if (range)
            request = new ImageRequest(file.Path, _rangeOlder, _rangeNewer, false, false);
        else if (workingCopy && file.Untracked)
            request = new ImageRequest(file.Path, null, null, false, true);
        else if (workingCopy && _viewingStaged)
            request = new ImageRequest(file.Path, "HEAD", "", false, false);
        else if (workingCopy)
            request = new ImageRequest(file.Path, "", null, false, true);
        else
            request = new ImageRequest(file.Path, _objectBefore, _objectAfter, false, false);

        var preview = await _session.PreviewImageAsync(request, token);
        if (preview is null || token.IsCancellationRequested)
            return;
        void Apply()
        {
            if (token.IsCancellationRequested)
                return;
            ImageNotice = preview.Notice;
            HasImageNotice = preview.Notice.Length > 0;
            SetImages(DecodeImage(preview.Before), DecodeImage(preview.After));
            if ((preview.Before is { Length: > 0 } && ImageBefore is null) || (preview.After is { Length: > 0 } && ImageAfter is null))
            {
                ImageNotice = string.IsNullOrEmpty(preview.Notice)
                    ? "This image could not be decoded."
                    : preview.Notice;
                HasImageNotice = true;
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            await Dispatcher.UIThread.InvokeAsync(Apply);
    }

    private void ShowLoaded(string path, BlobLoad loaded)
    {
        if (loaded.TooLarge)
        {
            LfsNotice = "The file is over 8 MB, so it stays a pointer.";
            HasLfsNotice = true;
            ShowLfsDownload = false;
            return;
        }

        if (loaded.Bytes.Length == 0)
        {
            LfsNotice = "Git returned an empty file.";
            HasLfsNotice = true;
            return;
        }

        if (ImageFiles.IsImagePath(path))
        {
            var bitmap = DecodeImage(loaded.Bytes);
            if (bitmap is null)
            {
                ImageNotice = "The file was loaded, but it could not be decoded as an image.";
                HasImageNotice = true;
                return;
            }

            SetImages(null, bitmap);
            ImageNotice = "Loaded " + ImageFiles.FormatBytes(loaded.Bytes.Length) + ".";
            HasImageNotice = true;
            ShowLfsDownload = false;
            return;
        }

        if (!IsText(loaded.Bytes))
        {
            LfsNotice = "Loaded " + ImageFiles.FormatBytes(loaded.Bytes.Length) + ". It is not text or a previewable image, so the bytes are not shown.";
            HasLfsNotice = true;
            ShowLfsDownload = false;
            return;
        }

        if (loaded.Bytes.Length > HistoryLimits.MaxDiffBytes)
        {
            LfsNotice = "Loaded " + ImageFiles.FormatBytes(loaded.Bytes.Length) + ". It is too large to list here.";
            HasLfsNotice = true;
            ShowLfsDownload = false;
            return;
        }

        var text = Encoding.UTF8.GetString(loaded.Bytes).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = text.Split('\n');
        if (lines.Length > HistoryLimits.MaxDiffLines)
        {
            LfsNotice = "Loaded " + lines.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " lines. That is too many to list here.";
            HasLfsNotice = true;
            ShowLfsDownload = false;
            return;
        }

        DiffRows.Clear();
        var language = DiffSyntax.Language(path);
        DiffRows.Add(new DiffFileRow { Label = path + "  (loaded)" });
        foreach (var line in lines)
        {
            DiffRows.Add(new DiffLineRow
            {
                Text = "  " + line,
                Language = language,
                Background = DiffColors.Clear,
            });
        }

        ShowLfsDownload = false;
        HasLfsNotice = true;
        LfsNotice = "Loaded " + ImageFiles.FormatBytes(loaded.Bytes.Length) + ".";
    }

    private void ClearPreview()
    {
        HasLfsNotice = false;
        LfsNotice = "";
        ShowLfsDownload = false;
        _lfsPath = "";
        _lfsRevision = null;
        _lfsPointer = null;
        _lfsLocal = false;
        HasImageNotice = false;
        ImageNotice = "";
        SetImages(null, null);
    }

    private void SetImages(Bitmap? before, Bitmap? after)
    {
        var oldBefore = ImageBefore;
        var oldAfter = ImageAfter;
        ImageBefore = before;
        ImageAfter = after;
        HasImageBefore = before is not null;
        HasImageAfter = after is not null;
        if (!ReferenceEquals(oldBefore, before))
            oldBefore?.Dispose();
        if (!ReferenceEquals(oldAfter, after))
            oldAfter?.Dispose();
    }

    private static Bitmap? DecodeImage(byte[]? data)
    {
        if (data is null || data.Length == 0)
            return null;
        try
        {
            using var stream = new MemoryStream(data);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsText(byte[] data)
    {
        var take = Math.Min(data.Length, 8000);
        for (var i = 0; i < take; i++)
        {
            if (data[i] == 0)
                return false;
        }

        try
        {
            _ = new UTF8Encoding(false, true).GetString(data);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
