using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ImageLabPlugin.Domain.Robustness;

namespace ImageLabPlugin.Features.RobustnessLab;

/// <summary>轻量成功率曲线；仅绘制已聚合值，不拥有实验状态或重新计算结果。</summary>
internal sealed class RobustnessCurveControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<RobustnessCurvePoint>> PointsProperty = AvaloniaProperty.Register<RobustnessCurveControl, IReadOnlyList<RobustnessCurvePoint>>(nameof(Points), []);
    public IReadOnlyList<RobustnessCurvePoint> Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public RobustnessCurveControl() { Focusable = true; AffectsRender<RobustnessCurveControl>(PointsProperty); }
    public override void Render(DrawingContext context)
    {
        base.Render(context); var bounds = Bounds.Deflate(12); var axis = new Pen(Brushes.Gray, 1); context.DrawLine(axis, bounds.BottomLeft, bounds.TopLeft); context.DrawLine(axis, bounds.BottomLeft, bounds.BottomRight);
        var groups = Points.Where(p => p.SuccessRate.HasValue).GroupBy(p => p.Profile).ToArray(); var colors = new[] { Brushes.DodgerBlue, Brushes.SeaGreen, Brushes.OrangeRed };
        foreach (var (group, groupIndex) in groups.Select((value, index) => (value, index)))
        {
            var values = group.OrderBy(p => p.ScanValue).ToArray(); if (values.Length == 0) continue; var min = values.Min(p => p.ScanValue); var max = values.Max(p => p.ScanValue); Point? previous = null;
            foreach (var value in values)
            {
                var x = max == min ? bounds.Center.X : bounds.Left + ((double)((value.ScanValue - min) / (max - min)) * bounds.Width); var y = bounds.Bottom - (value.SuccessRate!.Value * bounds.Height); var point = new Point(x, y);
                context.DrawEllipse(colors[groupIndex % colors.Length], null, point, 3, 3); if (previous is { } old) context.DrawLine(new Pen(colors[groupIndex % colors.Length], 2), old, point); previous = point;
            }
        }
    }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.Key is Key.Left or Key.Right) { e.Handled = true; InvalidateVisual(); } base.OnKeyDown(e); }
}
