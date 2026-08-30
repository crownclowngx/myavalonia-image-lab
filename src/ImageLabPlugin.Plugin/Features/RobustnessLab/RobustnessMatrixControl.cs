using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ImageLabPlugin.Domain.Robustness;

namespace ImageLabPlugin.Features.RobustnessLab;

/// <summary>Profile×扫描点矩阵。颜色只作辅助，下方表格始终提供相同数值和 n。</summary>
internal sealed class RobustnessMatrixControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<RobustnessCurvePoint>> PointsProperty = AvaloniaProperty.Register<RobustnessMatrixControl, IReadOnlyList<RobustnessCurvePoint>>(nameof(Points), []);
    public IReadOnlyList<RobustnessCurvePoint> Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    private int _selected;
    public RobustnessMatrixControl() { Focusable = true; AffectsRender<RobustnessMatrixControl>(PointsProperty); }
    public override void Render(DrawingContext context)
    {
        base.Render(context); if (Points.Count == 0) return; var profiles = Points.Select(p => p.Profile).Distinct().Order().ToArray(); var scans = Points.Select(p => p.ScanValue).Distinct().Order().ToArray();
        var width = Bounds.Width / Math.Max(1, profiles.Length); var height = Bounds.Height / Math.Max(1, scans.Length);
        for (var row = 0; row < scans.Length; row++) for (var column = 0; column < profiles.Length; column++)
        {
            var item = Points.FirstOrDefault(p => p.Profile == profiles[column] && p.ScanValue == scans[row]); var rate = item?.SuccessRate; IBrush brush = rate is null ? Brushes.Gray : new SolidColorBrush(Color.FromRgb((byte)(220 * (1d - rate.Value)), (byte)(80 + 150 * rate.Value), 70));
            var rect = new Rect(column * width + 1, row * height + 1, Math.Max(0, width - 2), Math.Max(0, height - 2)); context.FillRectangle(brush, rect);
            if ((row * profiles.Length) + column == _selected) context.DrawRectangle(null, new Pen(Brushes.White, 2), rect);
        }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var columns = Math.Max(1, Points.Select(p => p.Profile).Distinct().Count()); var count = Math.Max(1, Points.Count);
        _selected = e.Key switch { Key.Left => Math.Max(0, _selected - 1), Key.Right => Math.Min(count - 1, _selected + 1), Key.Up => Math.Max(0, _selected - columns), Key.Down => Math.Min(count - 1, _selected + columns), _ => _selected };
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) { e.Handled = true; InvalidateVisual(); } base.OnKeyDown(e);
    }
}
