using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ImageLabPlugin.Features.WaveletLab;

/// <summary>按案例执行顺序绘制 0–1 保留系数比例曲线；完整数值始终由旁边表格等价信息提供。</summary>
/// <remarks>控件不排序、不选择“最佳点”，也不读取质量结论；无参考图时仍只是稀疏度趋势。</remarks>
public sealed class WaveletScanChartControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<WaveletScanChartControl, IReadOnlyList<double>?>(nameof(Values));

    static WaveletScanChartControl() => AffectsRender<WaveletScanChartControl>(ValuesProperty);
    public IReadOnlyList<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        const double padding = 10d;
        var area = new Rect(padding, padding, Math.Max(0d, Bounds.Width - 2d * padding), Math.Max(0d, Bounds.Height - 2d * padding));
        var axis = new Pen(Brushes.Gray, 1d); context.DrawLine(axis, area.BottomLeft, area.BottomRight); context.DrawLine(axis, area.TopLeft, area.BottomLeft);
        if (Values is null || Values.Count == 0 || area.Width <= 0d || area.Height <= 0d) return;
        var line = new Pen(Brushes.DodgerBlue, 2d);
        Point Map(int index) => new(area.X + (Values.Count == 1 ? area.Width / 2d : area.Width * index / (Values.Count - 1d)),
            area.Bottom - area.Height * Math.Clamp(Values[index], 0d, 1d));
        var previous = Map(0);
        for (var index = 1; index < Values.Count; index++) { var current = Map(index); context.DrawLine(line, previous, current); previous = current; }
    }
}
