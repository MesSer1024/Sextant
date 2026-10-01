using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Sextant.Git;

namespace Sextant.Services;

public sealed class AvaloniaDialogService : IDialogService
{
    private readonly Window _owner;

    public AvaloniaDialogService(Window owner) => _owner = owner;

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    public async Task<string?> PickGitExecutableAsync()
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Locate git",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("git") { Patterns = OperatingSystem.IsWindows() ? ["git.exe"] : ["git"] },
                FilePickerFileTypes.All,
            ],
        });
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirm = "OK")
    {
        var window = Create(title);
        var accepted = false;
        var ok = new Button { Content = confirm, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) =>
        {
            accepted = true;
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();
        window.Content = Column(
            Message(message),
            Buttons(cancel, ok));
        await window.ShowDialog(_owner);
        return accepted;
    }

    public async Task<string?> PromptAsync(string title, string message, string initial = "", bool allowEmpty = false)
    {
        var window = Create(title);
        var box = new TextBox { Text = initial, PlaceholderText = message };
        string? value = null;
        var accepted = false;
        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) =>
        {
            accepted = true;
            if (!string.IsNullOrWhiteSpace(box.Text))
                value = box.Text.Trim();
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();
        window.Content = Column(Message(message), box, Buttons(cancel, ok));
        await window.ShowDialog(_owner);
        if (allowEmpty && accepted)
            return value ?? "";
        return value;
    }

    public async Task<CloneRequest?> PromptCloneAsync()
    {
        var window = Create("Clone repository");
        var url = new TextBox { PlaceholderText = "https://example.com/repo.git" };
        var parent = new TextBox { PlaceholderText = "Parent folder" };
        var folder = new TextBox { PlaceholderText = "Folder name (optional)" };
        var browse = new Button { Content = "Browse…" };
        browse.Click += async (_, _) =>
        {
            var picked = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Clone into",
                AllowMultiple = false,
            });
            if (picked.Count > 0)
                parent.Text = picked[0].Path.LocalPath;
        };
        CloneRequest? request = null;
        var ok = new Button { Content = "Clone", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(url.Text) || string.IsNullOrWhiteSpace(parent.Text))
            {
                error.Text = "Enter a URL and a parent folder.";
                return;
            }

            var name = string.IsNullOrWhiteSpace(folder.Text) ? NameFromUrl(url.Text.Trim()) : folder.Text.Trim();
            request = new CloneRequest(url.Text.Trim(), Path.Combine(parent.Text.Trim(), name));
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();
        var parentRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        parentRow.Children.Add(parent);
        Grid.SetColumn(browse, 1);
        parentRow.Children.Add(browse);
        window.Content = Column(
            Message("Clone with the system git binary."),
            Labeled("URL", url),
            Labeled("Parent folder", parentRow),
            Labeled("Folder name", folder),
            error,
            Buttons(cancel, ok));
        await window.ShowDialog(_owner);
        return request;
    }

    public async Task<string?> PickAsync(string title, string message, IReadOnlyList<string> options)
    {
        var window = Create(title);
        var list = new ListBox { ItemsSource = options, MaxHeight = 240 };
        string? selected = null;
        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) =>
        {
            selected = list.SelectedItem as string;
            if (selected is not null)
                window.Close();
        };
        cancel.Click += (_, _) => window.Close();
        window.Content = Column(Message(message), list, Buttons(cancel, ok));
        await window.ShowDialog(_owner);
        return selected;
    }

    public async Task<PerformanceChoice?> ConfirmPerformanceAsync(PerformanceSuggestion suggestion)
    {
        var window = Create("Speed up status");
        var many = new CheckBox
        {
            Content = "git config --local feature.manyFiles true",
            IsChecked = suggestion.ManyFiles,
            IsEnabled = suggestion.ManyFiles,
        };
        var monitor = new CheckBox
        {
            Content = "git config --local core.fsmonitor true",
            IsChecked = suggestion.FileSystemMonitor,
            IsEnabled = suggestion.FileSystemMonitor,
        };
        PerformanceChoice? choice = null;
        var ok = new Button { Content = "Apply", IsDefault = true };
        var cancel = new Button { Content = "Not now", IsCancel = true };
        ok.Click += (_, _) =>
        {
            choice = new PerformanceChoice(many.IsChecked == true, monitor.IsChecked == true);
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();
        window.Content = Column(
            Message("Status in this repository is slow. These settings are written to this repository's .git/config only after you apply them."),
            many,
            monitor,
            Buttons(cancel, ok));
        await window.ShowDialog(_owner);
        if (choice is { ManyFiles: false, FileSystemMonitor: false })
            return null;
        return choice;
    }

    public async Task CopyAsync(string text)
    {
        var clipboard = _owner.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }

    private static Window Create(string title) => new()
    {
        Title = title,
        Width = 520,
        MinWidth = 420,
        SizeToContent = SizeToContent.Height,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        CanResize = false,
        Padding = new Avalonia.Thickness(16),
    };

    private static TextBlock Message(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
    };

    private static Control Labeled(string label, Control control)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = label, Opacity = 0.75 });
        panel.Children.Add(control);
        return panel;
    }

    private static StackPanel Column(params Control[] controls)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var control in controls)
            panel.Children.Add(control);
        return panel;
    }

    private static StackPanel Buttons(params Button[] buttons)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        foreach (var button in buttons)
            panel.Children.Add(button);
        return panel;
    }

    private static string NameFromUrl(string url)
    {
        var trimmed = url.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        var name = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        return name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
