using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Sextant.Controls;

/// <summary>
/// Shows one clay still. When an orbit mesh is set, dragging turns it and the wheel zooms.
/// A new angle is drawn off the UI thread. Pictures without a mesh stay still.
/// </summary>
public sealed class FbxOrbitView : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<FbxOrbitView, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<FbxPreview.Orbit?> OrbitProperty =
        AvaloniaProperty.Register<FbxOrbitView, FbxPreview.Orbit?>(nameof(Orbit));

    private Bitmap? _frame;
    private Point _last;
    private float _yaw = FbxPreview.DefaultYaw;
    private float _pitch = FbxPreview.DefaultPitch;
    private float _zoom = FbxPreview.DefaultZoom;
    private int _request;
    private bool _drawing;
    private bool _dragging;
    private bool _attached = true;

    public FbxOrbitView()
    {
        DoubleTapped += (_, eventArgs) => ResetView(eventArgs);
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public FbxPreview.Orbit? Orbit
    {
        get => GetValue(OrbitProperty);
        set => SetValue(OrbitProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
            InvalidateVisual();
        if (change.Property == OrbitProperty)
            ResetAngles();
    }

    protected override Size MeasureOverride(Size available)
    {
        var bitmap = _frame ?? Source;
        if (bitmap is null)
            return default;
        var maxWidth = double.IsInfinity(available.Width) ? bitmap.PixelSize.Width : available.Width;
        var maxHeight = double.IsInfinity(available.Height) ? bitmap.PixelSize.Height : Math.Min(available.Height, 420);
        var scale = Math.Min(maxWidth / bitmap.PixelSize.Width, maxHeight / bitmap.PixelSize.Height);
        if (scale <= 0 || double.IsInfinity(scale))
            return default;
        return new Size(bitmap.PixelSize.Width * scale, bitmap.PixelSize.Height * scale);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        // A drawn fill is what Avalonia hit-tests. Transparent still receives the drag.
        context.FillRectangle(Brushes.Transparent, bounds);
        var bitmap = _frame ?? Source;
        if (bitmap is null)
            return;
        var scale = Math.Min(bounds.Width / bitmap.PixelSize.Width, bounds.Height / bitmap.PixelSize.Height);
        if (scale <= 0 || double.IsInfinity(scale))
            return;
        var width = bitmap.PixelSize.Width * scale;
        var height = bitmap.PixelSize.Height * scale;
        var x = (bounds.Width - width) / 2;
        var y = (bounds.Height - height) / 2;
        context.DrawImage(bitmap, new Rect(x, y, width, height));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Orbit is not { CanTurn: true } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _dragging = true;
        _last = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || Orbit is not { CanTurn: true })
            return;
        var point = e.GetPosition(this);
        var delta = point - _last;
        _last = point;
        _yaw += (float)delta.X * 0.01f;
        _pitch = Math.Clamp(_pitch - (float)delta.Y * 0.01f, FbxPreview.MinPitch, FbxPreview.MaxPitch);
        e.Handled = true;
        RequestFrame();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
            return;
        _dragging = false;
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
        Cursor = new Cursor(StandardCursorType.Hand);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Orbit is not { CanTurn: true })
            return;
        var step = Math.Clamp(e.Delta.Y, -4, 4);
        if (step == 0)
            return;
        _zoom = Math.Clamp(_zoom * MathF.Pow(1.1f, (float)step), 0.35f, 6f);
        e.Handled = true;
        RequestFrame();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = true;
        base.OnAttachedToVisualTree(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _dragging = false;
        _request++;
        DropFrame();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!_dragging)
            return;
        _dragging = false;
        Cursor = Orbit is { CanTurn: true } ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow);
    }

    private void ResetView(TappedEventArgs e)
    {
        if (Orbit is not { CanTurn: true })
            return;
        e.Handled = true;
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
        ResetAngles();
    }

    private void ResetAngles()
    {
        _request++;
        _yaw = FbxPreview.DefaultYaw;
        _pitch = FbxPreview.DefaultPitch;
        _zoom = FbxPreview.DefaultZoom;
        _dragging = false;
        DropFrame();
        Cursor = Orbit is { CanTurn: true } ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow);
        ToolTip.SetTip(this, Orbit is { CanTurn: true } ? "Drag to turn. Scroll to zoom. Double-click resets the view." : null);
        InvalidateVisual();
    }

    private bool IsHome =>
        Math.Abs(_yaw - FbxPreview.DefaultYaw) < 0.0001f
        && Math.Abs(_pitch - FbxPreview.DefaultPitch) < 0.0001f
        && Math.Abs(_zoom - FbxPreview.DefaultZoom) < 0.0001f;

    private void RequestFrame()
    {
        if (Orbit is not { CanTurn: true } orbit)
            return;
        var request = ++_request;
        if (IsHome)
        {
            DropFrame();
            InvalidateVisual();
            return;
        }

        if (_drawing)
            return;
        _drawing = true;
        var yaw = _yaw;
        var pitch = _pitch;
        var zoom = _zoom;
        _ = DrawFrame(orbit, request, yaw, pitch, zoom);
    }

    private async Task DrawFrame(FbxPreview.Orbit orbit, int request, float yaw, float pitch, float zoom)
    {
        byte[]? png = null;
        try
        {
            png = await Task.Run(() => orbit.Render(yaw, pitch, zoom)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            png = null;
        }

        await Dispatcher.UIThread.InvokeAsync(() => ApplyFrame(orbit, request, png));
    }

    private void ApplyFrame(FbxPreview.Orbit orbit, int request, byte[]? png)
    {
        _drawing = false;
        if (!_attached || png is null || !ReferenceEquals(Orbit, orbit) || IsHome)
        {
            if (_attached && ReferenceEquals(Orbit, orbit) && !IsHome && _request != request)
                RequestFrame();
            return;
        }

        Bitmap? bitmap;
        try
        {
            using var stream = new MemoryStream(png, writable: false);
            bitmap = new Bitmap(stream);
        }
        catch (Exception)
        {
            if (_request != request)
                RequestFrame();
            return;
        }

        var previous = _frame;
        _frame = bitmap;
        previous?.Dispose();
        InvalidateVisual();
        if (_request != request)
            RequestFrame();
    }

    private void DropFrame()
    {
        _frame?.Dispose();
        _frame = null;
    }
}
