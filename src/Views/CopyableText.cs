using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Sextant.ViewModels;

namespace Sextant.Views;

public class CopyableText : SelectableTextBlock
{
    public static readonly StyledProperty<string> CopyKeyProperty =
        AvaloniaProperty.Register<CopyableText, string>(nameof(CopyKey), "body");

    public CopyableText()
    {
        IsTabStop = false;
        var copy = new MenuItem { Header = "Copy", InputGesture = TextBox.CopyGesture };
        var all = new MenuItem { Header = "Select all", InputGesture = AppGestures.CommandKey(Key.A) };
        copy.Click += (_, _) => Host?.Copy();
        all.Click += (_, _) => Host?.SelectAll(this);
        var flyout = new MenuFlyout { Items = { copy, all } };
        flyout.Opening += (_, _) => copy.IsEnabled = Host?.HasSelection() == true;
        ContextFlyout = flyout;
    }

    public string CopyKey
    {
        get => GetValue(CopyKeyProperty);
        set => SetValue(CopyKeyProperty, value);
    }

    /// <summary>
    /// Length of the line when the displayed text lives in inlines and <see cref="TextBlock.Text"/> is empty.
    /// </summary>
    public int PlainLength { get; set; } = -1;

    public int Hit(PointerEventArgs e)
    {
        var point = e.GetPosition(this) - new Point(Padding.Left, Padding.Top);
        var index = TextLayout.HitTestPoint(point).TextPosition;
        var length = PlainLength >= 0 ? PlainLength : (Text?.Length ?? 0);
        if (index < 0)
            return 0;
        return index > length ? length : index;
    }

    public int LineIndex()
    {
        var host = Host;
        if (host is null)
            return -1;
        Visual? node = this;
        while (node is not null && node != host)
        {
            if (node is Control control)
            {
                var index = host.IndexFromContainer(control);
                if (index >= 0)
                    return index;
            }

            node = node.GetVisualParent();
        }

        return -1;
    }

    public void ShowSlice(int start, int end)
    {
        if (SelectionStart == start && SelectionEnd == end)
            return;
        SelectionStart = start;
        SelectionEnd = end;
    }

    public void HideSlice()
    {
        if (SelectionStart != SelectionEnd)
            ClearSelection();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Host?.Apply(this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            Host?.Apply(this);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        Focus();
        var line = LineIndex();
        if (line >= 0)
            Host?.Begin(this, line, SelectionStart, SelectionEnd);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured == this && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Host?.Move(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var host = Host;
        var right = e.InitialPressMouseButton == MouseButton.Right;
        var inside = right && host?.Covers(LineIndex(), Hit(e)) == true;
        base.OnPointerReleased(e);
        if (!right || host is null)
            return;
        if (inside)
            host.ApplyAll();
        else
            host.Clear();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var map = Application.Current?.PlatformSettings?.HotkeyConfiguration;
        if (map is not null && map.Copy.Any(gesture => gesture.Matches(e)))
        {
            Host?.Copy();
            e.Handled = true;
            return;
        }

        if (map is not null && map.SelectAll.Any(gesture => gesture.Matches(e)))
        {
            Host?.SelectAll(this);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        // SelectableTextBlock clears its highlight here. A selection covers several
        // rows, and the copy menu takes focus while that selection should stay.
    }

    private CopyListBox? Host => this.FindAncestorOfType<CopyListBox>();
}

public static class CopyText
{
    public static bool Participates(string controlKey, string? selectionKey)
    {
        if (selectionKey is null)
            return false;
        if (controlKey == selectionKey)
            return true;
        return controlKey == "span" && selectionKey is "body" or "left" or "right";
    }

    public static string? Of(object? item, string key) => item switch
    {
        DiffLineRow row when key == "body" => row.Text,
        DiffHunkRow row when key is "body" or "left" or "right" or "span" => row.Header,
        DiffFileRow row when key is "body" or "left" or "right" or "span" => row.Label,
        DiffSideRow row when key == "left" => row.Left,
        DiffSideRow row when key == "right" => row.Right,
        BlameRow row when key == "blame" => row.Text,
        BlameRow row when key == "meta" => row.Meta,
        BlameRow row when key == "number" => row.Number,
        MergeRegionRow row when key == "context" && row.IsContext => row.Context,
        MergeRegionRow row when key == "ours" && row.IsConflict => row.OursDisplay,
        MergeRegionRow row when key == "theirs" && row.IsConflict => row.TheirsDisplay,
        MergeRegionRow row when key == "base" && row.ShowBaseSection => row.BaseDisplay,
        _ => null,
    };
}
