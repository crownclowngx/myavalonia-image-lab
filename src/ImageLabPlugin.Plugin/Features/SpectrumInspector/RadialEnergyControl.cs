using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ImageLabPlugin.Features.SpectrumInspector;

/// <summary>绘制 256-bin 径向能量占比曲线的轻量控件。</summary>
/// <remarks>
/// 控件只消费已经归一化的只读 bin，不参与 FFT 或能量计算。纵轴按当前可见最大 bin 自适应，便于同时
/// 观察弱纹理与强 DC 图片；实际百分比仍由旁边的文字报表给出，避免图形缩放被误读为绝对能量。
/// </remarks>
public sealed class RadialEnergyControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<RadialEnergyControl, IReadOnlyList<double>?>(nameof(Values));

    static RadialEnergyControl() => AffectsRender<RadialEnergyControl>(ValuesProperty);

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(36, 128, 128, 128)), null, bounds);
        var values = Values;
        if (values is null || values.Count < 2 || bounds.Width <= 1d || bounds.Height <= 1d) return;
        var maximum = values.Where(double.IsFinite).DefaultIfEmpty().Max();
        if (maximum <= 0d) return;
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            drawing.BeginFigure(new Point(0d, bounds.Height - (values[0] / maximum * bounds.Height)), false);
            for (var i = 1; i < values.Count; i++)
            {
                var x = i / (double)(values.Count - 1) * bounds.Width;
                var y = bounds.Height - (Math.Clamp(values[i], 0d, maximum) / maximum * bounds.Height);
                drawing.LineTo(new Point(x, y));
            }
        }
        context.DrawGeometry(null, new Pen(Brushes.DeepSkyBlue, 1.5), geometry);
    }
}
