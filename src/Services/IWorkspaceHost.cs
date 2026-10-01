using Sextant.Git;
using Sextant.ViewModels;

namespace Sextant.Services;

public interface IDialogService
{
    Task<string?> PickFolderAsync(string title);

    Task<string?> PickGitExecutableAsync();

    Task<bool> ConfirmAsync(string title, string message, string confirm = "OK");

    Task<string?> PromptAsync(string title, string message, string initial = "", bool allowEmpty = false);

    Task<CloneRequest?> PromptCloneAsync();

    Task<string?> PickAsync(string title, string message, IReadOnlyList<string> options);

    Task<PerformanceChoice?> ConfirmPerformanceAsync(PerformanceSuggestion suggestion);

    Task CopyAsync(string text);
}

public sealed record CloneRequest(string Url, string Destination);

public sealed record PerformanceChoice(bool ManyFiles, bool FileSystemMonitor);

public interface IWorkspaceHost
{
    IDialogService? Dialogs { get; }

    GitProcessRunner Runner { get; }

    string? GitExecutable { get; }

    bool GitReady { get; }

    double LocationsWidth { get; }

    double GraphWidth { get; }

    double FilesHeight { get; }

    void Activate(RepositoryViewModel tab);

    void Close(RepositoryViewModel tab);

    void NoteLoaded(RepositoryViewModel tab);

    void Save();
}
