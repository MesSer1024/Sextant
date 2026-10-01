using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant;
using Sextant.Git;
using Sextant.Git.Parsing;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Sextant.ViewModels;

public partial class RepositoryViewModel
{
    private string? _rangeOlder;
    private string? _rangeNewer;
    private string _rangeOlderSubject = "";
    private string _rangeNewerSubject = "";

    public ObservableCollection<BlameRow> BlameRows { get; } = [];

    [ObservableProperty]
    public partial string HistoryText { get; set; } = "";

    [ObservableProperty]
    public partial string HistoryCaption { get; set; } = "";

    [ObservableProperty]
    public partial bool HasHistoryFilter { get; set; }

    [ObservableProperty]
    public partial bool ShowHistorySearch { get; set; }

    public bool ShowHistoryChrome => ShowHistorySearch || HasHistoryFilter;

    [ObservableProperty]
    public partial string ConflictText { get; set; } = "";

    [ObservableProperty]
    public partial bool IgnoreWhitespace { get; set; }

    [ObservableProperty]
    public partial bool SideBySide { get; set; }

    [ObservableProperty]
    public partial bool AllFiles { get; set; }

    [ObservableProperty]
    public partial bool ShowingBlame { get; set; }

    [ObservableProperty]
    public partial bool ShowingMerge { get; set; }

    [ObservableProperty]
    public partial bool ShowMergeBase { get; set; }

    public ResetCollection<MergeRegionRow> MergeRegions { get; } = [];

    private string? _mergePath;

    public bool ShowingDiff => !ShowingBlame && !ShowingMerge;

    public string SideBySideLabel => SideBySide ? "Inline" : "Side by side";

    public string MergeBaseLabel => ShowMergeBase ? "Hide base" : "Show base";

    public string WhitespaceLabel => IgnoreWhitespace ? "Show whitespace" : "Ignore whitespace";

    public string FilesModeLabel => AllFiles ? "Selected file" : "All files";

    public string BlameLabel => ShowingBlame ? "Diff" : "Blame";

    public bool CanRenameLocation => SelectedLocation?.ShowRename == true;

    public bool CanPopLocation => SelectedLocation?.ShowPop == true;

    public bool CanApplyLocation => SelectedLocation?.ShowApply == true;

    public bool CanDropLocation => SelectedLocation?.ShowDrop == true;

    partial void OnSideBySideChanged(bool value) => OnPropertyChanged(nameof(SideBySideLabel));

    partial void OnIgnoreWhitespaceChanged(bool value) => OnPropertyChanged(nameof(WhitespaceLabel));

    partial void OnAllFilesChanged(bool value) => OnPropertyChanged(nameof(FilesModeLabel));

    partial void OnShowingBlameChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowingDiff));
        OnPropertyChanged(nameof(BlameLabel));
    }

    partial void OnShowingMergeChanged(bool value) => OnPropertyChanged(nameof(ShowingDiff));

    partial void OnHasHistoryFilterChanged(bool value) => OnPropertyChanged(nameof(ShowHistoryChrome));

    partial void OnShowHistorySearchChanged(bool value) => OnPropertyChanged(nameof(ShowHistoryChrome));

    [RelayCommand]
    private void ToggleHistorySearch() => ShowHistorySearch = !ShowHistorySearch;

    partial void OnShowMergeBaseChanged(bool value)
    {
        OnPropertyChanged(nameof(MergeBaseLabel));
        foreach (var row in MergeRegions)
            row.ShowBase = value;
    }

    [RelayCommand]
    private void ToggleMergeBase() => ShowMergeBase = !ShowMergeBase;

    [RelayCommand]
    public async Task SaveConflict()
    {
        if (_session is null || !ShowingMerge || string.IsNullOrEmpty(_mergePath))
            return;
        var pieces = new List<ConflictPiece>(MergeRegions.Count);
        foreach (var row in MergeRegions)
        {
            pieces.Add(row.IsConflict
                ? new ConflictPiece(true, "", row.Ours, row.Theirs, row.HasBase ? row.BaseText : null, row.Result)
                : ConflictPiece.FromContext(row.Context));
        }

        var text = ConflictParser.Compose(pieces);
        var path = _mergePath;
        await RunAsync("Saving resolution…", ct => _session.SaveResolutionAsync(path, text, ct));
    }

    public void NoteGraphSelection(IReadOnlyList<GraphRowViewModel> rows)
    {
        if (_applying || _session is null)
            return;
        var commits = rows.Where(row => !row.IsWorkingCopy && row.Sha is not null).ToList();
        _selectedShas.Clear();
        _selectedShas.AddRange(commits.Select(row => row.Sha!));
        if (commits.Count >= 2)
        {
            var ordered = commits
                .Select(row => (Row: row, Index: Rows.IndexOf(row)))
                .Where(item => item.Index >= 0)
                .OrderByDescending(item => item.Index)
                .Select(item => item.Row)
                .ToList();
            if (ordered.Count < 2)
                return;
            _rangeOlder = ordered[0].Sha;
            _rangeNewer = ordered[^1].Sha;
            _rangeOlderSubject = ordered[0].Subject;
            _rangeNewerSubject = ordered[^1].Subject;
            _ = LoadRangeAsync();
            return;
        }

        var wasRange = _rangeOlder is not null;
        _rangeOlder = null;
        _rangeNewer = null;
        if (wasRange)
            _ = LoadDetailsAsync();
    }

    [RelayCommand]
    private Task SearchHistory()
    {
        var query = HistoryQueryParser.Parse(HistoryText);
        return RunAsync("Searching…", ct => _session!.SetHistoryAsync(query.IsEmpty ? null : query, ct));
    }

    [RelayCommand]
    private Task ClearHistory()
    {
        HistoryText = "";
        return RunAsync("Loading history…", ct => _session!.SetHistoryAsync(null, ct));
    }

    [RelayCommand]
    private Task Stash() => StashAsync();

    [RelayCommand]
    private Task AddRemote() => AddRemoteAsync();

    [RelayCommand]
    private Task ToggleWhitespace()
    {
        IgnoreWhitespace = !IgnoreWhitespace;
        return ReloadDiffViewAsync();
    }

    [RelayCommand]
    private Task ToggleSideBySide()
    {
        SideBySide = !SideBySide;
        return ReloadDiffViewAsync();
    }

    [RelayCommand]
    private Task ToggleAllFiles()
    {
        AllFiles = !AllFiles;
        return ReloadDiffViewAsync();
    }

    [RelayCommand]
    private Task ToggleBlame()
    {
        ShowingBlame = !ShowingBlame;
        return LoadDiffAsync();
    }

    [RelayCommand]
    private Task RenameSelected()
    {
        if (SelectedLocation?.ShowRename == true)
            SelectedLocation.RenameCommand.Execute(null);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task PopSelected()
    {
        if (SelectedLocation?.ShowPop == true)
            SelectedLocation.PopCommand.Execute(null);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ApplySelected()
    {
        if (SelectedLocation?.ShowApply == true)
            SelectedLocation.ApplyCommand.Execute(null);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task DropSelected()
    {
        if (SelectedLocation?.ShowDrop == true)
            SelectedLocation.DropCommand.Execute(null);
        return Task.CompletedTask;
    }

    private Task ShowFileHistoryAsync(string path) =>
        RunAsync("Loading file history…", ct => _session!.SetHistoryAsync(HistoryQuery.ForPath(path), ct));

    private Task StashAsync()
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var message = await dialogs.PromptAsync("Stash", "Stash message. Leave it blank to let git describe the stash.", allowEmpty: true);
            if (message is null || _session is null)
                return;
            await RunAsync("Stashing…", ct => _session.StashPushAsync(message, ct));
        });
    }

    private Task AddRemoteAsync()
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var name = await dialogs.PromptAsync("Add remote", "Remote name");
            if (string.IsNullOrWhiteSpace(name) || _session is null)
                return;
            var url = await dialogs.PromptAsync("Add remote", "Remote URL or path");
            if (string.IsNullOrWhiteSpace(url) || _session is null)
                return;
            await RunAsync("Adding remote…", ct => _session.AddRemoteAsync(name, url, ct));
        });
    }

    private Task RemoveRemoteAsync(string name)
    {
        return ConfirmRun("Remove remote", $"Remove remote {name}?", "Remove", "Removing remote…", ct => _session!.RemoveRemoteAsync(name, ct));
    }

    private Task RenameRemoteAsync(string name)
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var next = await dialogs.PromptAsync("Rename remote", $"New name for {name}", name);
            if (string.IsNullOrWhiteSpace(next) || next == name || _session is null)
                return;
            await RunAsync("Renaming remote…", ct => _session.RenameRemoteAsync(name, next, ct));
        });
    }

    private Task DeleteTagAsync(string name) =>
        ConfirmRun("Delete tag", $"Delete tag {name}?", "Delete", "Deleting tag…", ct => _session!.DeleteTagAsync(name, ct));

    private Task PopStashAsync(StashEntry stash) =>
        ConfirmRun("Pop stash", $"Pop {stash.Ref}? Git will try to apply it and then drop it.", "Pop", "Popping stash…", ct => _session!.StashPopAsync(stash.Ref, ct));

    private Task ApplyStashAsync(StashEntry stash) =>
        RunAsync("Applying stash…", ct => _session!.StashApplyAsync(stash.Ref, ct));

    private Task DropStashAsync(StashEntry stash) =>
        ConfirmRun("Drop stash", $"Drop {stash.Ref}? The stashed changes will be discarded.", "Drop", "Dropping stash…", ct => _session!.StashDropAsync(stash.Ref, ct));

    private Task ResetAsync(CommitRecord commit, string mode)
    {
        if (mode == "--hard")
        {
            var head = _session?.Snapshot().Branch;
            var discarded = head?.Oid is { } oid && !string.Equals(oid, commit.Sha, StringComparison.OrdinalIgnoreCase)
                ? $" Commit {Short(oid)} will be discarded."
                : "";
            return ConfirmRun(
                "Hard reset",
                $"Hard reset to {Short(commit.Sha)} {commit.Subject}? Uncommitted changes will be discarded.{discarded}",
                "Reset hard",
                "Resetting…",
                ct => _session!.ResetAsync(mode, commit.Sha, ct));
        }

        var label = mode == "--soft" ? "Resetting soft…" : "Resetting mixed…";
        return RunAsync(label, ct => _session!.ResetAsync(mode, commit.Sha, ct));
    }

    private Task CherryPickAsync(CommitRecord commit) =>
        RunAsync("Cherry-picking…", ct => _session!.CherryPickAsync(commit.Sha, ct));

    private Task RevertAsync(CommitRecord commit) =>
        RunAsync("Reverting…", ct => _session!.RevertAsync(commit.Sha, ct));

    private Task TagAsync(CommitRecord commit)
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var name = await dialogs.PromptAsync("Create tag", $"Tag name for {Short(commit.Sha)}");
            if (string.IsNullOrWhiteSpace(name) || _session is null)
                return;
            await RunAsync("Tagging…", ct => _session.CreateTagAsync(name, commit.Sha, ct));
        });
    }

    private Task ConfirmRun(string title, string message, string confirm, string busy, Func<CancellationToken, Task> action)
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var ok = await dialogs.ConfirmAsync(title, message, confirm);
            if (!ok || _session is null)
                return;
            await RunAsync(busy, action);
        });
    }

    private Task ReloadDiffViewAsync()
    {
        if (ShowingBlame)
            ShowingBlame = false;
        return LoadDiffAsync();
    }

    private async Task LoadRangeAsync()
    {
        if (_session is null || _rangeOlder is null || _rangeNewer is null || _lifetime.IsCancellationRequested)
            return;
        ShowingWorkingCopy = false;
        ShowingCommit = true;
        if (ShowingBlame)
            ShowingBlame = false;
        CommitTitle = Short(_rangeOlder) + ".." + Short(_rangeNewer);
        CommitMeta = _rangeOlderSubject + "  →  " + _rangeNewerSubject;
        _shownSha = null;
        ReplaceDetails();
        var token = _details!.Token;
        try
        {
            var files = await _session.RangeFilesAsync(_rangeOlder, _rangeNewer, token);
            if (files is null || token.IsCancellationRequested)
                return;
            ShowCommitFiles(files);
            await LoadDiffAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
        }
    }

    private async Task LoadBlameAsync()
    {
        if (_lifetime.IsCancellationRequested)
            return;
        ClearMerge();
        var file = SelectedFile;
        if (file is null || file.IsHeader || _session is null)
        {
            BlameRows.Clear();
            ClearDiff("Select a file to blame.");
            return;
        }

        ReplaceDetails();
        var token = _details!.Token;
        var revision = _rangeNewer ?? (SelectedGraphRow is { IsWorkingCopy: false, Sha: { } sha } ? sha : null);
        try
        {
            var document = await _session.BlameAsync(revision, file.Path, _allowLarge, token);
            if (document is null || token.IsCancellationRequested)
                return;
            BlameRows.Clear();
            DiffRows.Clear();
            ShowLoadDiff = document.IsTooLarge;
            if (document.IsTooLarge)
            {
                HasDiffNotice = true;
                DiffNotice = "This blame is large. Load it only if you need the whole file.";
                return;
            }

            HasDiffNotice = false;
            DiffNotice = "";
            foreach (var line in document.Lines)
            {
                var who = line.Uncommitted ? "Not committed" : line.Author;
                var id = line.Sha.Length <= 7 ? line.Sha : line.Sha[..7];
                var number = line.Number.ToString(CultureInfo.InvariantCulture);
                var meta = id + "  " + who;
                var count = LineFold.Count(line.Text);
                for (var index = 0; index < count; index++)
                {
                    BlameRows.Add(new BlameRow
                    {
                        Number = index == 0 ? number : "",
                        Meta = index == 0 ? meta : "",
                        Text = LineFold.Piece(line.Text, index) ?? "",
                        Continues = index > 0,
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
        }
    }
}
