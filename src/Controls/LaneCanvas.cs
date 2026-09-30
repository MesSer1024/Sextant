using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Sextant.Git;

namespace Sextant.Controls;

public sealed class LaneCanvas : Control
{
    private const double Pitch = 10;
    private static readonly IBrush[] Palette =
    [
        Brush("#4C78A8"),
        Brush("#F58518"),
        Brush("#54A24B"),
        Brush("#E45756"),
        Brush("#72B7B2"),
        Brush("#B279A2"),
        Brush("#EECA3B"),
        Brush("#FF9DA6"),
    ];

    public static readonly StyledProperty<LaneGeometry?> GeometryProperty =
        AvaloniaProperty.Register<LaneCanvas, LaneGeometry?>(nameof(Geometry));

    static LaneCanvas()
    {
        AffectsRender<LaneCanvas>(GeometryProperty);
        AffectsMeasure<LaneCanvas>(GeometryProperty);
    }

    public LaneGeometry? Geometry
    {
        get => GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    protected override Size MeasureOverride(Size available)
    {
        var count = Math.Max(1, Geometry?.LaneCount ?? 1);
        return new Size(8 + (count * Pitch), 24);
    }

    public override void Render(DrawingContext context)
    {
        var geometry = Geometry;
        if (geometry is null)
            return;

        var mid = Bounds.Height / 2;
        foreach (var lane in geometry.ThroughLanes)
            context.DrawLine(Pen(lane), new Point(X(lane), 0), new Point(X(lane), Bounds.Height));
        foreach (var lane in geometry.IncomingLanes)
            context.DrawLine(Pen(lane), new Point(X(lane), 0), new Point(X(geometry.NodeLane), mid));
        foreach (var edge in geometry.Edges)
            context.DrawLine(Pen(edge.To), new Point(X(edge.From), mid), new Point(X(edge.To), Bounds.Height));
        context.DrawEllipse(Fill(geometry.NodeLane), null, new Point(X(geometry.NodeLane), mid), 3.5, 3.5);
    }

    private static double X(int lane) => 6 + (lane * Pitch);

    private static IPen Pen(int lane) => new Pen(Fill(lane), 1.4);

    private static IBrush Fill(int lane) => Palette[Math.Abs(lane) % Palette.Length];

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
