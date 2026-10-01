using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Sextant.Views;

public class CopyListBox : ListBox
{
    // The Fluent theme is keyed to ListBox. A subclass keeps its own style key otherwise,
    // gets no control template, and the diff never builds an items presenter.
    protected override Type StyleKeyOverride => typeof(ListBox);

    private INotifyCollectionChanged? _source;
    private TopLevel? _window;
    private string? _key;
    private int _anchorLine;
    private int _anchorChar;
    private int _focusLine;
    private int _focusChar;
    private bool _active;

    public CopyListBox()
    {
        ContainerPrepared += (_, e) => ApplyContainer(e.Container);
        ContainerIndexChanged += (_, e) => ApplyContainer(e.Container);
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) =>
        new CopyListBoxItem();

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey) =>
        NeedsContainer<CopyListBoxItem>(item, out recycleKey);

    public void Begin(CopyableText source, int line, int selectionStart, int selectionEnd)
    {
        _key = source.CopyKey;
        _anchorLine = line;
        _anchorChar = selectionStart;
        _focusLine = line;
        _focusChar = selectionEnd;
        _active = true;
        ApplyAll();
    }

    public void Move(PointerEventArgs e)
    {
        if (!_active || _key is null)
            return;
        AutoScroll(e);
        var text = HitText(e.GetPosition(this));
        if (text is null)
            return;
        if (_key == "span" && text.CopyKey is "left" or "right" or "body")
            _key = text.CopyKey;
        if (!CopyText.Participates(text.CopyKey, _key))
        {
            text = TextOnRow(text, _key);
            if (text is null)
                return;
        }

        var line = text.LineIndex();
        if (line < 0)
            return;
        _focusLine = line;
        _focusChar = text.Hit(e);
        ApplyAll();
    }

    public void SelectAll(CopyableText source)
    {
        var key = source.CopyKey;
        if (key == "span")
        {
            if (Any("body"))
                key = "body";
            else if (Any("left"))
                key = "left";
            else if (Any("right"))
                key = "right";
        }

        var lines = Lines(key);
        var first = -1;
        var last = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i] is null)
                continue;
            if (first < 0)
                first = i;
            last = i;
        }

        if (first < 0 || last < 0)
            return;
        _key = key;
        _anchorLine = first;
        _anchorChar = 0;
        _focusLine = last;
        _focusChar = lines[last]!.Length;
        _active = true;
        ApplyAll();
        source.Focus();
    }

    public bool HasSelection() =>
        _active && _key is not null && SelectedString().Length > 0;

    public bool Covers(int line, int caret)
    {
        if (!_active || _key is null || line < 0)
            return false;
        var slice = LineCopy.Slice(Lines(_key), _anchorLine, _anchorChar, _focusLine, _focusChar, line);
        return slice is not null && caret >= slice.Value.Start && caret <= slice.Value.End;
    }

    public void Copy()
    {
        var text = SelectedString();
        if (text.Length == 0)
            return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;
        _ = CopyAsync(clipboard, text);
    }

    public void Clear()
    {
        if (!_active)
            return;
        _active = false;
        _key = null;
        ApplyAll();
    }

    public void Apply(CopyableText text)
    {
        if (!text.IsAttachedToVisualTree())
            return;
        var line = text.LineIndex();
        if (!_active || _key is null || line < 0 || !CopyText.Participates(text.CopyKey, _key))
        {
            text.HideSlice();
            return;
        }

        var slice = LineCopy.Slice(Lines(_key), _anchorLine, _anchorChar, _focusLine, _focusChar, line);
        if (slice is null)
            text.HideSlice();
        else
            text.ShowSlice(slice.Value.Start, slice.Value.End);
    }

    public void ApplyAll()
    {
        foreach (var container in GetRealizedContainers())
            ApplyContainer(container);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ItemsSourceProperty)
            return;
        if (_source is not null)
            _source.CollectionChanged -= OnItemsChanged;
        _source = change.NewValue as INotifyCollectionChanged;
        if (_source is not null)
            _source.CollectionChanged += OnItemsChanged;
        Clear();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this);
        _window?.AddHandler(PointerPressedEvent, OnWindowPressed, RoutingStrategies.Tunnel);
        if (_source is null && ItemsSource is INotifyCollectionChanged source)
        {
            _source = source;
            _source.CollectionChanged += OnItemsChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
        {
            _window.RemoveHandler(PointerPressedEvent, OnWindowPressed);
            _window = null;
        }

        if (_source is not null)
        {
            _source.CollectionChanged -= OnItemsChanged;
            _source = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Clear();

    private void OnWindowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_active || FlyoutOpen())
            return;
        if (e.Source is Visual visual && this.IsVisualAncestorOf(visual))
            return;
        Clear();
    }

    private bool FlyoutOpen()
    {
        foreach (var container in GetRealizedContainers())
        {
            foreach (var text in container.GetVisualDescendants().OfType<CopyableText>())
            {
                if (text.ContextFlyout?.IsOpen == true)
                    return true;
            }
        }

        return false;
    }

    private void ApplyContainer(Control container)
    {
        foreach (var text in container.GetVisualDescendants().OfType<CopyableText>())
            Apply(text);
    }

    private string SelectedString() =>
        _key is null ? "" : LineCopy.Join(Lines(_key), _anchorLine, _anchorChar, _focusLine, _focusChar);

    private string?[] Lines(string key)
    {
        var items = (IList)Items;
        var lines = new string?[items.Count];
        for (var i = 0; i < items.Count; i++)
            lines[i] = CopyText.Of(items[i], key);
        return lines;
    }

    private bool Any(string key)
    {
        var items = (IList)Items;
        for (var i = 0; i < items.Count; i++)
        {
            if (CopyText.Of(items[i], key) is not null)
                return true;
        }

        return false;
    }

    private CopyableText? HitText(Point point)
    {
        if (this.InputHitTest(point) is not Visual visual)
            return null;
        return visual.FindAncestorOfType<CopyableText>(includeSelf: true);
    }

    private static CopyableText? TextOnRow(CopyableText from, string key)
    {
        Visual? node = from;
        while (node is not null)
        {
            if (node is CopyListBoxItem item)
            {
                return item.GetVisualDescendants().OfType<CopyableText>()
                    .FirstOrDefault(text => CopyText.Participates(text.CopyKey, key));
            }

            node = node.GetVisualParent();
        }

        return null;
    }

    private void AutoScroll(PointerEventArgs e)
    {
        var scroll = this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null || Items.Count == 0)
            return;
        var y = e.GetPosition(scroll).Y;
        var toward = y < 28 ? _focusLine - 1 : y > scroll.Bounds.Height - 28 ? _focusLine + 1 : _focusLine;
        if (toward == _focusLine || toward < 0 || toward >= Items.Count)
            return;
        ScrollIntoView(toward);
    }

    private static async Task CopyAsync(IClipboard clipboard, string text)
    {
        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception)
        {
            // The clipboard can be busy. The selection stays so the user can try again.
        }
    }
}

public class CopyListBoxItem : ListBoxItem
{
    // The row content is hosted by the ListBoxItem template. A subclass without this key
    // has no content presenter, so the diff line is never added.
    protected override Type StyleKeyOverride => typeof(ListBoxItem);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // The text marks the press handled on the way up. Skipping the list selection
        // keeps the diff color and leaves the pointer with the text.
        if (e.Source is Visual source && source.FindAncestorOfType<CopyableText>(includeSelf: true) is not null)
            return;
        base.OnPointerPressed(e);
    }
}
