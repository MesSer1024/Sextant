using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;
using System.Windows.Input;

namespace Sextant.ViewModels;

public static class UiCommands
{
    public static ICommand Disabled { get; } = new RelayCommand(() => { }, () => false);
}

public static class DiffColors
{
    public static IBrush Added { get; } = new SolidColorBrush(Color.FromArgb(48, 61, 184, 107));

    public static IBrush Removed { get; } = new SolidColorBrush(Color.FromArgb(48, 220, 70, 70));

    public static IBrush Clear { get; } = Brushes.Transparent;
}

public partial class GraphRowViewModel : ObservableObject
{
    public bool IsWorkingCopy { get; init; }

    public bool ShowLanes { get; init; }

    public string? Sha { get; init; }

    public CommitRecord? Commit { get; init; }

    public LaneGeometry? Lanes { get; init; }

    public string Author { get; init; } = "";

    public string When { get; init; } = "";

    public string Meta => string.IsNullOrEmpty(Author) ? "" : Author + "  ·  " + When;

    [ObservableProperty]
    public partial string Subject { get; set; } = "";

    [ObservableProperty]
    public partial string RefText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsHead { get; set; }

    public bool ShowCheckout { get; init; }

    public ICommand CheckoutCommand { get; init; } = UiCommands.Disabled;

    public ICommand CreateBranchCommand { get; init; } = UiCommands.Disabled;

    public ICommand CopyShaCommand { get; init; } = UiCommands.Disabled;
}

public partial class LocationItem : ObservableObject
{
    public bool IsHeader { get; init; }

    public bool IsCurrent { get; init; }

    public string Key { get; init; } = "";

    public string Label { get; init; } = "";

    public string? Oid { get; init; }

    public FontWeight Weight => IsHeader || IsCurrent ? FontWeight.SemiBold : FontWeight.Normal;

    public bool ShowCheckout { get; init; }

    public bool ShowMerge { get; init; }

    public bool ShowDelete { get; init; }

    public bool ShowSetUpstream { get; init; }

    public bool ShowReveal { get; init; }

    public ICommand CheckoutCommand { get; init; } = UiCommands.Disabled;

    public ICommand MergeCommand { get; init; } = UiCommands.Disabled;

    public ICommand DeleteCommand { get; init; } = UiCommands.Disabled;

    public ICommand SetUpstreamCommand { get; init; } = UiCommands.Disabled;

    public ICommand RevealCommand { get; init; } = UiCommands.Disabled;
}

public partial class FileRowViewModel : ObservableObject
{
    public bool IsHeader { get; init; }

    public bool IsFile => !IsHeader;

    public string Path { get; init; } = "";

    public string Label { get; init; } = "";

    public string StatusText { get; init; } = "";

    public ChangeKind Kind { get; init; }

    public bool FromStagedList { get; init; }

    public bool Untracked { get; init; }

    public bool ShowStage { get; init; }

    public bool ShowUnstage { get; init; }

    public bool ShowDiscard { get; init; }

    public bool ShowMergetool { get; init; }

    public ICommand StageCommand { get; init; } = UiCommands.Disabled;

    public ICommand UnstageCommand { get; init; } = UiCommands.Disabled;

    public ICommand DiscardCommand { get; init; } = UiCommands.Disabled;

    public ICommand MergetoolCommand { get; init; } = UiCommands.Disabled;
}

public abstract class DiffRow;

public sealed class DiffHunkRow : DiffRow
{
    public required string Header { get; init; }

    public required string ActionLabel { get; init; }

    public bool ShowAction { get; init; }

    public ICommand ActionCommand { get; init; } = UiCommands.Disabled;
}

public sealed class DiffLineRow : DiffRow
{
    public required string Text { get; init; }

    public required IBrush Background { get; init; }
}

public sealed class PaletteItem
{
    public required string Title { get; init; }

    public required Func<Task> Run { get; init; }
}
