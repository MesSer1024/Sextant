using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

namespace Sextant.Controls;

/// <summary>
/// A short arc that turns while it is on screen. The timer stops when a parent hides it.
/// </summary>
public sealed class LoadingSpinner : Panel
{
    private readonly Arc _arc;
    private readonly RotateTransform _rotate = new();
    private readonly DispatcherTimer _timer;
    private bool _spinning;

    public LoadingSpinner()
    {
        _arc = new Arc
        {
            StartAngle = 0,
            SweepAngle = 280,
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        };
        Children.Add(_arc);
        RenderTransform = _rotate;
        RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => _rotate.Angle = (_rotate.Angle + 7) % 360;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Paint();
        Sync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
            Sync();
        else if (change.Property.Name == nameof(ActualThemeVariant))
            Paint();
    }

    private void Paint()
    {
        if (TryGetResource("SystemControlForegroundBaseHighBrush", ActualThemeVariant, out var value) && value is IBrush brush)
            _arc.Stroke = brush;
        else
            _arc.Stroke = Brushes.Gray;
    }

    private void Sync()
    {
        if (IsVisible)
            Start();
        else
            Stop();
    }

    private void Start()
    {
        if (_spinning)
            return;
        _spinning = true;
        _timer.Start();
    }

    private void Stop()
    {
        _spinning = false;
        _timer.Stop();
    }
}
