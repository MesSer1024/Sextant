using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant;
using Sextant.ViewModels;
using System.ComponentModel;

namespace Sextant.Views;

public partial class RepositoryView : UserControl
{
    private readonly record struct FileAnchor(bool Header, string Key, bool FromStaged, double Top);

    private bool _widthsApplied;
    private bool _scrollHooked;
    private RepositoryViewModel? _scrollVm;
    private RepositoryViewModel? _watched;
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
            if (vm.LocationsWidth >= 140)
                LocationsColumn.Width = new GridLength(vm.LocationsWidth);
            if (vm.GraphWidth >= 240)
                GraphColumn.Width = new GridLength(vm.GraphWidth);
            if (vm.FilesHeight >= 80)
                FilesRow.Height = new GridLength(vm.FilesHeight);
        }

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
        UnhookFileScroll();
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnViewModelPropertyChanged;
            _watched = null;
        }

        base.OnUnloaded(e);
    }

    private void WatchViewModel()
    {
        var next = DataContext as RepositoryViewModel;
        if (ReferenceEquals(_watched, next))
            return;
        if (_watched is not null)
            _watched.PropertyChanged -= OnViewModelPropertyChanged;
        _watched = next;
        if (_watched is not null)
        {
            _watched.PropertyChanged += OnViewModelPropertyChanged;
            ApplyCommandLog(_watched.CommandLog);
        }
    }

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
    }

    private ColumnDefinition LocationsColumn => Columns.ColumnDefinitions[0];

    private ColumnDefinition GraphColumn => Columns.ColumnDefinitions[2];

    private RowDefinition FilesRow => Details.RowDefinitions[2];

    public void ReadWidths(MainViewModel vm)
    {
        if (LocationsColumn.Width.GridUnitType == GridUnitType.Pixel && LocationsColumn.Width.Value >= 140)
            vm.LocationsWidth = LocationsColumn.Width.Value;
        if (GraphColumn.Width.GridUnitType == GridUnitType.Pixel && GraphColumn.Width.Value >= 240)
            vm.GraphWidth = GraphColumn.Width.Value;
        if (FilesRow.Height.GridUnitType == GridUnitType.Pixel && FilesRow.Height.Value >= 80)
            vm.FilesHeight = FilesRow.Height.Value;
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
        item.ShowCheckout || item.ShowMerge || item.ShowDelete || item.ShowSetUpstream || item.ShowReveal
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
