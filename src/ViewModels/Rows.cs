using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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

    [ObservableProperty]
    public partial string Subject { get; set; } = "";

    [ObservableProperty]
    public partial string Detail { get; set; } = "";

    [ObservableProperty]
    public partial bool IsHead { get; set; }

    public bool ShowCheckout { get; init; }

    public bool ShowRewrite { get; init; }

    public ICommand CheckoutCommand { get; init; } = UiCommands.Disabled;

    public ICommand CreateBranchCommand { get; init; } = UiCommands.Disabled;

    public ICommand CopyShaCommand { get; init; } = UiCommands.Disabled;

    public ICommand ResetSoftCommand { get; init; } = UiCommands.Disabled;

    public ICommand ResetMixedCommand { get; init; } = UiCommands.Disabled;

    public ICommand ResetHardCommand { get; init; } = UiCommands.Disabled;

    public ICommand CherryPickCommand { get; init; } = UiCommands.Disabled;

    public ICommand RevertCommand { get; init; } = UiCommands.Disabled;

    public ICommand RebaseCommand { get; init; } = UiCommands.Disabled;

    public ICommand RewordCommand { get; init; } = UiCommands.Disabled;

    public ICommand TagCommand { get; init; } = UiCommands.Disabled;
}

public sealed class ResetCollection<T> : ObservableCollection<T>
{
    public void Reset(IReadOnlyList<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

public partial class LocationItem : ObservableObject
{
    public bool IsHeader { get; init; }

    public bool IsCurrent { get; init; }

    public string Key { get; init; } = "";

    public string CollapseKey { get; set; } = "";

    public string Label { get; set; } = "";

    public int Depth { get; set; }

    public ObservableCollection<LocationItem> Children { get; } = [];

    public bool HasChildren => Children.Count > 0;

    public Thickness Indent => new(Depth * 14, 0, 0, 0);

    public double ExpandAngle => IsExpanded ? 90 : 0;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandAngle));

    public string? Oid { get; init; }

    public FontWeight Weight => IsHeader || IsCurrent ? FontWeight.SemiBold : FontWeight.Normal;

    public bool ShowCheckout { get; init; }

    public bool ShowMerge { get; init; }

    public bool ShowDelete { get; init; }

    public bool ShowSetUpstream { get; init; }

    public bool ShowReveal { get; init; }

    public bool ShowRename { get; init; }

    public bool ShowPop { get; init; }

    public bool ShowApply { get; init; }

    public bool ShowDrop { get; init; }

    public bool ShowOpen { get; init; }

    public ICommand OpenCommand { get; init; } = UiCommands.Disabled;

    public ICommand CheckoutCommand { get; init; } = UiCommands.Disabled;

    public ICommand MergeCommand { get; init; } = UiCommands.Disabled;

    public ICommand DeleteCommand { get; init; } = UiCommands.Disabled;

    public ICommand SetUpstreamCommand { get; init; } = UiCommands.Disabled;

    public ICommand RevealCommand { get; init; } = UiCommands.Disabled;

    public ICommand RenameCommand { get; init; } = UiCommands.Disabled;

    public ICommand PopCommand { get; init; } = UiCommands.Disabled;

    public ICommand ApplyCommand { get; init; } = UiCommands.Disabled;

    public ICommand DropCommand { get; init; } = UiCommands.Disabled;
}

public partial class FileRowViewModel : ObservableObject
{
    public bool IsHeader { get; init; }

    public bool IsFile => !IsHeader;

    public string Path { get; init; } = "";

    public string? OriginalPath { get; init; }

    public string Label { get; init; } = "";

    public string StatusText { get; init; } = "";

    public ChangeKind Kind { get; init; }

    public bool FromStagedList { get; init; }

    public bool Untracked { get; init; }

    public bool ShowStage { get; init; }

    public bool ShowUnstage { get; init; }

    public bool ShowDiscard { get; init; }

    public bool ShowMergetool { get; init; }

    public bool ShowHistory { get; init; }

    public ICommand StageCommand { get; init; } = UiCommands.Disabled;

    public ICommand UnstageCommand { get; init; } = UiCommands.Disabled;

    public ICommand DiscardCommand { get; init; } = UiCommands.Disabled;

    public ICommand MergetoolCommand { get; init; } = UiCommands.Disabled;

    public ICommand HistoryCommand { get; init; } = UiCommands.Disabled;
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

    /// <summary>This row is the rest of the previous logical line. Copy does not insert a break.</summary>
    public bool Continues { get; init; }

    public string? Language { get; init; }

    public required IBrush Background { get; init; }

    public bool ShowAction { get; init; }

    public string ActionLabel { get; init; } = "";

    public ICommand ActionCommand { get; init; } = UiCommands.Disabled;
}

public sealed class DiffSideRow : DiffRow
{
    public required string Left { get; init; }

    public required string Right { get; init; }

    public bool SkipLeftCopy { get; init; }

    public bool SkipRightCopy { get; init; }

    public bool LeftContinues { get; init; }

    public bool RightContinues { get; init; }

    public string? Language { get; init; }

    public required IBrush LeftBackground { get; init; }

    public required IBrush RightBackground { get; init; }
}

public sealed class DiffFileRow : DiffRow
{
    public required string Label { get; init; }
}

public sealed class BlameRow
{
    public required string Number { get; init; }

    public required string Meta { get; init; }

    public required string Text { get; init; }

    /// <summary>This row is the rest of the previous logical line. Copy does not insert a break.</summary>
    public bool Continues { get; init; }
}

public partial class MergeRegionRow : ObservableObject
{
    public bool IsConflict { get; init; }

    public bool IsContext => !IsConflict;

    public string Context { get; init; } = "";

    public string Ours { get; init; } = "";

    public string Theirs { get; init; } = "";

    public string BaseText { get; init; } = "";

    public bool HasBase { get; init; }

    public string OursDisplay => Ours.Length == 0 ? "(empty)" : Ours;

    public string TheirsDisplay => Theirs.Length == 0 ? "(empty)" : Theirs;

    public string BaseDisplay => HasBase
        ? (BaseText.Length == 0 ? "(empty)" : BaseText)
        : "No base in this region.";

    public bool ShowBaseSection => IsConflict && ShowBase;

    [ObservableProperty]
    public partial string Result { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowBase { get; set; }

    partial void OnShowBaseChanged(bool value) => OnPropertyChanged(nameof(ShowBaseSection));

    [RelayCommand]
    private void TakeOurs() => Result = Ours;

    [RelayCommand]
    private void TakeTheirs() => Result = Theirs;
}

public sealed class PaletteItem
{
    public required string Title { get; init; }

    public required Func<Task> Run { get; init; }
}
