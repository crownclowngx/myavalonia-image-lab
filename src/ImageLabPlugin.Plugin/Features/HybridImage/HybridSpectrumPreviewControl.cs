using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ImageLabPlugin.Features.HybridImage;

/// <summary>在中心化频谱上绘制低频/高频 Gaussian 理论 50% 截止环。</summary>
/// <remarks>
/// 控件只把 Application 已计算的频谱像素半径映射到 letterbox 目标矩形，不重新计算 σ、f50 或 FFT。
/// 低频用实线黄环，高频用虚线青环；二者都是连续公式参考，不冒充离散 3σ 核的精确测量。
/// </remarks>
internal sealed class HybridSpectrumPreviewControl : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<HybridSpectrumPreviewControl, Bitmap?>(nameof(Source));
    public static readonly StyledProperty<double> LowRadiusProperty =
        AvaloniaProperty.Register<HybridSpectrumPreviewControl, double>(nameof(LowRadius));
    public static readonly StyledProperty<double> HighRadiusProperty =
        AvaloniaProperty.Register<HybridSpectrumPreviewControl, double>(nameof(HighRadius));

    static HybridSpectrumPreviewControl()
    {
        AffectsRender<HybridSpectrumPreviewControl>(SourceProperty, LowRadiusProperty, HighRadiusProperty);
    }

    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public double LowRadius { get => GetValue(LowRadiusProperty); set => SetValue(LowRadiusProperty, value); }
    public double HighRadius { get => GetValue(HighRadiusProperty); set => SetValue(HighRadiusProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Black, Bounds);
        if (Source is null) return;
        var scale = Math.Min(Bounds.Width / Source.PixelSize.Width, Bounds.Height / Source.PixelSize.Height);
        var destination = new Rect((Bounds.Width - (Source.PixelSize.Width * scale)) / 2d,
            (Bounds.Height - (Source.PixelSize.Height * scale)) / 2d,
            Source.PixelSize.Width * scale, Source.PixelSize.Height * scale);
        context.DrawImage(Source, new Rect(Source.Size), destination);
        var center = destination.Center;
        DrawRing(context, center, destination, LowRadius, new Pen(Brushes.Gold, 1.5d));
        DrawRing(context, center, destination, HighRadius,
            new Pen(Brushes.Cyan, 1.5d, dashStyle: DashStyle.Dash));
    }

    private void DrawRing(DrawingContext context, Point center, Rect destination, double radius, Pen pen)
    {
        if (Source is null || !double.IsFinite(radius) || radius <= 0d) return;
        var radiusX = radius * destination.Width / Source.PixelSize.Width;
        var radiusY = radius * destination.Height / Source.PixelSize.Height;
        context.DrawEllipse(null, pen, center, radiusX, radiusY);
    }
}
