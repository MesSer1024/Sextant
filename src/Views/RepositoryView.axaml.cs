using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant;
using Sextant.Git.Parsing;
using Sextant.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Sextant.Views;

public partial class RepositoryView : UserControl
{
    private readonly record struct FileAnchor(bool Header, string Key, bool FromStaged, double Top);

    private bool _widthsApplied;
    private bool _scrollHooked;
    private RepositoryViewModel? _scrollVm;
    private RepositoryViewModel? _watched;
    private ResetCollection<ImageCompareRow>? _images;
    private ObservableCollection<DiffRow>? _diffRows;
    private string? _jumpPath;
    private string? _jumpOriginal;
    private bool _followImages;
    private string _appliedCommandLog = "";

    public RepositoryView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is RepositoryViewModel vm && !_widthsApplied)
        {
            _widthsApplied = true;
            ApplyWidths(vm);
        }

        WatchPanes();
        AttachGraphScroll();
        GraphList.TemplateApplied += (_, _) => AttachGraphScroll();
        HookFileScroll();
        WatchViewModel();
        CommandLogSelectAllItem.InputGesture = AppGestures.CommandKey(Key.A);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (IsLoaded)
        {
            HookFileScroll();
            WatchViewModel();
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_widthsApplied)
            PublishPanes();
        UnwatchPanes();
        UnhookFileScroll();
        UnwatchViewModel();
        base.OnUnloaded(e);
    }

    private void WatchViewModel()
    {
        var next = DataContext as RepositoryViewModel;
        if (ReferenceEquals(_watched, next))
            return;
        UnwatchViewModel();
        _watched = next;
        if (_watched is null)
            return;
        _watched.PropertyChanged += OnViewModelPropertyChanged;
        _watched.JumpToFile += OnJumpToFile;
        _images = _watched.ImageCompares;
        _images.CollectionChanged += OnImagesChanged;
        _diffRows = _watched.DiffRows;
        _diffRows.CollectionChanged += OnDiffRowsChanged;
        ApplyCommandLog(_watched.CommandLog);
    }

    private void UnwatchViewModel()
    {
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnViewModelPropertyChanged;
            _watched.JumpToFile -= OnJumpToFile;
            _watched = null;
        }

        if (_images is not null)
        {
            _images.CollectionChanged -= OnImagesChanged;
            _images = null;
        }

        if (_diffRows is not null)
        {
            _diffRows.CollectionChanged -= OnDiffRowsChanged;
            _diffRows = null;
        }
    }

    private void OnFilePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        if (e.Source is not Visual source)
            return;
        if (source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null)
            return;
        if (source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;
        if (DataContext is RepositoryViewModel vm)
            vm.RevealSelectedFile();
    }

    private void OnJumpToFile(string path, string? original)
    {
        _jumpPath = path;
        _jumpOriginal = original;
        _followImages = true;
        ScrollDiffTo(path, original);
        ScrollImageTo(path, original);
    }

    private void OnImagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_followImages || _jumpPath is not { } path)
            return;
        var original = _jumpOriginal;
        Dispatcher.UIThread.Post(() => ScrollImageTo(path, original), DispatcherPriority.Loaded);
    }

    private void OnDiffRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        _followImages = false;

    private void ScrollDiffTo(string path, string? original)
    {
        if (FindHeader(path, original) is not { } row)
            return;
        DiffList.ScrollIntoView(row);
        Dispatcher.UIThread.Post(() => AlignDiff(row, 0), DispatcherPriority.Loaded);
    }

    private void AlignDiff(DiffFileRow row, int pass)
    {
        var scroll = DiffList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var container = DiffList.ContainerFromItem(row) as Control;
        if (scroll is null || container is null)
        {
            if (pass < 6)
                Dispatcher.UIThread.Post(() => AlignDiff(row, pass + 1), DispatcherPriority.Loaded);
            return;
        }

        var point = container.TranslatePoint(default, scroll);
        if (point is null)
            return;
        var y = Math.Max(0, scroll.Offset.Y + point.Value.Y);
        if (Math.Abs(scroll.Offset.Y - y) > 0.5)
            scroll.Offset = new Vector(scroll.Offset.X, y);
        if (pass < 2)
            Dispatcher.UIThread.Post(() => AlignDiff(row, pass + 1), DispatcherPriority.Background);
    }

    private DiffFileRow? FindHeader(string path, string? original)
    {
        foreach (var item in DiffList.Items)
        {
            if (item is DiffFileRow row && MatchesFile(row.Path.Length > 0 ? row.Path : row.Label, path, original))
                return row;
        }

        return null;
    }

    private void ScrollImageTo(string path, string? original, int pass = 0)
    {
        if (!_followImages || !string.Equals(_jumpPath, path, StringComparison.Ordinal))
            return;
        if (DataContext is not RepositoryViewModel vm)
            return;
        var index = -1;
        for (var i = 0; i < vm.ImageCompares.Count; i++)
        {
            if (MatchesFile(vm.ImageCompares[i].Path, path, original))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return;
        if (ImageRows.ContainerFromIndex(index) is not Control container)
        {
            if (pass < 8)
                Dispatcher.UIThread.Post(() => ScrollImageTo(path, original, pass + 1), DispatcherPriority.Loaded);
            return;
        }

        var point = container.TranslatePoint(default, ImageStrip);
        if (point is null)
            return;
        var y = Math.Max(0, ImageStrip.Offset.Y + point.Value.Y);
        if (Math.Abs(ImageStrip.Offset.Y - y) > 0.5)
            ImageStrip.Offset = new Vector(ImageStrip.Offset.X, y);
    }

    private static bool MatchesFile(string candidate, string path, string? original) =>
        DiffParser.SameFile(candidate, path)
        || (original is { Length: > 0 } old && DiffParser.SameFile(candidate, old));

    private void ApplyCommandLog(string text)
    {
        if (_appliedCommandLog == text && (CommandLogBox.Text ?? "") == text)
            return;
        var box = CommandLogBox;
        var previous = box.Text ?? "";
        var start = box.SelectionStart;
        var end = box.SelectionEnd;
        var selected = start != end;
        var pinned = IsCommandLogPinned();
        var scroll = CommandLogScroll();
        var offset = scroll?.Offset ?? default;
        _appliedCommandLog = text;
        box.Text = text;
        if (selected && end <= text.Length && text.StartsWith(previous, StringComparison.Ordinal))
        {
            box.SelectionStart = start;
            box.SelectionEnd = end;
            HoldCommandLogScroll(offset);
            return;
        }

        if (!pinned && previous.Length > 0 && scroll is not null)
        {
            HoldCommandLogScroll(offset);
            return;
        }

        ScrollCommandLogToEnd();
    }

    private void HoldCommandLogScroll(Vector offset)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var later = CommandLogScroll();
            if (later is not null)
                later.Offset = offset;
        }, DispatcherPriority.Loaded);
    }

    private void ScrollCommandLogToEnd()
    {
        var box = CommandLogBox;
        box.CaretIndex = box.Text?.Length ?? 0;
        var lines = box.GetLineCount();
        if (lines > 0)
            box.ScrollToLine(lines - 1);
        Dispatcher.UIThread.Post(() =>
        {
            var later = CommandLogBox;
            if ((later.Text ?? "") != _appliedCommandLog)
                return;
            later.CaretIndex = _appliedCommandLog.Length;
            var count = later.GetLineCount();
            if (count > 0)
                later.ScrollToLine(count - 1);
        }, DispatcherPriority.Loaded);
    }

    private bool IsCommandLogPinned()
    {
        var scroll = CommandLogScroll();
        if (scroll is null || scroll.Extent.Height <= scroll.Viewport.Height + 1)
            return true;
        return scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 8;
    }

    private ScrollViewer? CommandLogScroll() =>
        CommandLogBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void OnCommandLogMenuOpening(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout flyout)
            return;
        foreach (var item in flyout.Items.OfType<MenuItem>())
        {
            if (item.Header is "Copy")
                item.IsEnabled = CommandLogBox.CanCopy;
            else if (item.Header is "Select all")
                item.IsEnabled = (CommandLogBox.Text?.Length ?? 0) > 0;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not RepositoryViewModel vm)
            return;
        if (e.PropertyName == nameof(RepositoryViewModel.ShowHistorySearch) && vm.ShowHistorySearch)
        {
            Dispatcher.UIThread.Post(() =>
            {
                HistorySearchBox.Focus();
                HistorySearchBox.SelectAll();
            }, DispatcherPriority.Background);
        }
        else if (e.PropertyName == nameof(RepositoryViewModel.CommandLog))
            ApplyCommandLog(vm.CommandLog);
        else if (e.PropertyName == nameof(RepositoryViewModel.CommandsOpen) && vm.CommandsOpen)
            Dispatcher.UIThread.Post(ScrollCommandLogToEnd, DispatcherPriority.Loaded);
        else if (_widthsApplied && e.PropertyName is nameof(RepositoryViewModel.LocationsWidth) or nameof(RepositoryViewModel.GraphWidth) or nameof(RepositoryViewModel.FilesHeight))
            ApplyWidths(vm);
    }

    private ColumnDefinition LocationsColumn => Columns.ColumnDefinitions[0];

    private ColumnDefinition GraphColumn => Columns.ColumnDefinitions[2];

    private RowDefinition FilesRow => HistoryColumn.RowDefinitions[0];

    public void ReadWidths()
    {
        if (_applyingWidths || DataContext is not RepositoryViewModel vm)
            return;
        var changed = false;
        if (LocationsColumn.Width.GridUnitType == GridUnitType.Pixel && LocationsColumn.Width.Value >= 140 && vm.LocationsWidth != LocationsColumn.Width.Value)
        {
            vm.LocationsWidth = LocationsColumn.Width.Value;
            changed = true;
        }

        if (GraphColumn.Width.GridUnitType == GridUnitType.Pixel && GraphColumn.Width.Value >= 240 && vm.GraphWidth != GraphColumn.Width.Value)
        {
            vm.GraphWidth = GraphColumn.Width.Value;
            changed = true;
        }

        if (FilesRow.Height.GridUnitType == GridUnitType.Pixel && FilesRow.Height.Value >= 80 && vm.FilesHeight != FilesRow.Height.Value)
        {
            vm.FilesHeight = FilesRow.Height.Value;
            changed = true;
        }

        if (changed)
            vm.NotePaneEdit();
    }

    private bool _applyingWidths;

    private void ApplyWidths(RepositoryViewModel vm)
    {
        _applyingWidths = true;
        try
        {
            if (vm.LocationsWidth >= 140)
                LocationsColumn.Width = new GridLength(vm.LocationsWidth);
            if (vm.GraphWidth >= 240)
                GraphColumn.Width = new GridLength(vm.GraphWidth);
            if (vm.FilesHeight >= 80)
                FilesRow.Height = new GridLength(vm.FilesHeight);
        }
        finally
        {
            _applyingWidths = false;
        }
    }

    private bool _panesWatched;

    private void WatchPanes()
    {
        if (_panesWatched)
            return;
        _panesWatched = true;
        LocationsColumn.PropertyChanged += OnPaneChanged;
        GraphColumn.PropertyChanged += OnPaneChanged;
        FilesRow.PropertyChanged += OnPaneChanged;
    }

    private void UnwatchPanes()
    {
        if (!_panesWatched)
            return;
        _panesWatched = false;
        LocationsColumn.PropertyChanged -= OnPaneChanged;
        GraphColumn.PropertyChanged -= OnPaneChanged;
        FilesRow.PropertyChanged -= OnPaneChanged;
    }

    private void OnPaneChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ColumnDefinition.WidthProperty && e.Property != RowDefinition.HeightProperty)
            return;
        PublishPanes();
    }

    private void PublishPanes()
    {
        if (!_widthsApplied || _applyingWidths)
            return;
        ReadWidths();
    }

    private void AttachGraphScroll()
    {
        if (_scrollHooked)
            return;
        var scroll = GraphList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null)
            return;
        _scrollHooked = true;
        scroll.ScrollChanged += OnGraphScroll;
    }

    private void HookFileScroll()
    {
        UnhookFileScroll();
        if (DataContext is not RepositoryViewModel vm)
            return;
        _scrollVm = vm;
        vm.PreserveFileScroll += OnPreserveFileScroll;
    }

    private void UnhookFileScroll()
    {
        if (_scrollVm is null)
            return;
        _scrollVm.PreserveFileScroll -= OnPreserveFileScroll;
        _scrollVm = null;
    }

    private void OnPreserveFileScroll(Action update)
    {
        var scroll = FileScroll();
        var anchors = CaptureFileAnchors(scroll);
        var offset = scroll?.Offset ?? default;
        var restoreAutoScroll = FileList.AutoScrollToSelectedItem;
        FileList.AutoScrollToSelectedItem = false;
        try
        {
            update();
        }
        finally
        {
            Dispatcher.UIThread.Post(
                () => FinishFileScroll(anchors, offset, restoreAutoScroll, pass: 0),
                DispatcherPriority.Loaded);
        }
    }

    private void FinishFileScroll(List<FileAnchor> anchors, Vector offset, bool restoreAutoScroll, int pass)
    {
        var scroll = FileScroll();
        if (scroll is null)
        {
            FileList.AutoScrollToSelectedItem = restoreAutoScroll;
            return;
        }

        if (pass == 0)
        {
            scroll.Offset = offset;
            Dispatcher.UIThread.Post(
                () => FinishFileScroll(anchors, offset, restoreAutoScroll, pass: 1),
                DispatcherPriority.Loaded);
            return;
        }

        var corrected = scroll.Offset;
        foreach (var anchor in anchors)
        {
            if (FindContainer(anchor) is not { } container)
                continue;
            var point = container.TranslatePoint(default, scroll);
            if (point is null)
                continue;
            var delta = point.Value.Y - anchor.Top;
            corrected = new Vector(scroll.Offset.X, Math.Max(0, scroll.Offset.Y + delta));
            if (Math.Abs(delta) > 0.5)
                scroll.Offset = corrected;
            else
                corrected = scroll.Offset;
            break;
        }

        FileList.AutoScrollToSelectedItem = restoreAutoScroll;
        var hold = corrected;
        Dispatcher.UIThread.Post(() =>
        {
            var later = FileScroll();
            if (later is not null)
                later.Offset = hold;
        }, DispatcherPriority.Background);
    }

    private List<FileAnchor> CaptureFileAnchors(ScrollViewer? scroll)
    {
        var anchors = new List<FileAnchor>();
        if (scroll is null)
            return anchors;
        foreach (var container in FileList.GetRealizedContainers().OfType<Control>())
        {
            if (FileList.ItemFromContainer(container) is not FileRowViewModel row)
                continue;
            var point = container.TranslatePoint(default, scroll);
            if (point is null)
                continue;
            var top = point.Value.Y;
            if (top + container.Bounds.Height <= 0 || top >= scroll.Bounds.Height)
                continue;
            anchors.Add(new FileAnchor(row.IsHeader, row.IsHeader ? row.Label : row.Path, row.FromStagedList, top));
        }

        anchors.Sort(static (left, right) => left.Top.CompareTo(right.Top));
        return anchors;
    }

    private Control? FindContainer(FileAnchor anchor)
    {
        foreach (var container in FileList.GetRealizedContainers().OfType<Control>())
        {
            if (FileList.ItemFromContainer(container) is not FileRowViewModel row)
                continue;
            if (row.IsHeader)
            {
                if (anchor.Header && row.Label == anchor.Key)
                    return container;
                continue;
            }

            if (!anchor.Header && row.Path == anchor.Key && row.FromStagedList == anchor.FromStaged)
                return container;
        }

        return null;
    }

    private ScrollViewer? FileScroll() =>
        FileList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void OnGraphScroll(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll || scroll.Extent.Height <= scroll.Viewport.Height)
            return;
        if (scroll.Offset.Y + scroll.Viewport.Height < scroll.Extent.Height - 48)
            return;
        if (DataContext is RepositoryViewModel vm)
            _ = vm.LoadMoreFromScrollAsync();
    }

    private void OnCommitKeyDown(object? sender, KeyEventArgs e)
    {
        if (AppGestures.Matches(e, Key.Enter) && DataContext is RepositoryViewModel vm)
        {
            vm.CommitCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnHistoryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is RepositoryViewModel vm)
        {
            vm.SearchHistoryCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnGraphSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not RepositoryViewModel vm || sender is not ListBox list)
            return;
        var rows = new List<GraphRowViewModel>();
        if (list.SelectedItems is not null)
        {
            foreach (var item in list.SelectedItems)
            {
                if (item is GraphRowViewModel row)
                    rows.Add(row);
            }
        }

        vm.NoteGraphSelection(rows);
    }

    private static bool HasLocationMenu(LocationItem item) =>
        item.ShowCheckout || item.ShowMerge || item.ShowRebase || item.ShowDelete || item.ShowSetUpstream || item.ShowReveal
        || item.ShowRename || item.ShowPop || item.ShowApply || item.ShowDrop || item.ShowOpen;

    private void OnLocationExpand(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: LocationItem item } && DataContext is RepositoryViewModel vm)
            vm.ToggleLocation(item);
    }

    private void OnLocationExpandDoubleTapped(object? sender, TappedEventArgs e) => e.Handled = true;

    private void OnLocationDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not RepositoryViewModel vm || vm.SelectedLocation is not { } item)
            return;
        if (item.HasChildren)
            vm.ToggleLocation(item);
        else
            vm.ActivateLocation(item);
    }

    private void OnMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.PlacementTarget is not Control target)
            return;
        menu.DataContext = target.DataContext;
        if (target.DataContext is LocationItem item && !HasLocationMenu(item))
            e.Cancel = true;
    }
}
