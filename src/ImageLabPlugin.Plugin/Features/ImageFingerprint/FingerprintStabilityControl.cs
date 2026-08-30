using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ImageLabPlugin.Application.Fingerprinting;

namespace ImageLabPlugin.Features.ImageFingerprint;

/// <summary>只绘制当前算法的稳定性距离曲线；它不运行试验，也不持有图片或 Session。</summary>
internal sealed class FingerprintStabilityControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<FingerprintStabilityPoint>> PointsProperty =
        AvaloniaProperty.Register<FingerprintStabilityControl, IReadOnlyList<FingerprintStabilityPoint>>(nameof(Points), []);
    public static readonly StyledProperty<string?> AlgorithmIdProperty =
        AvaloniaProperty.Register<FingerprintStabilityControl, string?>(nameof(AlgorithmId));

    static FingerprintStabilityControl() => AffectsRender<FingerprintStabilityControl>(PointsProperty, AlgorithmIdProperty);
    public FingerprintStabilityControl() => Focusable = true;
    public IReadOnlyList<FingerprintStabilityPoint> Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public string? AlgorithmId { get => GetValue(AlgorithmIdProperty); set => SetValue(AlgorithmIdProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds.Deflate(14d);
        context.DrawLine(new Pen(Brushes.Gray), bounds.BottomLeft, bounds.TopLeft);
        context.DrawLine(new Pen(Brushes.Gray), bounds.BottomLeft, bounds.BottomRight);
        var values = Points.Select(point => (Point: point, Algorithm: point.Algorithms.FirstOrDefault(value => value.AlgorithmId.Value == AlgorithmId)))
            .Where(value => value.Algorithm is not null).ToArray();
        if (values.Length == 0) return;
        var min = values.Min(value => value.Point.RequestedValue);
        var max = values.Max(value => value.Point.RequestedValue);
        Point? previous = null;
        foreach (var value in values)
        {
            var x = min == max ? bounds.Center.X : bounds.Left + ((double)((value.Point.RequestedValue - min) / (max - min)) * bounds.Width);
            var y = bounds.Bottom - ((value.Algorithm!.Distance.Distance / 64d) * bounds.Height);
            var current = new Point(x, y);
            context.DrawEllipse(Brushes.DodgerBlue, null, current, 3, 3);
            if (previous is { } old) context.DrawLine(new Pen(Brushes.DodgerBlue, 2), old, current);
            previous = current;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right) { InvalidateVisual(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
