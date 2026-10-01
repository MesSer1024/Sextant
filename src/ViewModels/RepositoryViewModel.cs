using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;
using Sextant.Git.Parsing;
using Sextant.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Sextant.ViewModels;

public partial class RepositoryViewModel : ViewModelBase
{
    private readonly IWorkspaceHost _host;
    private readonly CancellationTokenSource _lifetime = new();
    private RepositorySession? _session;
    private GitDirectoryWatcher? _watcher;
    private CancellationTokenSource? _operation;
    private CancellationTokenSource? _details;
    private Task? _load;
    private bool _applying;
    private bool _askedPerformance;
    private bool _watcherFailed;
    private bool _loadingMore;
    private int _historyGeneration;
    private int _seenCommits;
    private string _entrySignature = "";
    private readonly List<FileRowViewModel> _workingFiles = [];
    private bool _showingCommitFiles;
    private string _refSignature = "";
    private string _commandSignature = "";
    private string? _rawPatch;
    private bool _viewingStaged;
    private bool _wasMerge;
    private string? _shownSha;

    public RepositoryViewModel(IWorkspaceHost host, string requestedPath)
    {
        _host = host;
        RequestedPath = requestedPath;
        Title = System.IO.Path.GetFileName(requestedPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(Title))
            Title = requestedPath;
    }

    public string RequestedPath { get; }

    public double LocationsWidth => _host.LocationsWidth;

    public double GraphWidth => _host.GraphWidth;

    public double FilesHeight => _host.FilesHeight;

    public ObservableCollection<GraphRowViewModel> Rows { get; } = [];

    public ResetCollection<LocationItem> Locations { get; } = [];

    private readonly List<LocationItem> _locationRoots = [];

    private readonly HashSet<string> _collapsedLocations = new(StringComparer.Ordinal);

    public ObservableCollection<FileRowViewModel> Files { get; } = [];

    public ObservableCollection<DiffRow> DiffRows { get; } = [];

    public ObservableCollection<string> CommandLines { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string? Toplevel { get; set; }

    [ObservableProperty]
    public partial string BranchText { get; set; } = "";

    [ObservableProperty]
    public partial string AheadBehindText { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowAheadBehind { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial bool IsConflicted { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool CanCancel { get; set; }

    [ObservableProperty]
    public partial string BusyText { get; set; } = "";

    [ObservableProperty]
    public partial string Banner { get; set; } = "";

    [ObservableProperty]
    public partial bool HasBanner { get; set; }

    [ObservableProperty]
    public partial string CommitMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowingWorkingCopy { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowingCommit { get; set; }

    [ObservableProperty]
    public partial string CommitTitle { get; set; } = "";

    [ObservableProperty]
    public partial string CommitMeta { get; set; } = "";

    [ObservableProperty]
    public partial bool NothingStaged { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowLoadMore { get; set; }

    [ObservableProperty]
    public partial string LoadMoreText { get; set; } = "Load more";

    [ObservableProperty]
    public partial bool ShowLoadDiff { get; set; }

    [ObservableProperty]
    public partial string DiffNotice { get; set; } = "";

    [ObservableProperty]
    public partial bool HasDiffNotice { get; set; }

    [ObservableProperty]
    public partial bool CommandsOpen { get; set; }

    [ObservableProperty]
    public partial GraphRowViewModel? SelectedGraphRow { get; set; }

    [ObservableProperty]
    public partial FileRowViewModel? SelectedFile { get; set; }

    [ObservableProperty]
    public partial LocationItem? SelectedLocation { get; set; }

    public bool IsReady => _session is not null;

    public bool CanCommit => !IsBusy && ShowingWorkingCopy && !string.IsNullOrWhiteSpace(CommitMessage);

    public bool CanStageAll => !IsBusy && ShowingWorkingCopy && _hasUnstagedWork;

    public bool CanUnstageAll => !IsBusy && ShowingWorkingCopy && _hasStagedWork;

    private bool _hasUnstagedWork;

    private bool _hasStagedWork;

    public bool CanRunCommands => !IsBusy;

    public bool CanCheckoutLocation => SelectedLocation?.ShowCheckout == true;

    public bool CanMergeLocation => SelectedLocation?.ShowMerge == true;

    public bool CanDeleteLocation => SelectedLocation?.ShowDelete == true;

    public bool CanUpstreamLocation => SelectedLocation?.ShowSetUpstream == true;

    public bool CanRevealLocation => SelectedLocation?.ShowReveal == true;

    public bool ShowDirtyDot => IsDirty && !IsConflicted;

    public Task EnsureLoadedAsync() => _load ??= LoadCoreAsync();

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _operation?.Cancel();
        _details?.Cancel();
        _watcher?.Dispose();
        _watcher = null;
        if (_session is not null)
            await _session.DisposeAsync();
    }

    public void ActivateLocation(LocationItem item)
    {
        if (item.ShowCheckout)
            item.CheckoutCommand.Execute(null);
        else if (item.ShowReveal)
            item.RevealCommand.Execute(null);
    }

    [RelayCommand]
    public Task Refresh() => RunAsync("Refreshing…", ct => Session.RefreshRefsAndStatusAsync(ct));

    public Task RefreshFromFocusAsync()
    {
        if (_session is null || IsBusy)
            return Task.CompletedTask;
        return Refresh();
    }

    [RelayCommand]
    public Task Fetch()
    {
        var progress = Progress();
        return RunAsync("Fetching…", ct => Session.FetchAsync(progress, ct));
    }

    [RelayCommand]
    public Task Pull()
    {
        var progress = Progress();
        return RunAsync("Pulling…", ct => Session.PullAsync(progress, ct));
    }

    [RelayCommand]
    public Task Push() => PushCoreAsync();

    [RelayCommand]
    public Task Commit() => CommitCoreAsync(noVerify: false);

    [RelayCommand]
    private Task CommitWithoutHooks() => CommitCoreAsync(noVerify: true);

    private async Task CommitCoreAsync(bool noVerify)
    {
        if (!CanCommit || _session is null)
            return;
        var message = CommitMessage;
        var label = noVerify ? "Committing without hooks…" : "Committing…";
        var ok = await RunAsync(label, ct => _session.CommitAsync(message, ct, noVerify));
        if (ok)
            CommitMessage = "";
    }

    [RelayCommand]
    private Task StageAll() => RunAsync("Staging all…", ct => Session.StageAllAsync(ct));

    [RelayCommand]
    private Task UnstageAll() => RunAsync("Unstaging all…", ct => Session.UnstageAllAsync(ct));

    [RelayCommand]
    private void Cancel() => _operation?.Cancel();

    [RelayCommand]
    public async Task CreateBranch()
    {
        if (_host.Dialogs is null || _session is null || IsBusy)
            return;
        var name = await _host.Dialogs.PromptAsync("Create branch", "Branch name");
        if (string.IsNullOrWhiteSpace(name))
            return;
        await RunAsync("Creating branch…", ct => _session.CreateBranchAsync(name, ct));
    }

    [RelayCommand]
    public async Task AbortMerge()
    {
        if (_host.Dialogs is null || _session is null || IsBusy)
            return;
        var kind = _session.Snapshot().Sequencer;
        var noun = kind switch
        {
            SequencerKind.CherryPick => "cherry-pick",
            SequencerKind.Revert => "revert",
            _ => "merge",
        };
        var ok = await _host.Dialogs.ConfirmAsync("Abort", $"Abort the current {noun} and return to HEAD?", "Abort");
        if (!ok)
            return;
        await RunAsync("Aborting…", ct => _session.AbortSequencerAsync(ct));
    }

    [RelayCommand]
    public async Task CheckoutFromPalette()
    {
        var pick = await PickRefAsync("Checkout", "Checkout a branch.");
        if (pick is null || _session is null)
            return;
        if (pick.StartsWith("refs/heads/", StringComparison.Ordinal))
            await RunAsync("Checking out…", ct => _session.SwitchAsync(ShortHead(pick), ct));
        else if (pick.StartsWith("refs/remotes/", StringComparison.Ordinal))
            await RunAsync("Checking out…", ct => _session.SwitchTrackAsync(ShortRemote(pick), ct));
    }

    [RelayCommand]
    public async Task MergeFromPalette()
    {
        var pick = await PickRefAsync("Merge", "Merge a branch into HEAD.");
        if (pick is null)
            return;
        var name = pick.StartsWith("refs/heads/", StringComparison.Ordinal) ? ShortHead(pick) : ShortRemote(pick);
        await MergeNamedAsync(name);
    }

    [RelayCommand]
    private void ToggleCommands() => CommandsOpen = !CommandsOpen;

    [RelayCommand]
    private void ActivateTab() => _host.Activate(this);

    [RelayCommand]
    private void CloseTab() => _host.Close(this);

    [RelayCommand]
    private Task LoadMore()
    {
        var past = _session?.Snapshot().HistoryCapped == true;
        return LoadMoreAsync(past);
    }

    public Task LoadMoreFromScrollAsync()
    {
        if (_session is null)
            return Task.CompletedTask;
        var state = _session.Snapshot();
        if (state.HistoryEnded || state.HistoryCapped)
            return Task.CompletedTask;
        return LoadMoreAsync(false);
    }

    [RelayCommand]
    private Task LoadLargeDiff()
    {
        _allowLarge = true;
        return LoadDiffAsync();
    }

    [RelayCommand]
    private void DismissBanner() => HasBanner = false;

    [RelayCommand]
    private async Task CopyBanner()
    {
        if (_host.Dialogs is not null)
            await _host.Dialogs.CopyAsync(Banner);
    }

    [RelayCommand]
    private Task CheckoutSelected()
    {
        if (SelectedLocation?.ShowCheckout == true)
            SelectedLocation.CheckoutCommand.Execute(null);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task MergeSelected()
    {
        if (SelectedLocation?.ShowMerge == true)
            SelectedLocation.MergeCommand.Execute(null);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task DeleteSelected()
    {
        if (SelectedLocation?.ShowDelete == true)
            SelectedLocation.DeleteCommand.Execute(null);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task UpstreamSelected()
    {
        if (SelectedLocation?.ShowSetUpstream == true)
            SelectedLocation.SetUpstreamCommand.Execute(null);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task RevealSelected()
    {
        if (SelectedLocation?.ShowReveal == true)
            SelectedLocation.RevealCommand.Execute(null);
        return Task.CompletedTask;
    }

    private bool _allowLarge;

    private RepositorySession Session => _session ?? throw new InvalidOperationException("Repository is not open.");

    partial void OnCommitMessageChanged(string value) => OnPropertyChanged(nameof(CanCommit));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(CanRunCommands));
        NotifyBulkStage();
    }

    partial void OnShowingWorkingCopyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCommit));
        NotifyBulkStage();
    }

    partial void OnNothingStagedChanged(bool value) => OnPropertyChanged(nameof(CanCommit));

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(ShowDirtyDot));

    partial void OnIsConflictedChanged(bool value) => OnPropertyChanged(nameof(ShowDirtyDot));

    partial void OnSelectedLocationChanged(LocationItem? value)
    {
        OnPropertyChanged(nameof(CanCheckoutLocation));
        OnPropertyChanged(nameof(CanMergeLocation));
        OnPropertyChanged(nameof(CanDeleteLocation));
        OnPropertyChanged(nameof(CanUpstreamLocation));
        OnPropertyChanged(nameof(CanRevealLocation));
        OnPropertyChanged(nameof(CanRenameLocation));
        OnPropertyChanged(nameof(CanPopLocation));
        OnPropertyChanged(nameof(CanApplyLocation));
        OnPropertyChanged(nameof(CanDropLocation));
    }

    partial void OnSelectedGraphRowChanged(GraphRowViewModel? value)
    {
        if (_applying || _rangeOlder is not null)
            return;
        // The graph SelectionChanged handler records a multi-select range in this same turn.
        Dispatcher.UIThread.Post(() =>
        {
            if (_applying || _rangeOlder is not null || !ReferenceEquals(SelectedGraphRow, value))
                return;
            _ = LoadDetailsAsync();
        }, DispatcherPriority.Background);
    }

    partial void OnSelectedFileChanged(FileRowViewModel? value)
    {
        if (_applying)
            return;
        if (value is { IsHeader: true })
            return;
        _allowLarge = false;
        _ = LoadDiffAsync();
    }

    private async Task LoadCoreAsync()
    {
        if (!_host.GitReady || _host.GitExecutable is null)
        {
            Fail("Git is not ready.");
            _load = null;
            return;
        }

        BusyText = "Opening…";
        IsBusy = true;
        try
        {
            _session = await OpenSessionAsync(RequestedPath);
            if (_session is null || _lifetime.IsCancellationRequested)
                return;
            Toplevel = _session.Toplevel;
            Title = _session.DisplayName;
            Apply(_session.Snapshot());
            _host.NoteLoaded(this);
            StartWatcher();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }

        if (_session is not null && !_lifetime.IsCancellationRequested)
        {
            await LoadDetailsAsync();
            await MaybeSuggestAsync();
        }
    }

    private async Task<RepositorySession?> OpenSessionAsync(string path)
    {
        try
        {
            return await RepositorySession.OpenAsync(_host.Runner, _host.GitExecutable!, path, _lifetime.Token);
        }
        catch (GitCommandFailedException exception) when (exception.IsDubiousOwnership)
        {
            var dialogs = _host.Dialogs;
            var trust = dialogs is not null && await dialogs.ConfirmAsync(
                "Trust this repository?",
                exception.Message + Environment.NewLine + Environment.NewLine
                    + "Trusting adds this path to the global safe.directory list.",
                "Trust");
            if (!trust)
            {
                Fail(exception.Message);
                _load = null;
                return null;
            }

            try
            {
                await RepositoryAdmin.AddSafeDirectoryAsync(_host.Runner, _host.GitExecutable!, path, _lifetime.Token);
                return await RepositorySession.OpenAsync(_host.Runner, _host.GitExecutable!, path, _lifetime.Token);
            }
            catch (GitCommandFailedException again)
            {
                Fail(again.Message);
                _load = null;
                return null;
            }
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
            _load = null;
            return null;
        }
    }

    private void StartWatcher()
    {
        if (_session is null)
            return;
        _watcher?.Dispose();
        _watcher = new GitDirectoryWatcher(_session.GitDirectory, () => Dispatcher.UIThread.Post(() => _ = RefreshFromWatcherAsync()));
        _watcher.Failed += () => Dispatcher.UIThread.Post(OnWatcherFailed);
    }

    private async Task RefreshFromWatcherAsync()
    {
        if (_session is null || IsBusy || _lifetime.IsCancellationRequested)
            return;
        try
        {
            await _session.RefreshStatusAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested)
                return;
            Apply(_session.Snapshot());
            await LoadDetailsAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
            if (_session is not null)
                Apply(_session.Snapshot());
        }
    }

    private void OnWatcherFailed()
    {
        if (_watcherFailed)
            return;
        _watcherFailed = true;
        _watcher?.Dispose();
        _watcher = null;
        Fail("The repository watcher stopped. Refresh with F5. Sextant will still offer local performance settings when status is slow.");
    }

    private async Task MaybeSuggestAsync()
    {
        if (_askedPerformance || _session is null || _host.Dialogs is null)
            return;
        var suggestion = _session.Snapshot().Suggestion;
        if (suggestion is null)
            return;
        _askedPerformance = true;
        var choice = await _host.Dialogs.ConfirmPerformanceAsync(suggestion.Value);
        if (choice is null || _session is null)
            return;
        if (choice.ManyFiles)
            await RunAsync("Writing config…", ct => _session.SetLocalConfigAsync("feature.manyFiles", "true", ct));
        if (choice.FileSystemMonitor && _session is not null)
            await RunAsync("Writing config…", ct => _session.SetLocalConfigAsync("core.fsmonitor", "true", ct));
    }

    private async Task<bool> RunAsync(string label, Func<CancellationToken, Task> action)
    {
        if (_session is null || IsBusy)
            return false;
        _operation?.Dispose();
        _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        IsBusy = true;
        CanCancel = true;
        BusyText = label;
        var ok = false;
        try
        {
            await action(_operation.Token);
            HasBanner = false;
            Banner = "";
            ok = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
        }
        finally
        {
            IsBusy = false;
            CanCancel = false;
            BusyText = "";
        }

        if (_session is not null && !_lifetime.IsCancellationRequested)
        {
            Apply(_session.Snapshot());
            await LoadDetailsAsync();
        }

        return ok;
    }

    private async Task PushCoreAsync()
    {
        if (_session is null || IsBusy)
            return;
        var state = _session.Snapshot();
        if (state.Branch.Upstream is null && !state.Branch.Detached && state.Branch.HeadName is { } branch)
        {
            if (_host.Dialogs is null)
                return;
            if (state.Remotes.Count == 0)
            {
                Fail("This branch has no upstream, and this repository has no remotes. Add a remote, then push.");
                return;
            }

            var remote = await _host.Dialogs.PickAsync("Push", $"Push {branch} and set its upstream.", state.Remotes);
            if (remote is null)
                return;
            var progress = Progress();
            await RunAsync("Pushing…", ct => _session.PushUpstreamAsync(remote, branch, progress, ct));
            return;
        }

        var pushProgress = Progress();
        await RunAsync("Pushing…", ct => _session.PushAsync(pushProgress, ct));
    }

    private IProgress<string> Progress() => new Progress<string>(text =>
    {
        if (!string.IsNullOrWhiteSpace(text))
            BusyText = text;
    });

    private void NotifyBulkStage()
    {
        OnPropertyChanged(nameof(CanStageAll));
        OnPropertyChanged(nameof(CanUnstageAll));
    }

    private void Fail(string message)
    {
        Banner = message;
        HasBanner = true;
    }

    private void Apply(SessionState state)
    {
        _rangeOlder = null;
        _rangeNewer = null;
        var wantWork = SelectedGraphRow is null || SelectedGraphRow.IsWorkingCopy;
        var wantSha = SelectedGraphRow?.Sha;
        var wantPath = SelectedFile is { IsHeader: false } file ? file.Path : null;
        var wantStaged = SelectedFile?.FromStagedList ?? false;

        _applying = true;
        try
        {
            Toplevel = _session?.Toplevel ?? Toplevel;
            if (_session is not null && !string.IsNullOrEmpty(_session.DisplayName))
                Title = _session.DisplayName;
            BranchText = DescribeBranch(state.Branch);
            ShowAheadBehind = state.Branch.Ahead != 0 || state.Branch.Behind != 0;
            AheadBehindText = ShowAheadBehind ? $"↑{state.Branch.Ahead}  ↓{state.Branch.Behind}" : "";
            IsDirty = state.Entries.Count > 0;
            IsConflicted = state.Sequencer != SequencerKind.None;
            ConflictText = state.Sequencer switch
            {
                SequencerKind.CherryPick => "Cherry-pick in progress. Resolve the files, stage them, and commit, or abort the cherry-pick.",
                SequencerKind.Revert => "Revert in progress. Resolve the files, stage them, and commit, or abort the revert.",
                _ => "Merge in progress. Resolve the files, stage them, and commit, or abort the merge.",
            };
            HistoryCaption = state.HistoryLabel ?? "";
            HasHistoryFilter = state.HistoryLabel is { Length: > 0 };
            if (state.Sequencer != SequencerKind.None && !_wasMerge && string.IsNullOrWhiteSpace(CommitMessage) && !string.IsNullOrWhiteSpace(state.MergeMessage))
                CommitMessage = state.MergeMessage.Trim();
            _wasMerge = state.Sequencer != SequencerKind.None;
            NothingStaged = !state.Entries.Any(entry => entry.Staged);
            var hasUnstaged = state.Entries.Any(entry => entry.Kind != ChangeKind.Unmerged && (entry.Unstaged || entry.Kind == ChangeKind.Untracked));
            var hasStaged = state.Entries.Any(entry => entry.Staged && entry.Kind != ChangeKind.Unmerged);
            if (hasUnstaged != _hasUnstagedWork || hasStaged != _hasStagedWork)
            {
                _hasUnstagedWork = hasUnstaged;
                _hasStagedWork = hasStaged;
                NotifyBulkStage();
            }
            ShowLoadMore = !state.HistoryEnded;
            LoadMoreText = state.HistoryCapped ? "Load more (past 50,000)" : "Load more";

            var entrySignature = EntrySignature(state.Entries);
            if (entrySignature != _entrySignature)
            {
                _entrySignature = entrySignature;
                RebuildFiles(state);
            }

            var refSignature = RefSignature(state);
            if (refSignature != _refSignature)
            {
                _refSignature = refSignature;
                RebuildLocations(state);
            }

            if (state.HistoryGeneration != _historyGeneration)
            {
                _historyGeneration = state.HistoryGeneration;
                _seenCommits = state.Commits.Count;
                RebuildGraph(state);
            }
            else if (state.Commits.Count > _seenCommits)
            {
                AppendGraph(state);
                _seenCommits = state.Commits.Count;
            }
            else if (Rows.Count > 0 && Rows[0].IsWorkingCopy)
            {
                Rows[0].Subject = WorkingSummary(state);
            }

            UpdateCommands(state);
            RememberSelection(wantWork, wantSha, wantPath, wantStaged);
        }
        finally
        {
            _applying = false;
        }

        OnPropertyChanged(nameof(CanCommit));
    }

    private void RememberSelection(bool wantWork, string? wantSha, string? wantPath, bool wantStaged)
    {
        GraphRowViewModel? row = wantWork
            ? Rows.FirstOrDefault(candidate => candidate.IsWorkingCopy)
            : Rows.FirstOrDefault(candidate => string.Equals(candidate.Sha, wantSha, StringComparison.OrdinalIgnoreCase))
                ?? Rows.FirstOrDefault(candidate => candidate.IsWorkingCopy);
        SelectedGraphRow = row;
        if (row is null || row.IsWorkingCopy)
        {
            SelectedFile = Files.FirstOrDefault(candidate => !candidate.IsHeader && candidate.Path == wantPath && candidate.FromStagedList == wantStaged)
                ?? Files.FirstOrDefault(candidate => !candidate.IsHeader && candidate.Path == wantPath)
                ?? Files.FirstOrDefault(candidate => !candidate.IsHeader);
        }
    }

    private void RebuildGraph(SessionState state)
    {
        Rows.Clear();
        Rows.Add(WorkingRow(state));
        foreach (var commit in state.Commits)
            Rows.Add(CommitRow(commit, state));
    }

    private void AppendGraph(SessionState state)
    {
        if (Rows.Count == 0)
        {
            RebuildGraph(state);
            return;
        }

        for (var i = _seenCommits; i < state.Commits.Count; i++)
            Rows.Add(CommitRow(state.Commits[i], state));
    }

    private GraphRowViewModel WorkingRow(SessionState state) => new()
    {
        IsWorkingCopy = true,
        ShowLanes = false,
        Subject = WorkingSummary(state),
        CreateBranchCommand = CreateBranchCommand,
    };

    private GraphRowViewModel CommitRow(GraphCommit commit, SessionState state)
    {
        var locals = state.Refs.Where(reference =>
            reference.Name.StartsWith("refs/heads/", StringComparison.Ordinal)
            && string.Equals(reference.Oid, commit.Commit.Sha, StringComparison.OrdinalIgnoreCase)).ToList();
        var checkout = locals.Count == 1 && !locals[0].IsHead;
        var name = checkout ? ShortHead(locals[0].Name) : "";
        return new GraphRowViewModel
        {
            Sha = commit.Commit.Sha,
            Commit = commit.Commit,
            Lanes = commit.Lanes,
            ShowLanes = true,
            Subject = commit.Commit.Subject,
            Author = commit.Commit.AuthorName,
            When = Relative(commit.Commit.AuthorUnixSeconds),
            RefText = RefLabel(commit.Commit.Sha, state.Refs),
            IsHead = !string.IsNullOrEmpty(state.Branch.Oid)
                && string.Equals(state.Branch.Oid, commit.Commit.Sha, StringComparison.OrdinalIgnoreCase),
            ShowCheckout = checkout,
            ShowRewrite = true,
            CheckoutCommand = checkout
                ? new AsyncRelayCommand(() => RunAsync("Checking out…", ct => Session.SwitchAsync(name, ct)))
                : UiCommands.Disabled,
            CreateBranchCommand = CreateBranchCommand,
            CopyShaCommand = new AsyncRelayCommand(() => CopyText(commit.Commit.Sha)),
            ResetSoftCommand = new AsyncRelayCommand(() => ResetAsync(commit.Commit, "--soft")),
            ResetMixedCommand = new AsyncRelayCommand(() => ResetAsync(commit.Commit, "--mixed")),
            ResetHardCommand = new AsyncRelayCommand(() => ResetAsync(commit.Commit, "--hard")),
            CherryPickCommand = new AsyncRelayCommand(() => CherryPickAsync(commit.Commit)),
            RevertCommand = new AsyncRelayCommand(() => RevertAsync(commit.Commit)),
            TagCommand = new AsyncRelayCommand(() => TagAsync(commit.Commit)),
        };
    }

    private void RebuildFiles(SessionState state)
    {
        _workingFiles.Clear();
        var conflicts = state.Entries.Where(entry => entry.Kind == ChangeKind.Unmerged).ToList();
        var staged = state.Entries.Where(entry => entry.Staged && entry.Kind != ChangeKind.Unmerged).ToList();
        var unstaged = state.Entries.Where(entry => (entry.Unstaged || entry.Kind == ChangeKind.Untracked) && entry.Kind != ChangeKind.Unmerged).ToList();
        AddFileSection("Conflicts", conflicts, stagedList: false, conflict: true);
        AddFileSection("Staged", staged, stagedList: true, conflict: false);
        AddFileSection("Unstaged", unstaged, stagedList: false, conflict: false);
        if (!_showingCommitFiles)
            CopyFiles(_workingFiles);
    }

    private void AddFileSection(string title, List<StatusEntry> entries, bool stagedList, bool conflict)
    {
        if (entries.Count == 0)
            return;
        _workingFiles.Add(new FileRowViewModel { IsHeader = true, Label = title });
        foreach (var entry in entries)
            _workingFiles.Add(FileRow(entry, stagedList, conflict));
    }

    private FileRowViewModel FileRow(StatusEntry entry, bool stagedList, bool conflict)
    {
        var path = entry.Path;
        var untracked = entry.Kind == ChangeKind.Untracked;
        return new FileRowViewModel
        {
            Path = path,
            Label = entry.OriginalPath is { Length: > 0 } original ? original + " → " + path : path,
            StatusText = conflict ? "U" : untracked ? "?" : (stagedList ? entry.IndexStatus : entry.WorkTreeStatus).ToString(),
            Kind = entry.Kind,
            FromStagedList = stagedList,
            Untracked = untracked,
            ShowStage = conflict || !stagedList,
            ShowUnstage = stagedList && !conflict,
            ShowDiscard = !conflict,
            ShowMergetool = conflict,
            ShowHistory = true,
            StageCommand = new AsyncRelayCommand(() => RunAsync(conflict ? "Staging resolution…" : "Staging…", ct => Session.StageFileAsync(path, ct))),
            UnstageCommand = new AsyncRelayCommand(() => RunAsync("Unstaging…", ct => Session.UnstageFileAsync(path, ct))),
            DiscardCommand = new AsyncRelayCommand(() => DiscardAsync(path, untracked)),
            MergetoolCommand = new AsyncRelayCommand(() => RunAsync("Opening merge tool…", ct => Session.MergetoolAsync(path, ct))),
            HistoryCommand = new AsyncRelayCommand(() => ShowFileHistoryAsync(path)),
        };
    }

    private async Task DiscardAsync(string path, bool untracked)
    {
        if (_host.Dialogs is null || _session is null)
            return;
        var ok = await _host.Dialogs.ConfirmAsync(
            "Discard",
            $"Discard changes to {path}? This cannot be undone.",
            "Discard");
        if (!ok)
            return;
        if (untracked)
            await RunAsync("Discarding…", ct => _session.DiscardUntrackedAsync(path, ct));
        else
            await RunAsync("Discarding…", ct => _session.DiscardTrackedAsync(path, ct));
    }

    private void RebuildLocations(SessionState state)
    {
        var selected = SelectedLocation?.Key;
        RememberCollapsed();
        _locationRoots.Clear();

        var branches = new List<LocationItem>();
        foreach (var branch in state.Refs.Where(reference => reference.Name.StartsWith("refs/heads/", StringComparison.Ordinal))
                     .OrderBy(reference => reference.Name, StringComparer.Ordinal))
        {
            var name = ShortHead(branch.Name);
            var current = branch.IsHead;
            branches.Add(new LocationItem
            {
                Key = "b:" + name,
                Label = name,
                IsCurrent = current,
                Oid = branch.Oid,
                ShowCheckout = !current,
                ShowMerge = !current,
                ShowDelete = !current,
                ShowSetUpstream = true,
                ShowReveal = true,
                CheckoutCommand = new AsyncRelayCommand(() => RunAsync("Checking out…", ct => Session.SwitchAsync(name, ct))),
                MergeCommand = new AsyncRelayCommand(() => MergeNamedAsync(name)),
                DeleteCommand = new AsyncRelayCommand(() => DeleteNamedAsync(name)),
                SetUpstreamCommand = new AsyncRelayCommand(() => SetUpstreamNamedAsync(name)),
                RevealCommand = new AsyncRelayCommand(() => RevealAsync(branch.Oid)),
            });
        }

        _locationRoots.Add(Section("h:branches", "Branches", GroupLocations(branches, "b"), branches.Count));

        var trackedRefs = state.Refs.Where(reference =>
            reference.Name.StartsWith("refs/remotes/", StringComparison.Ordinal)
            && !reference.Name.EndsWith("/HEAD", StringComparison.Ordinal)).ToList();
        foreach (var group in trackedRefs.GroupBy(reference => RemoteGroup(reference.Name)).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var tracked = new List<LocationItem>();
            foreach (var remote in group.OrderBy(reference => reference.Name, StringComparer.Ordinal))
            {
                var name = ShortRemote(remote.Name);
                var label = name.Length > group.Key.Length + 1 ? name[(group.Key.Length + 1)..] : name;
                tracked.Add(new LocationItem
                {
                    Key = "r:" + name,
                    Label = label,
                    Oid = remote.Oid,
                    ShowCheckout = true,
                    ShowMerge = true,
                    ShowReveal = true,
                    CheckoutCommand = new AsyncRelayCommand(() => RunAsync("Checking out…", ct => Session.SwitchTrackAsync(name, ct))),
                    MergeCommand = new AsyncRelayCommand(() => MergeNamedAsync(name)),
                    RevealCommand = new AsyncRelayCommand(() => RevealAsync(remote.Oid)),
                });
            }

            _locationRoots.Add(Section("h:remote:" + group.Key, group.Key, GroupLocations(tracked, "r:" + group.Key), tracked.Count));
        }

        var tags = new List<LocationItem>();
        foreach (var tag in state.Refs.Where(reference => reference.Name.StartsWith("refs/tags/", StringComparison.Ordinal))
                     .OrderBy(reference => reference.Name, StringComparer.Ordinal))
        {
            var name = tag.Name["refs/tags/".Length..];
            tags.Add(new LocationItem
            {
                Key = "t:" + name,
                Label = name,
                Oid = tag.Oid,
                ShowReveal = true,
                ShowDelete = true,
                RevealCommand = new AsyncRelayCommand(() => RevealAsync(tag.Oid)),
                DeleteCommand = new AsyncRelayCommand(() => DeleteTagAsync(name)),
            });
        }

        _locationRoots.Add(Section("h:tags", "Tags", GroupLocations(tags, "t"), tags.Count));

        var remotes = new List<LocationItem>();
        foreach (var remote in state.Remotes.OrderBy(name => name, StringComparer.Ordinal))
        {
            remotes.Add(new LocationItem
            {
                Key = "m:" + remote,
                Label = remote,
                ShowDelete = true,
                ShowRename = true,
                DeleteCommand = new AsyncRelayCommand(() => RemoveRemoteAsync(remote)),
                RenameCommand = new AsyncRelayCommand(() => RenameRemoteAsync(remote)),
            });
        }

        _locationRoots.Add(Section("h:remotes", "Remotes", remotes, remotes.Count));

        if (state.Stashes.Count > 0)
        {
            var stashes = new List<LocationItem>();
            foreach (var stash in state.Stashes)
            {
                stashes.Add(new LocationItem
                {
                    Key = "s:" + stash.Ref,
                    Label = stash.Ref + "  " + stash.Subject,
                    ShowPop = true,
                    ShowApply = true,
                    ShowDrop = true,
                    PopCommand = new AsyncRelayCommand(() => PopStashAsync(stash)),
                    ApplyCommand = new AsyncRelayCommand(() => ApplyStashAsync(stash)),
                    DropCommand = new AsyncRelayCommand(() => DropStashAsync(stash)),
                });
            }

            _locationRoots.Add(Section("h:stashes", "Stashes", stashes, stashes.Count));
        }

        PublishLocations(selected);
    }

    public void ToggleLocation(LocationItem item)
    {
        if (item.Children.Count == 0)
            return;
        item.IsExpanded = !item.IsExpanded;
        if (item.CollapseKey.Length > 0)
        {
            if (item.IsExpanded)
                _collapsedLocations.Remove(item.CollapseKey);
            else
                _collapsedLocations.Add(item.CollapseKey);
        }

        PublishLocations(SelectedLocation?.Key);
    }

    private void PublishLocations(string? selectedKey)
    {
        var flat = new List<LocationItem>();
        foreach (var root in _locationRoots)
            AppendVisible(root, 0, flat);
        Locations.Reset(flat);
        SelectedLocation = selectedKey is null ? null : flat.FirstOrDefault(item => item.Key == selectedKey);
    }

    private static void AppendVisible(LocationItem item, int depth, List<LocationItem> flat)
    {
        item.Depth = depth;
        flat.Add(item);
        if (!item.IsExpanded)
            return;
        foreach (var child in item.Children)
            AppendVisible(child, depth + 1, flat);
    }

    private void RememberCollapsed() => RememberCollapsed(_locationRoots);

    private void RememberCollapsed(IEnumerable<LocationItem> items)
    {
        foreach (var item in items)
        {
            if (item.Children.Count > 0 && item.CollapseKey.Length > 0)
            {
                if (item.IsExpanded)
                    _collapsedLocations.Remove(item.CollapseKey);
                else
                    _collapsedLocations.Add(item.CollapseKey);
            }

            RememberCollapsed(item.Children);
        }
    }

    private List<LocationItem> GroupLocations(IReadOnlyList<LocationItem> leaves, string scope)
    {
        var byPath = new Dictionary<string, LocationItem>(StringComparer.Ordinal);
        foreach (var leaf in leaves)
            byPath[leaf.Label] = leaf;
        return MapNodes(PathGrouping.Group(byPath.Keys), byPath, scope);
    }

    private List<LocationItem> MapNodes(
        IReadOnlyList<PathGrouping.Node> nodes,
        Dictionary<string, LocationItem> byPath,
        string scope)
    {
        var list = new List<LocationItem>();
        foreach (var node in nodes)
        {
            var children = MapNodes(node.Children, byPath, scope);
            var refs = (node.RefPath is null ? 0 : 1) + children.Sum(RefCount);
            var label = node.Children.Count == 0 ? node.Label : $"{node.Label} ({refs})";
            if (node.RefPath is { } path && byPath.TryGetValue(path, out var leaf))
            {
                leaf.Label = label;
                foreach (var child in children)
                    leaf.Children.Add(child);
                if (children.Count > 0)
                    ApplyFold(leaf, scope + ":" + node.FullName);
                list.Add(leaf);
                continue;
            }

            var folder = new LocationItem
            {
                Key = "g:" + scope + ":" + node.FullName,
                Label = label,
            };
            ApplyFold(folder, scope + ":" + node.FullName);
            foreach (var child in children)
                folder.Children.Add(child);
            list.Add(folder);
        }

        return list;
    }

    private void ApplyFold(LocationItem item, string collapseKey)
    {
        item.CollapseKey = collapseKey;
        item.IsExpanded = !_collapsedLocations.Contains(collapseKey);
    }

    private static int RefCount(LocationItem item) =>
        (item.Key.StartsWith("g:", StringComparison.Ordinal) ? 0 : 1) + item.Children.Sum(RefCount);

    private LocationItem Section(string key, string title, IReadOnlyList<LocationItem> children, int count)
    {
        var section = new LocationItem
        {
            IsHeader = true,
            Key = key,
            Label = $"{title} ({count})",
            IsExpanded = !_collapsedLocations.Contains(key),
            CollapseKey = key,
        };
        foreach (var child in children)
            section.Children.Add(child);
        return section;
    }

    private async Task MergeNamedAsync(string name)
    {
        if (_host.Dialogs is null || _session is null || IsBusy)
            return;
        var current = _session.Snapshot().Branch.HeadName ?? "HEAD";
        var ok = await _host.Dialogs.ConfirmAsync("Merge", $"Merge {name} into {current}?", "Merge");
        if (!ok)
            return;
        await RunAsync("Merging…", ct => _session.MergeAsync(name, ct));
    }

    private async Task DeleteNamedAsync(string name)
    {
        if (_host.Dialogs is null || _session is null || IsBusy)
            return;
        var ok = await _host.Dialogs.ConfirmAsync(
            "Delete branch",
            $"Delete {name}? Git uses branch -d and refuses a branch that is not merged.",
            "Delete");
        if (!ok)
            return;
        await RunAsync("Deleting branch…", ct => _session.DeleteBranchAsync(name, ct));
    }

    private async Task SetUpstreamNamedAsync(string branch)
    {
        if (_host.Dialogs is null || _session is null || IsBusy)
            return;
        var options = _session.Snapshot().Refs
            .Where(reference => reference.Name.StartsWith("refs/remotes/", StringComparison.Ordinal)
                && !reference.Name.EndsWith("/HEAD", StringComparison.Ordinal))
            .Select(reference => ShortRemote(reference.Name))
            .ToList();
        if (options.Count == 0)
        {
            Fail("There is no remote-tracking branch to use as upstream.");
            return;
        }

        var pick = await _host.Dialogs.PickAsync("Set upstream", $"Upstream for {branch}", options);
        if (pick is null)
            return;
        await RunAsync("Setting upstream…", ct => _session.SetUpstreamAsync(branch, pick, ct));
    }

    private async Task RevealAsync(string oid)
    {
        if (_session is null)
            return;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var found = Rows.FirstOrDefault(row => string.Equals(row.Sha, oid, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
            {
                SelectedGraphRow = found;
                return;
            }

            var state = _session.Snapshot();
            if (state.HistoryEnded)
                break;
            var pastCap = state.HistoryCapped;
            var loaded = await _session.LoadMoreHistoryAsync(pastCap, _lifetime.Token);
            Apply(_session.Snapshot());
            if (!loaded)
            {
                if (!pastCap && _session.Snapshot().HistoryCapped)
                    continue;
                break;
            }
        }

        var row = Rows.FirstOrDefault(candidate => string.Equals(candidate.Sha, oid, StringComparison.OrdinalIgnoreCase));
        if (row is null)
            Fail("That commit is not in the loaded history.");
        else
            SelectedGraphRow = row;
    }

    private async Task<string?> PickRefAsync(string title, string message)
    {
        if (_host.Dialogs is null || _session is null)
            return null;
        var options = _session.Snapshot().Refs
            .Where(reference => reference.Name.StartsWith("refs/heads/", StringComparison.Ordinal)
                || (reference.Name.StartsWith("refs/remotes/", StringComparison.Ordinal)
                    && !reference.Name.EndsWith("/HEAD", StringComparison.Ordinal)))
            .Select(reference => reference.Name.StartsWith("refs/heads/", StringComparison.Ordinal)
                ? ShortHead(reference.Name)
                : ShortRemote(reference.Name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var pick = await _host.Dialogs.PickAsync(title, message, options);
        if (pick is null)
            return null;
        var match = _session.Snapshot().Refs.FirstOrDefault(reference =>
            ShortHead(reference.Name) == pick || ShortRemote(reference.Name) == pick);
        return match?.Name;
    }

    private async Task LoadMoreAsync(bool pastCap)
    {
        if (_session is null || _loadingMore || IsBusy)
            return;
        _loadingMore = true;
        try
        {
            await _session.LoadMoreHistoryAsync(pastCap, _lifetime.Token);
            Apply(_session.Snapshot());
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
        }
        finally
        {
            _loadingMore = false;
        }
    }

    private async Task LoadDetailsAsync()
    {
        if (_applying || _lifetime.IsCancellationRequested)
            return;
        var row = SelectedGraphRow;
        if (row is null || row.IsWorkingCopy)
        {
            ShowingWorkingCopy = true;
            ShowingCommit = false;
            CommitTitle = "";
            CommitMeta = "";
            _shownSha = null;
            if (_showingCommitFiles)
                RestoreWorkingFiles();
            await LoadDiffAsync();
            return;
        }

        ShowingWorkingCopy = false;
        ShowingCommit = true;
        CommitTitle = row.Subject;
        var sha = row.Sha ?? "";
        var shortSha = sha.Length <= 7 ? sha : sha[..7];
        CommitMeta = string.Join("  ·  ", new[] { row.Author, row.When, shortSha }.Where(part => part.Length > 0));
        if (_session is null)
            return;
        if (_shownSha == sha && _showingCommitFiles)
            return;
        ReplaceDetails();
        var token = _details!.Token;
        try
        {
            var parent = row.Commit?.Parents.Count > 0 ? row.Commit.Parents[0] : null;
            var files = await _session.CommitFilesAsync(sha, token);
            if (files is null || token.IsCancellationRequested)
                return;
            ShowCommitFiles(files);
            _shownSha = sha;
            _diffParent = parent;
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

    private string? _diffParent;

    private void RestoreWorkingFiles()
    {
        var previous = SelectedFile;
        _showingCommitFiles = false;
        _applying = true;
        try
        {
            CopyFiles(_workingFiles);
            SelectedFile = Files.FirstOrDefault(candidate => !candidate.IsHeader && candidate.Path == previous?.Path && candidate.FromStagedList == (previous?.FromStagedList ?? false))
                ?? Files.FirstOrDefault(candidate => !candidate.IsHeader);
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>
    /// The file list view runs the replacement while holding the working-copy scroll position.
    /// </summary>
    public event Action<Action>? PreserveFileScroll;

    private void CopyFiles(IReadOnlyList<FileRowViewModel> rows)
    {
        void Apply()
        {
            Files.Clear();
            foreach (var row in rows)
                Files.Add(row);
        }

        var preserve = PreserveFileScroll;
        if (preserve is null)
            Apply();
        else
            preserve(Apply);
    }

    private void ShowCommitFiles(IReadOnlyList<CommitFileChange> files)
    {
        var previous = SelectedFile?.Path;
        _showingCommitFiles = true;
        _applying = true;
        try
        {
            Files.Clear();
            if (files.Count > 0)
                Files.Add(new FileRowViewModel { IsHeader = true, Label = "Changes" });
            foreach (var change in files)
            {
                Files.Add(new FileRowViewModel
                {
                    Path = change.Path,
                    Label = change.OriginalPath is { Length: > 0 } original ? original + " → " + change.Path : change.Path,
                    StatusText = Letter(change.Kind),
                    Kind = change.Kind,
                    ShowHistory = true,
                    HistoryCommand = new AsyncRelayCommand(() => ShowFileHistoryAsync(change.Path)),
                });
            }

            SelectedFile = Files.FirstOrDefault(candidate => !candidate.IsHeader && candidate.Path == previous)
                ?? Files.FirstOrDefault(candidate => !candidate.IsHeader);
        }
        finally
        {
            _applying = false;
        }
    }

    private async Task LoadDiffAsync()
    {
        if (_lifetime.IsCancellationRequested)
            return;
        if (ShowingBlame)
        {
            await LoadBlameAsync();
            return;
        }

        var file = SelectedFile is { IsHeader: false } selected ? selected : null;
        var row = SelectedGraphRow;
        var range = _rangeOlder is not null && _rangeNewer is not null;
        var workingCopy = !range && (row is null || row.IsWorkingCopy);
        if (_session is null || (!AllFiles && file is null))
        {
            ClearDiff(workingCopy ? "Select a file." : "");
            return;
        }

        ReplaceDetails();
        var token = _details!.Token;
        var allowLarge = _allowLarge;
        var ignoreWhitespace = IgnoreWhitespace;
        try
        {
            DiffDocument? document;
            if (range)
            {
                _viewingStaged = false;
                document = await _session.RangeDiffAsync(
                    _rangeOlder!,
                    _rangeNewer!,
                    AllFiles ? null : file!.Path,
                    allowLarge,
                    ignoreWhitespace,
                    token);
            }
            else if (workingCopy)
            {
                _viewingStaged = file?.FromStagedList == true;
                document = AllFiles
                    ? await _session.WorktreeDiffAsync(_viewingStaged, allowLarge, ignoreWhitespace, token)
                    : await _session.WorkingDiffAsync(file!.Path, file.FromStagedList, file.Untracked, allowLarge, token, ignoreWhitespace);
            }
            else
            {
                _viewingStaged = false;
                var parent = _diffParent ?? (row?.Commit?.Parents.Count > 0 ? row.Commit.Parents[0] : null);
                document = await _session.CommitDiffAsync(row!.Sha!, parent, AllFiles ? null : file!.Path, allowLarge, token, ignoreWhitespace);
            }

            if (document is null || token.IsCancellationRequested)
                return;
            RenderDiff(document, file, workingCopy);
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
        }
    }

    private void RenderDiff(DiffDocument document, FileRowViewModel? file, bool workingCopy)
    {
        DiffRows.Clear();
        BlameRows.Clear();
        _rawPatch = document.RawPatch;
        ShowLoadDiff = document.IsTooLarge;
        if (document.IsTooLarge)
        {
            HasDiffNotice = true;
            DiffNotice = "This diff is large. Load it only if you need the whole file.";
            return;
        }

        if (AllFiles)
        {
            var files = string.IsNullOrEmpty(document.RawPatch) ? [] : DiffParser.ParseFiles(document.RawPatch);
            if (files.Count == 0)
            {
                HasDiffNotice = true;
                DiffNotice = "No textual changes.";
                return;
            }

            HasDiffNotice = false;
            DiffNotice = "";
            foreach (var entry in files)
            {
                DiffRows.Add(new DiffFileRow { Label = string.IsNullOrEmpty(entry.Path) ? "Diff" : entry.Path });
                AppendFileDiff(entry.Document, workingCopy, KindForDiff(entry.Document));
            }

            return;
        }

        AppendFileDiff(document, workingCopy, file?.Kind ?? ChangeKind.Modified, notice: true);
    }

    private void AppendFileDiff(DiffDocument document, bool workingCopy, ChangeKind kind, bool notice = false)
    {
        if (document.IsBinary)
        {
            NoteFile(notice, "Binary file.");
            return;
        }

        if (document.Hunks.Count == 0)
        {
            NoteFile(notice, document.IsNewFile ? "New file." : "No textual changes.");
            return;
        }

        if (notice)
        {
            HasDiffNotice = false;
            DiffNotice = "";
        }

        var parts = CanStageParts(workingCopy, kind, document);
        var patch = document.RawPatch;
        var hunkLabel = _viewingStaged ? "Unstage hunk" : "Stage hunk";
        var lineLabel = _viewingStaged ? "Unstage line" : "Stage line";
        for (var index = 0; index < document.Hunks.Count; index++)
        {
            var hunk = document.Hunks[index];
            var hunkIndex = index;
            DiffRows.Add(new DiffHunkRow
            {
                Header = hunk.Header,
                ShowAction = parts,
                ActionLabel = hunkLabel,
                ActionCommand = parts
                    ? new AsyncRelayCommand(() => ApplyShownHunkAsync(patch, hunkIndex))
                    : UiCommands.Disabled,
            });
            if (SideBySide)
                AppendSideBySide(hunk);
            else
                AppendInline(hunk, parts, patch, hunkIndex, lineLabel);
        }
    }

    private void NoteFile(bool notice, string message)
    {
        if (notice)
        {
            HasDiffNotice = true;
            DiffNotice = message;
            return;
        }

        DiffRows.Add(new DiffLineRow { Text = message, Background = DiffColors.Clear });
    }

    private void AppendInline(DiffHunk hunk, bool parts, string patch, int hunkIndex, string lineLabel)
    {
        for (var lineIndex = 0; lineIndex < hunk.Lines.Count; lineIndex++)
        {
            var line = hunk.Lines[lineIndex];
            var (prefix, background) = line.Kind switch
            {
                DiffLineKind.Added => ("+ ", DiffColors.Added),
                DiffLineKind.Removed => ("- ", DiffColors.Removed),
                _ => ("  ", DiffColors.Clear),
            };
            var show = parts && line.Kind is DiffLineKind.Added or DiffLineKind.Removed;
            var captured = lineIndex;
            DiffRows.Add(new DiffLineRow
            {
                Text = prefix + line.Text,
                Background = background,
                ShowAction = show,
                ActionLabel = lineLabel,
                ActionCommand = show
                    ? new AsyncRelayCommand(() => ApplyShownLineAsync(patch, hunkIndex, captured))
                    : UiCommands.Disabled,
            });
        }
    }

    private void AppendSideBySide(DiffHunk hunk)
    {
        var removed = new Queue<string>();
        foreach (var line in hunk.Lines)
        {
            switch (line.Kind)
            {
                case DiffLineKind.Removed:
                    removed.Enqueue(line.Text);
                    break;
                case DiffLineKind.Added:
                    if (removed.Count > 0)
                        AddSide("- " + removed.Dequeue(), DiffColors.Removed, "+ " + line.Text, DiffColors.Added);
                    else
                        AddSide("", DiffColors.Clear, "+ " + line.Text, DiffColors.Added);
                    break;
                case DiffLineKind.Context:
                    FlushRemoved();
                    AddSide("  " + line.Text, DiffColors.Clear, "  " + line.Text, DiffColors.Clear);
                    break;
            }
        }

        FlushRemoved();

        void FlushRemoved()
        {
            while (removed.Count > 0)
                AddSide("- " + removed.Dequeue(), DiffColors.Removed, "", DiffColors.Clear);
        }
    }

    private void AddSide(string left, IBrush leftBackground, string right, IBrush rightBackground)
    {
        DiffRows.Add(new DiffSideRow
        {
            Left = left,
            Right = right,
            LeftBackground = leftBackground,
            RightBackground = rightBackground,
        });
    }

    private static bool CanStageParts(bool workingCopy, ChangeKind kind, DiffDocument document) =>
        workingCopy
        && !document.IsRename
        && !document.IsBinary
        && !string.IsNullOrEmpty(document.RawPatch)
        && kind is ChangeKind.Modified or ChangeKind.Added or ChangeKind.Untracked or ChangeKind.Deleted;

    private static ChangeKind KindForDiff(DiffDocument document)
    {
        if (document.IsNewFile)
            return ChangeKind.Added;
        if (document.IsDeleted)
            return ChangeKind.Deleted;
        return ChangeKind.Modified;
    }

    private async Task ApplyShownHunkAsync(string patch, int index)
    {
        if (_session is null || string.IsNullOrEmpty(patch))
            return;
        var reverse = _viewingStaged;
        await RunAsync(reverse ? "Unstaging hunk…" : "Staging hunk…", ct => _session.ApplyHunkAsync(patch, index, reverse, ct));
    }

    private async Task ApplyShownLineAsync(string patch, int hunkIndex, int lineIndex)
    {
        if (_session is null || string.IsNullOrEmpty(patch))
            return;
        var reverse = _viewingStaged;
        try
        {
            await RunAsync(reverse ? "Unstaging line…" : "Staging line…", ct => _session.ApplyLineAsync(patch, hunkIndex, lineIndex, reverse, ct));
        }
        catch (InvalidOperationException exception)
        {
            Fail(exception.Message);
        }
    }

    private void ClearDiff(string notice)
    {
        DiffRows.Clear();
        BlameRows.Clear();
        _rawPatch = null;
        ShowLoadDiff = false;
        DiffNotice = notice;
        HasDiffNotice = notice.Length > 0;
    }

    private void ReplaceDetails()
    {
        _details?.Cancel();
        _details?.Dispose();
        _details = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
    }

    private void UpdateCommands(SessionState state)
    {
        var signature = state.Commands.Count == 0
            ? ""
            : state.Commands.Count + ":" + state.Commands[^1].At.ToUnixTimeMilliseconds();
        if (signature == _commandSignature)
            return;
        _commandSignature = signature;
        CommandLines.Clear();
        foreach (var entry in state.Commands.TakeLast(40))
        {
            var arguments = string.Join(" ", entry.Arguments);
            CommandLines.Add($"{entry.At.LocalDateTime:HH:mm:ss}  {entry.Duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)}ms  {entry.ExitCode}  {arguments}");
        }
    }

    private static string WorkingSummary(SessionState state)
    {
        var sequencer = state.Sequencer switch
        {
            SequencerKind.CherryPick => "Cherry-pick in progress",
            SequencerKind.Revert => "Revert in progress",
            SequencerKind.Merge => "Merge in progress",
            _ => null,
        };
        if (sequencer is not null)
            return "Working copy  ·  " + sequencer;
        if (state.Entries.Count == 0)
            return state.Branch.Unborn ? "Working copy  ·  No commits yet" : "Working copy  ·  Clean";
        var conflicts = state.Entries.Count(entry => entry.Kind == ChangeKind.Unmerged);
        var staged = state.Entries.Count(entry => entry.Staged && entry.Kind != ChangeKind.Unmerged);
        var unstaged = state.Entries.Count(entry => entry.Kind != ChangeKind.Unmerged && (entry.Unstaged || entry.Kind == ChangeKind.Untracked));
        var parts = new List<string>();
        if (conflicts > 0)
            parts.Add($"{conflicts} conflicted");
        if (staged > 0)
            parts.Add($"{staged} staged");
        if (unstaged > 0)
            parts.Add($"{unstaged} unstaged");
        return "Working copy  ·  " + string.Join(", ", parts);
    }

    private static string DescribeBranch(BranchHeader branch)
    {
        if (branch.Detached)
            return "detached " + Short(branch.Oid);
        if (string.IsNullOrEmpty(branch.HeadName))
            return branch.Unborn ? "No branch" : "HEAD";
        return branch.Upstream is { Length: > 0 } upstream ? branch.HeadName + "  →  " + upstream : branch.HeadName;
    }

    private static string RefLabel(string sha, IReadOnlyList<GitRef> refs)
    {
        var names = new List<string>();
        foreach (var reference in refs)
        {
            if (!string.Equals(reference.Oid, sha, StringComparison.OrdinalIgnoreCase))
                continue;
            if (reference.Name.StartsWith("refs/remotes/", StringComparison.Ordinal)
                && reference.Name.EndsWith("/HEAD", StringComparison.Ordinal))
                continue;
            names.Add(ShortRef(reference.Name));
        }

        if (names.Count == 0)
            return "";
        if (names.Count <= 4)
            return string.Join("  ", names);
        return string.Join("  ", names.Take(4)) + $"  +{names.Count - 4}";
    }

    private static string ShortRef(string name)
    {
        if (name.StartsWith("refs/heads/", StringComparison.Ordinal))
            return name["refs/heads/".Length..];
        if (name.StartsWith("refs/remotes/", StringComparison.Ordinal))
            return name["refs/remotes/".Length..];
        if (name.StartsWith("refs/tags/", StringComparison.Ordinal))
            return name["refs/tags/".Length..];
        if (name == "refs/stash")
            return "stash";
        return name;
    }

    private static string ShortHead(string name) =>
        name.StartsWith("refs/heads/", StringComparison.Ordinal) ? name["refs/heads/".Length..] : name;

    private static string ShortRemote(string name) =>
        name.StartsWith("refs/remotes/", StringComparison.Ordinal) ? name["refs/remotes/".Length..] : name;

    private static string RemoteGroup(string name)
    {
        var rest = ShortRemote(name);
        var slash = rest.IndexOf('/');
        return slash < 0 ? rest : rest[..slash];
    }

    private static string Short(string? oid) =>
        string.IsNullOrEmpty(oid) ? "" : oid.Length <= 7 ? oid : oid[..7];

    private static string Letter(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "A",
        ChangeKind.Deleted => "D",
        ChangeKind.Renamed => "R",
        ChangeKind.Copied => "C",
        ChangeKind.Unmerged => "U",
        ChangeKind.Untracked => "?",
        ChangeKind.TypeChanged => "T",
        _ => "M",
    };

    private static string Relative(long unixSeconds)
    {
        var delta = DateTimeOffset.Now - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (delta.TotalMinutes < 1)
            return "just now";
        if (delta.TotalHours < 1)
            return $"{(int)delta.TotalMinutes}m";
        if (delta.TotalDays < 1)
            return $"{(int)delta.TotalHours}h";
        if (delta.TotalDays < 30)
            return $"{(int)delta.TotalDays}d";
        if (delta.TotalDays < 365)
            return $"{(int)(delta.TotalDays / 30)}mo";
        return $"{(int)(delta.TotalDays / 365)}y";
    }

    private static string EntrySignature(IReadOnlyList<StatusEntry> entries)
    {
        var builder = new StringBuilder();
        foreach (var entry in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
            builder.Append(entry.Path).Append(entry.IndexStatus).Append(entry.WorkTreeStatus).Append((int)entry.Kind).Append('|');
        return builder.ToString();
    }

    private static string RefSignature(SessionState state)
    {
        var builder = new StringBuilder();
        foreach (var reference in state.Refs.OrderBy(reference => reference.Name, StringComparer.Ordinal))
            builder.Append(reference.Name).Append('=').Append(reference.Oid).Append(reference.IsHead ? '*' : ' ').Append(reference.Upstream).Append('|');
        foreach (var remote in state.Remotes)
            builder.Append(remote).Append(';');
        foreach (var stash in state.Stashes)
            builder.Append(stash.Ref).Append('=').Append(stash.Sha).Append('|');
        return builder.ToString();
    }

    private Task CopyText(string text) => _host.Dialogs is null ? Task.CompletedTask : _host.Dialogs.CopyAsync(text);
}
