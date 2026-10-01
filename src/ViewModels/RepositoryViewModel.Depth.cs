using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant;
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
    private bool _lfsAfter;

    [ObservableProperty]
    public partial bool SparseCheckout { get; set; }

    [ObservableProperty]
    public partial string LfsNotice { get; set; } = "";

    [ObservableProperty]
    public partial bool HasLfsNotice { get; set; }

    [ObservableProperty]
    public partial bool ShowLfsDownload { get; set; }

    public ResetCollection<ImageCompareRow> ImageCompares { get; } = new();

    [ObservableProperty]
    public partial bool ShowingImages { get; set; }

    // A newer file selection bumps this so a finished load cannot clear the spinner that replaced it.
    private int _imageLoad;

    public bool CanOpenLocation => SelectedLocation?.ShowOpen == true;

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
            // An image pointer is fetched for both sides by the image preview. The button would load only one of them.
            if (ImageFiles.IsImagePath(note.Path) && ImagePointerWithinCap(note))
                continue;

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

            if (ImageFiles.IsImagePath(note.Path))
            {
                builder.Append(". It is over 8 MB, so it stays a pointer.");
                continue;
            }

            builder.Append(". Not downloaded.");
            if (note.LocalBytes is { } local)
                builder.Append(" The working copy already has ").Append(ImageFiles.FormatBytes(local)).Append(" on disk.");

            if (!singleFile || document.LfsFiles.Count != 1)
                continue;
            var chosen = note.After ?? note.Before;
            if (note.LocalBytes is { } onDisk && PreviewLimit.Allows(onDisk))
            {
                ShowLfsDownload = true;
                _lfsLocal = true;
                _lfsAfter = true;
                _lfsPath = note.Path;
                _lfsSize = onDisk;
            }
            else if (chosen is not null && PreviewLimit.Allows(chosen.Size))
            {
                ShowLfsDownload = true;
                _lfsPath = note.Path;
                _lfsSize = chosen.Size;
                if (note.After is not null && !_afterIsWorktree && _objectAfter is not null)
                {
                    _lfsRevision = _objectAfter;
                    _lfsAfter = true;
                }
                else if (note.Before is not null && _objectBefore is not null)
                {
                    _lfsRevision = _objectBefore;
                    _lfsAfter = false;
                }
                else if (_objectBefore is not null)
                {
                    _lfsRevision = _objectBefore;
                    _lfsAfter = false;
                }
                else
                {
                    _lfsPointer = chosen.Render();
                    _lfsAfter = true;
                }
            }
            else if (chosen is not null)
            {
                builder.Append(" It is over 8 MB, so it stays a pointer.");
            }
        }

        if (builder.Length == 0)
        {
            HasLfsNotice = false;
            LfsNotice = "";
            return;
        }

        LfsNotice = builder.ToString();
        HasLfsNotice = true;
    }

    private static bool ImagePointerWithinCap(LfsFileNote note)
    {
        if (note.LocalBytes is { } local && PreviewLimit.Allows(local))
            return true;
        if (note.Before is { } before && PreviewLimit.Allows(before.Size))
            return true;
        if (note.After is { } after && PreviewLimit.Allows(after.Size))
            return true;
        return false;
    }

    private async Task LoadImageAsync(FileRowViewModel? file, bool workingCopy, bool range, CancellationToken token)
    {
        if (_session is null || token.IsCancellationRequested)
            return;
        var targets = ImageTargets(file);
        if (targets.Count == 0)
            return;

        var load = ++_imageLoad;
        var rows = new List<ImageCompareRow>(targets.Count);
        foreach (var target in targets)
            rows.Add(new ImageCompareRow(target.Path, null, null, "", "") { IsLoading = true });
        var visible = new List<ImageCompareRow>();
        var published = false;
        // A local file is often ready immediately. The spinner waits a moment so that case does not flash.
        var spinnerDelay = Task.Delay(TimeSpan.FromMilliseconds(200));
        try
        {
            foreach (var pair in targets.Zip(rows))
            {
                if (token.IsCancellationRequested || load != _imageLoad)
                    return;
                var target = pair.First;
                var row = pair.Second;
                var beforeRevision = range ? _rangeOlder : _objectBefore;
                var afterRevision = range ? _rangeNewer : _objectAfter;
                var afterWorktree = !range && _afterIsWorktree;
                if (!AllFiles && workingCopy && file?.Untracked == true && !_viewingStaged)
                {
                    beforeRevision = null;
                    afterRevision = null;
                    afterWorktree = true;
                }

                var previewTask = _session.PreviewImageAsync(
                    new ImageRequest(target.Path, beforeRevision, afterRevision, false, afterWorktree, target.BeforePath),
                    token);
                if (!published)
                {
                    var winner = await Task.WhenAny(previewTask, spinnerDelay).ConfigureAwait(false);
                    if (winner != previewTask && load == _imageLoad && !token.IsCancellationRequested)
                    {
                        await OnUi(() => published = PublishImageRows(load, rows, visible, published));
                    }
                }

                var preview = await previewTask.ConfigureAwait(false);
                if (token.IsCancellationRequested || load != _imageLoad)
                    return;
                // SVG and TIFF are drawn here, off the UI thread, so the row spinner can keep turning.
                var beforePrepared = preview is null ? null : ImageRaster.Prepare(target.Path, preview.Before);
                var afterPrepared = preview is null ? null : ImageRaster.Prepare(target.Path, preview.After);
                if (token.IsCancellationRequested || load != _imageLoad)
                    return;
                await OnUi(() =>
                {
                    if (load != _imageLoad)
                        return;
                    FillImageRow(row, preview, beforePrepared, afterPrepared, published, visible);
                });
            }

            if (!published && load == _imageLoad && !token.IsCancellationRequested)
                await OnUi(() => published = PublishImageRows(load, rows, visible, published));
        }
        finally
        {
            await OnUi(() => FinishImageLoad(load, rows, published));
        }
    }

    private bool PublishImageRows(int load, List<ImageCompareRow> rows, List<ImageCompareRow> visible, bool published)
    {
        if (load != _imageLoad || published)
            return published;
        var show = new List<ImageCompareRow>();
        foreach (var row in rows)
        {
            if (row.IsLoading || visible.Contains(row))
                show.Add(row);
        }

        ReplaceImages(show);
        return true;
    }

    private void FillImageRow(
        ImageCompareRow row,
        ImagePreview? preview,
        byte[]? beforePrepared,
        byte[]? afterPrepared,
        bool published,
        List<ImageCompareRow> visible)
    {
        if (preview is null)
        {
            row.IsLoading = false;
            row.Release();
            if (published)
                RemoveImageRow(row);
            return;
        }

        var before = DecodeBitmap(beforePrepared);
        var after = DecodeBitmap(afterPrepared);
        row.Before = before;
        row.After = after;
        row.BeforeNotice = SideNotice(preview.Before, before, preview.BeforeNotice);
        row.AfterNotice = SideNotice(preview.After, after, preview.AfterNotice);
        row.IsLoading = false;
        if (!published && !visible.Contains(row))
            visible.Add(row);
    }

    private void FinishImageLoad(int load, List<ImageCompareRow> rows, bool published)
    {
        if (load != _imageLoad)
        {
            if (!published)
            {
                foreach (var row in rows)
                    row.Release();
            }

            return;
        }

        if (!published)
        {
            foreach (var row in rows)
                row.Release();
            return;
        }

        var pending = new List<ImageCompareRow>();
        foreach (var row in ImageCompares)
        {
            if (row.IsLoading)
                pending.Add(row);
        }

        foreach (var row in pending)
            RemoveImageRow(row);
    }

    private void RemoveImageRow(ImageCompareRow row)
    {
        row.Release();
        ImageCompares.Remove(row);
        ShowingImages = ImageCompares.Count > 0;
    }

    private async Task OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }

    private List<(string Path, string? BeforePath)> ImageTargets(FileRowViewModel? file)
    {
        var targets = new List<(string Path, string? BeforePath)>();
        void Add(string? path, string? before)
        {
            if (string.IsNullOrEmpty(path) || !ImageFiles.IsImagePath(path))
                return;
            if (targets.Exists(item => string.Equals(item.Path, path, StringComparison.Ordinal)))
                return;
            if (string.Equals(before, path, StringComparison.Ordinal))
                before = null;
            targets.Add((path, before));
        }

        if (!AllFiles)
        {
            if (file is not null)
                Add(file.Path, file.OriginalPath ?? (_rawPatch is null ? null : RenameSource(_rawPatch)));
            return targets;
        }

        if (!string.IsNullOrEmpty(_rawPatch))
        {
            foreach (var entry in DiffParser.ParseFiles(_rawPatch))
                Add(entry.Path, RenameSource(entry.Document.RawPatch));
        }

        if (file is { Untracked: true })
            Add(file.Path, null);
        return targets;
    }

    private static string? RenameSource(string patch)
    {
        var normalized = patch.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            if (!line.StartsWith("rename from ", StringComparison.Ordinal))
                continue;
            var path = line["rename from ".Length..];
            if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
                path = path[1..^1];
            return path.Length == 0 ? null : path;
        }

        return null;
    }

    private static string SideNotice(byte[]? bytes, Bitmap? bitmap, string previewNotice)
    {
        if (bitmap is not null)
            return "";
        if (bytes is { Length: > 0 })
            return "This image could not be decoded.";
        return previewNotice;
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
            var bitmap = DecodeImage(path, loaded.Bytes);
            if (bitmap is null)
            {
                PlaceImageNotice(path, "The file was loaded, but it could not be decoded as an image.");
                return;
            }

            PlaceImage(path, bitmap);
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
        _lfsAfter = false;
        ReplaceImages([]);
    }

    private void ReplaceImages(IReadOnlyList<ImageCompareRow> rows)
    {
        foreach (var old in ImageCompares)
            old.Release();
        ImageCompares.Reset(rows);
        ShowingImages = ImageCompares.Count > 0;
    }

    private void PlaceImage(string path, Bitmap bitmap)
    {
        var row = ImageCompares.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.Ordinal));
        if (row is null)
        {
            row = new ImageCompareRow(path, null, null, "", "");
            ImageCompares.Add(row);
            ShowingImages = true;
        }

        if (_lfsAfter)
        {
            row.After = bitmap;
            row.AfterNotice = "";
        }
        else
        {
            row.Before = bitmap;
            row.BeforeNotice = "";
        }
    }

    private void PlaceImageNotice(string path, string notice)
    {
        var row = ImageCompares.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.Ordinal));
        if (row is null)
        {
            row = new ImageCompareRow(path, null, null, _lfsAfter ? "" : notice, _lfsAfter ? notice : "");
            ImageCompares.Add(row);
            ShowingImages = true;
            return;
        }

        if (_lfsAfter)
            row.AfterNotice = notice;
        else
            row.BeforeNotice = notice;
    }

    private static Bitmap? DecodeImage(string path, byte[]? data) => DecodeBitmap(ImageRaster.Prepare(path, data));

    private static Bitmap? DecodeBitmap(byte[]? data)
    {
        if (data is null || data.Length == 0)
            return null;
        try
        {
            using var stream = new MemoryStream(data, writable: false);
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

public sealed class ImageCompareRow : ObservableObject
{
    private Bitmap? _before;
    private Bitmap? _after;
    private string _beforeNotice;
    private string _afterNotice;

    public ImageCompareRow(string path, Bitmap? before, Bitmap? after, string beforeNotice, string afterNotice)
    {
        Path = path;
        _before = before;
        _after = after;
        _beforeNotice = beforeNotice;
        _afterNotice = afterNotice;
    }

    public string Path { get; }

    private bool _loading;

    public bool IsLoading
    {
        get => _loading;
        set => SetProperty(ref _loading, value);
    }

    public Bitmap? Before
    {
        get => _before;
        set => SetBitmap(ref _before, value, nameof(Before), nameof(HasBefore), nameof(ShowBeforeNotice), nameof(BeforeCaption));
    }

    public Bitmap? After
    {
        get => _after;
        set => SetBitmap(ref _after, value, nameof(After), nameof(HasAfter), nameof(ShowAfterNotice), nameof(AfterCaption));
    }

    public string BeforeNotice
    {
        get => _beforeNotice;
        set
        {
            if (_beforeNotice == value)
                return;
            _beforeNotice = value;
            OnPropertyChanged(nameof(BeforeNotice));
            OnPropertyChanged(nameof(ShowBeforeNotice));
        }
    }

    public string AfterNotice
    {
        get => _afterNotice;
        set
        {
            if (_afterNotice == value)
                return;
            _afterNotice = value;
            OnPropertyChanged(nameof(AfterNotice));
            OnPropertyChanged(nameof(ShowAfterNotice));
        }
    }

    public bool HasBefore => _before is not null;

    public bool HasAfter => _after is not null;

    public bool ShowBeforeNotice => _before is null && _beforeNotice.Length > 0;

    public bool ShowAfterNotice => _after is null && _afterNotice.Length > 0;

    public string BeforeCaption => Caption("Before", _before);

    public string AfterCaption => Caption("After", _after);

    public void Release()
    {
        Before = null;
        After = null;
    }

    private void SetBitmap(ref Bitmap? field, Bitmap? value, params string[] names)
    {
        if (ReferenceEquals(field, value))
            return;
        var old = field;
        field = value;
        foreach (var name in names)
            OnPropertyChanged(name);
        if (!ReferenceEquals(old, value))
            old?.Dispose();
    }

    private static string Caption(string side, Bitmap? bitmap) =>
        bitmap is null ? side : side + "  " + bitmap.PixelSize.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "×" + bitmap.PixelSize.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
