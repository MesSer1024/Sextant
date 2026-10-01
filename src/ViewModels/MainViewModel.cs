using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;
using Sextant.Services;
using System.Collections.ObjectModel;

namespace Sextant.ViewModels;

public partial class MainViewModel : ViewModelBase, IWorkspaceHost
{
    private readonly WorkspaceStore _store;
    private readonly WorkspaceState _workspace;
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<PaletteItem> _palette = [];
    private Task? _initialize;
    private bool _started;
    private int _shutDown;

    public MainViewModel(WorkspaceStore store, WorkspaceState workspace, AppSettings settings, GitProcessRunner runner)
    {
        _store = store;
        _workspace = workspace;
        _settings = settings;
        Runner = runner;
        LocationsWidth = workspace.LocationsWidth;
        GraphWidth = workspace.GraphWidth;
        FilesHeight = workspace.FilesHeight;
    }

    public GitProcessRunner Runner { get; }

    public IDialogService? Dialogs { get; private set; }

    public ObservableCollection<RepositoryViewModel> Tabs { get; } = [];

    public ObservableCollection<PaletteItem> PaletteMatches { get; } = [];

    [ObservableProperty]
    public partial RepositoryViewModel? ActiveTab { get; set; }

    [ObservableProperty]
    public partial string? GitExecutable { get; set; }

    [ObservableProperty]
    public partial bool GitReady { get; set; }

    [ObservableProperty]
    public partial string GitProblem { get; set; } = "";

    [ObservableProperty]
    public partial bool HasGitProblem { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial string TitleText { get; set; } = "Sextant";

    [ObservableProperty]
    public partial bool PaletteOpen { get; set; }

    [ObservableProperty]
    public partial string PaletteQuery { get; set; } = "";

    [ObservableProperty]
    public partial PaletteItem? SelectedPalette { get; set; }

    public double LocationsWidth { get; set; }

    public double GraphWidth { get; set; }

    public double FilesHeight { get; set; }

    public bool CanUseGit => GitReady && !IsBusy;

    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

    public bool HasActiveTab => ActiveTab is not null;

    public bool ShowEmpty => ActiveTab is null;

    public void Attach(IDialogService dialogs) => Dialogs = dialogs;

    public Task InitializeAsync() => _initialize ??= InitializeCoreAsync();

    public void OnWindowActivated()
    {
        if (!_started)
            return;
        if (ActiveTab is { IsReady: true } tab)
            _ = tab.RefreshFromFocusAsync();
    }

    public void Shutdown()
    {
        Save();
        if (Interlocked.Exchange(ref _shutDown, 1) == 1)
            return;
        _lifetime.Cancel();
        foreach (var tab in Tabs.ToArray())
            _ = tab.DisposeAsync();
    }

    public void Activate(RepositoryViewModel tab)
    {
        if (!Tabs.Contains(tab))
            return;
        foreach (var other in Tabs)
            other.IsActive = other == tab;
        ActiveTab = tab;
        TitleText = $"Sextant — {tab.Title}";
        _ = tab.EnsureLoadedAsync();
        Save();
    }

    public void Close(RepositoryViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;
        Tabs.Remove(tab);
        _ = tab.DisposeAsync();
        if (ActiveTab == tab)
        {
            if (Tabs.Count == 0)
            {
                ActiveTab = null;
                TitleText = "Sextant";
            }
            else
            {
                Activate(Tabs[Math.Min(index, Tabs.Count - 1)]);
                return;
            }
        }

        Save();
    }

    public void NoteLoaded(RepositoryViewModel tab)
    {
        if (tab.Toplevel is not { Length: > 0 })
            return;
        if (ActiveTab == tab)
            TitleText = $"Sextant — {tab.Title}";
        Save();
    }

    public void Save()
    {
        _workspace.OpenTabs = Tabs.Select(tab => tab.Toplevel ?? tab.RequestedPath).ToList();
        _workspace.ActiveTab = ActiveTab is null ? null : ActiveTab.Toplevel ?? ActiveTab.RequestedPath;
        _workspace.LocationsWidth = LocationsWidth;
        _workspace.GraphWidth = GraphWidth;
        _workspace.FilesHeight = FilesHeight;
        _store.SaveWorkspace(_workspace);
    }

    [RelayCommand]
    public Task OpenFolder() => OpenFolderAsync();

    [RelayCommand]
    public Task Clone() => CloneAsync();

    [RelayCommand]
    public Task Init() => InitAsync();

    [RelayCommand]
    public Task LocateGit() => LocateGitAsync();

    [RelayCommand]
    public void TogglePalette()
    {
        if (PaletteOpen)
        {
            ClosePalette();
            return;
        }

        RebuildPalette();
        PaletteQuery = "";
        FilterPalette();
        PaletteOpen = true;
    }

    [RelayCommand]
    public void ClosePalette() => PaletteOpen = false;

    [RelayCommand]
    public void CloseActive()
    {
        if (ActiveTab is not null)
            Close(ActiveTab);
    }

    [RelayCommand]
    public void NextTab()
    {
        if (Tabs.Count < 2 || ActiveTab is null)
            return;
        var index = Tabs.IndexOf(ActiveTab);
        Activate(Tabs[(index + 1) % Tabs.Count]);
    }

    [RelayCommand]
    public void RefreshActive()
    {
        if (ActiveTab is not null)
            _ = ActiveTab.Refresh();
    }

    public Task RunPaletteAsync()
    {
        var item = SelectedPalette ?? PaletteMatches.FirstOrDefault();
        PaletteOpen = false;
        return item is null ? Task.CompletedTask : item.Run();
    }

    public void MovePalette(int delta)
    {
        if (PaletteMatches.Count == 0)
            return;
        var index = SelectedPalette is null ? 0 : PaletteMatches.IndexOf(SelectedPalette);
        if (index < 0)
            index = 0;
        var next = Math.Clamp(index + delta, 0, PaletteMatches.Count - 1);
        SelectedPalette = PaletteMatches[next];
    }

    partial void OnGitReadyChanged(bool value) => OnPropertyChanged(nameof(CanUseGit));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanUseGit));

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatusText));

    partial void OnActiveTabChanged(RepositoryViewModel? value)
    {
        OnPropertyChanged(nameof(HasActiveTab));
        OnPropertyChanged(nameof(ShowEmpty));
    }

    partial void OnPaletteQueryChanged(string value) => FilterPalette();

    private async Task InitializeCoreAsync()
    {
        await ProbeAsync();
        if (_settings.ReopenTabs)
        {
            foreach (var path in _workspace.OpenTabs)
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    continue;
                if (Tabs.Any(tab => SameTab(tab, path)))
                    continue;
                Tabs.Add(new RepositoryViewModel(this, path));
            }

            var active = Tabs.FirstOrDefault(tab => _workspace.ActiveTab is not null && SameTab(tab, _workspace.ActiveTab));
            if (active is null && Tabs.Count > 0)
                active = Tabs[0];
            if (active is not null)
                Activate(active);
        }

        _started = true;
    }

    private async Task ProbeAsync()
    {
        string? path = null;
        if (!string.IsNullOrWhiteSpace(_settings.GitExecutable) && File.Exists(_settings.GitExecutable))
            path = _settings.GitExecutable;
        else
            path = GitLocator.FindOnPath();

        if (path is null)
        {
            SetGitProblem("Git was not found on PATH. Use Locate git to pick the executable.");
            return;
        }

        try
        {
            var version = await GitLocator.ProbeAsync(Runner, path, _lifetime.Token);
            GitExecutable = path;
            if (!GitVersions.IsSupported(version))
            {
                SetGitProblem($"Git {version.Raw} is older than 2.43.");
                GitReady = false;
                return;
            }

            GitReady = true;
            HasGitProblem = false;
            GitProblem = "";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            GitExecutable = path;
            GitReady = false;
            SetGitProblem(exception.Message);
        }
    }

    private async Task OpenFolderAsync()
    {
        if (!CanUseGit || Dialogs is null)
            return;
        var path = await Dialogs.PickFolderAsync("Open repository");
        if (string.IsNullOrWhiteSpace(path))
            return;
        await OpenPathAsync(path);
    }

    private async Task OpenPathAsync(string path)
    {
        if (!GitReady || GitExecutable is null)
            return;
        if (!Directory.Exists(path))
        {
            StatusText = "That folder does not exist.";
            return;
        }

        var existing = Tabs.FirstOrDefault(tab => SameTab(tab, path));
        if (existing is not null)
        {
            Activate(existing);
            return;
        }

        var tab = new RepositoryViewModel(this, path);
        Tabs.Add(tab);
        Activate(tab);
        await tab.EnsureLoadedAsync();
        if (!Tabs.Contains(tab))
            return;
        if (tab.Toplevel is not null)
        {
            var duplicate = Tabs.FirstOrDefault(other => other != tab && other.Toplevel is not null && RepoPath.Same(other.Toplevel, tab.Toplevel));
            if (duplicate is not null)
            {
                Close(tab);
                Activate(duplicate);
                return;
            }
        }

        Save();
    }

    private async Task CloneAsync()
    {
        if (!CanUseGit || Dialogs is null || GitExecutable is null)
            return;
        var request = await Dialogs.PromptCloneAsync();
        if (request is null)
            return;
        IsBusy = true;
        StatusText = "Cloning…";
        var progress = new Progress<string>(text =>
        {
            if (!string.IsNullOrWhiteSpace(text))
                StatusText = text;
        });
        try
        {
            await RepositoryAdmin.CloneAsync(Runner, GitExecutable, request.Url, request.Destination, progress, _lifetime.Token);
            StatusText = "";
            await OpenPathAsync(request.Destination);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Clone cancelled.";
        }
        catch (GitCommandFailedException exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InitAsync()
    {
        if (!CanUseGit || Dialogs is null || GitExecutable is null)
            return;
        var path = await Dialogs.PickFolderAsync("Init repository");
        if (string.IsNullOrWhiteSpace(path))
            return;
        IsBusy = true;
        StatusText = "Initializing…";
        try
        {
            await RepositoryAdmin.InitAsync(Runner, GitExecutable, path, _lifetime.Token);
            StatusText = "";
            await OpenPathAsync(path);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Init cancelled.";
        }
        catch (GitCommandFailedException exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LocateGitAsync()
    {
        if (Dialogs is null || IsBusy)
            return;
        var path = await Dialogs.PickGitExecutableAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            var version = await GitLocator.ProbeAsync(Runner, path, _lifetime.Token);
            GitExecutable = path;
            _settings.GitExecutable = path;
            _store.SaveSettings(_settings);
            if (!GitVersions.IsSupported(version))
            {
                GitReady = false;
                SetGitProblem($"Git {version.Raw} is older than 2.43.");
                return;
            }

            GitReady = true;
            HasGitProblem = false;
            GitProblem = "";
            StatusText = version.Raw;
        }
        catch (Exception exception)
        {
            GitReady = false;
            SetGitProblem(exception.Message);
        }
    }

    private static bool SameTab(RepositoryViewModel tab, string path)
    {
        if (RepoPath.Same(tab.RequestedPath, path))
            return true;
        return tab.Toplevel is not null && RepoPath.Same(tab.Toplevel, path);
    }

    private void SetGitProblem(string message)
    {
        GitProblem = message;
        HasGitProblem = true;
        GitReady = false;
    }

    private void RebuildPalette()
    {
        _palette.Clear();
        _palette.Add(new PaletteItem { Title = "Open repository", Run = OpenFolderAsync });
        _palette.Add(new PaletteItem { Title = "Clone repository", Run = CloneAsync });
        _palette.Add(new PaletteItem { Title = "Init repository", Run = InitAsync });
        _palette.Add(new PaletteItem { Title = "Locate git", Run = LocateGitAsync });
        if (ActiveTab is { } tab)
        {
            _palette.Add(new PaletteItem { Title = "Refresh", Run = tab.Refresh });
            _palette.Add(new PaletteItem { Title = "Fetch", Run = tab.Fetch });
            _palette.Add(new PaletteItem { Title = "Pull", Run = tab.Pull });
            _palette.Add(new PaletteItem { Title = "Push", Run = tab.Push });
            _palette.Add(new PaletteItem { Title = "Commit", Run = tab.Commit });
            _palette.Add(new PaletteItem { Title = "Commit without hooks", Run = () => tab.CommitWithoutHooksCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Stage all", Run = () => tab.StageAllCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Unstage all", Run = () => tab.UnstageAllCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Checkout branch", Run = tab.CheckoutFromPalette });
            _palette.Add(new PaletteItem { Title = "Create branch", Run = tab.CreateBranch });
            _palette.Add(new PaletteItem { Title = "Merge branch", Run = tab.MergeFromPalette });
            _palette.Add(new PaletteItem { Title = "Stash", Run = () => tab.StashCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Add remote", Run = () => tab.AddRemoteCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Toggle side-by-side diff", Run = () => tab.ToggleSideBySideCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Toggle ignore whitespace", Run = () => tab.ToggleWhitespaceCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Toggle all files", Run = () => tab.ToggleAllFilesCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Toggle blame", Run = () => tab.ToggleBlameCommand.ExecuteAsync(null) });
            if (tab.IsConflicted)
                _palette.Add(new PaletteItem { Title = "Abort", Run = tab.AbortMerge });
            _palette.Add(new PaletteItem { Title = "Toggle command log", Run = () => { tab.CommandsOpen = !tab.CommandsOpen; return Task.CompletedTask; } });
        }

        _palette.Add(new PaletteItem { Title = "Next tab", Run = () => { NextTab(); return Task.CompletedTask; } });
        _palette.Add(new PaletteItem { Title = "Close tab", Run = () => { CloseActive(); return Task.CompletedTask; } });
    }

    private void FilterPalette()
    {
        var tokens = PaletteQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        PaletteMatches.Clear();
        foreach (var item in _palette)
        {
            if (tokens.All(token => item.Title.Contains(token, StringComparison.OrdinalIgnoreCase)))
                PaletteMatches.Add(item);
        }

        SelectedPalette = PaletteMatches.FirstOrDefault();
    }
}
