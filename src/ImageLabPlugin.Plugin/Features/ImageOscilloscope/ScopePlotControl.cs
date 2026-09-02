using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ImageLabPlugin.Domain.ImageOscilloscope;

namespace ImageLabPlugin.Features.ImageOscilloscope;

/// <summary>绘制已着色的 Scope 代理、刻度网格和当前源像素标记。</summary>
/// <remarks>
/// 控件只消费 Bitmap 与强类型探针，不读取源图、不计算颜色公式、不重建密度。Waveform/Parade/Vector
/// 的坐标输入均已由领域 Mapper 冻结；这里仅按当前控件尺寸换算为屏幕坐标。
/// </remarks>
public sealed class ScopePlotControl : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<ScopePlotControl, Bitmap?>(nameof(Source));
    public static readonly StyledProperty<string?> PlotKindProperty =
        AvaloniaProperty.Register<ScopePlotControl, string?>(nameof(PlotKind));
    public static readonly StyledProperty<object?> ProbeProperty =
        AvaloniaProperty.Register<ScopePlotControl, object?>(nameof(Probe));
    public static readonly StyledProperty<object?> ReferenceTargetsProperty =
        AvaloniaProperty.Register<ScopePlotControl, object?>(nameof(ReferenceTargets));

    static ScopePlotControl() => AffectsRender<ScopePlotControl>(SourceProperty, PlotKindProperty, ProbeProperty, ReferenceTargetsProperty);

    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public string? PlotKind { get => GetValue(PlotKindProperty); set => SetValue(PlotKindProperty, value); }
    public object? Probe { get => GetValue(ProbeProperty); set => SetValue(ProbeProperty, value); }
    public object? ReferenceTargets { get => GetValue(ReferenceTargetsProperty); set => SetValue(ReferenceTargetsProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Black, Bounds);
        if (Source is not null) context.DrawImage(Source, new Rect(Source.Size), Bounds);
        DrawGrid(context);
        if (PlotKind == "Vectorscope") DrawVectorscopeReferences(context);
        if (Probe is ScopeProbe probe) DrawProbe(context, probe);
        context.DrawRectangle(null, new Pen(Brushes.Gray, 1d), Bounds.Deflate(0.5d));
    }

    private void DrawGrid(DrawingContext context)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(80, 190, 190, 190)), 1d);
        for (var index = 1; index < 4; index++)
        {
            var x = Bounds.Width * index / 4d;
            var y = Bounds.Height * index / 4d;
            context.DrawLine(pen, new Point(x, 0d), new Point(x, Bounds.Height));
            context.DrawLine(pen, new Point(0d, y), new Point(Bounds.Width, y));
        }
    }

    private void DrawVectorscopeReferences(DrawingContext context)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(150, 230, 230, 230)), 1d);
        var center = new Point(Bounds.Width / 2d, Bounds.Height / 2d);
        context.DrawLine(pen, new Point(center.X, 0d), new Point(center.X, Bounds.Height));
        context.DrawLine(pen, new Point(0d, center.Y), new Point(Bounds.Width, center.Y));
        if (ReferenceTargets is not IReadOnlyList<ScopeReferenceTarget> targets) return;
        foreach (var target in targets)
        {
            var point = new Point((target.Point.X + 0.5d) / 512d * Bounds.Width,
                (target.Point.Y + 0.5d) / 512d * Bounds.Height);
            context.DrawEllipse(null, pen, point, 5d, 5d);
        }
    }

    private void DrawProbe(DrawingContext context, ScopeProbe probe)
    {
        var pen = new Pen(Brushes.White, 2d);
        if (PlotKind == "Waveform") DrawMarker(context, probe.Waveform, 1, 256, pen);
        else if (PlotKind == "Vectorscope") DrawMarker(context, probe.Vectorscope, 512, 512, pen);
        else if (PlotKind == "Parade" && Source is not null)
        {
            var segmentWidth = (Source.PixelSize.Width - 8) / 3d;
            DrawParadeMarker(context, probe.RedParade, 0d, segmentWidth, pen);
            DrawParadeMarker(context, probe.GreenParade, segmentWidth + 4d, segmentWidth, pen);
            DrawParadeMarker(context, probe.BlueParade, (segmentWidth + 4d) * 2d, segmentWidth, pen);
        }
    }

    private void DrawMarker(DrawingContext context, ScopePoint point, int width, int height, Pen pen)
    {
        var sourceWidth = Source?.PixelSize.Width ?? width;
        var x = (point.X + 0.5d) / sourceWidth * Bounds.Width;
        var y = (point.Y + 0.5d) / height * Bounds.Height;
        context.DrawEllipse(null, pen, new Point(x, y), 5d, 5d);
    }

    private void DrawParadeMarker(DrawingContext context, ScopePoint point, double sourceLeft, double segmentWidth, Pen pen)
    {
        if (Source is null) return;
        var x = (sourceLeft + ((point.X + 0.5d) / Math.Max(1d, segmentWidth) * segmentWidth)) / Source.PixelSize.Width * Bounds.Width;
        var y = (point.Y + 0.5d) / 256d * Bounds.Height;
        context.DrawEllipse(null, pen, new Point(x, y), 4d, 4d);
    }
}
