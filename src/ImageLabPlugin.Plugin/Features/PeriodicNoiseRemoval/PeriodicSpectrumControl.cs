using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ImageLabPlugin.Features.FrequencyMaskEditor;

namespace ImageLabPlugin.Features.PeriodicNoiseRemoval;

internal sealed record PeriodicSpectrumMarker(int Number, double X, double Y, double ConjugateX, double ConjugateY,
    bool Selected, bool HighRisk);

internal sealed class PeriodicSpectrumSelectionEventArgs(double x, double y) : EventArgs
{
    public double X { get; } = x;
    public double Y { get; } = y;
}

/// <summary>绘制中心化频谱、候选/共轭覆盖，并只提交归一化单击意图。</summary>
/// <remarks>
/// 控件只拥有 letterbox、DPI、命中测试和可访问的图形编码：实心圆表示 canonical、空心圆表示共轭、叉号表示高风险。
/// 它不知道 FFT 自然索引、配方、共轭公式或检测分数，手动单击必须由 Application 映射后才能改变草案。
/// </remarks>
internal sealed class PeriodicSpectrumControl : Control
{
    public static readonly StyledProperty<Bitmap?> SpectrumProperty =
        AvaloniaProperty.Register<PeriodicSpectrumControl, Bitmap?>(nameof(Spectrum));
    public static readonly StyledProperty<IReadOnlyList<PeriodicSpectrumMarker>> MarkersProperty =
        AvaloniaProperty.Register<PeriodicSpectrumControl, IReadOnlyList<PeriodicSpectrumMarker>>(
            nameof(Markers), Array.Empty<PeriodicSpectrumMarker>());

    static PeriodicSpectrumControl()
    {
        AffectsRender<PeriodicSpectrumControl>(SpectrumProperty, MarkersProperty);
        FocusableProperty.OverrideDefaultValue<PeriodicSpectrumControl>(true);
    }

    public Bitmap? Spectrum { get => GetValue(SpectrumProperty); set => SetValue(SpectrumProperty, value); }
    public IReadOnlyList<PeriodicSpectrumMarker> Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }
    public event EventHandler<PeriodicSpectrumSelectionEventArgs>? FrequencySelected;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Black, Bounds);
        if (Spectrum is null || !FrequencyCanvasCoordinateMapper.TryGetImageRect(Bounds.Size, Spectrum.PixelSize,
                out var destination)) return;
        context.DrawImage(Spectrum, new Rect(Spectrum.Size), destination);
        context.DrawRectangle(new Pen(Brushes.Gray, 1d), destination);
        foreach (var marker in Markers)
        {
            DrawMarker(context, destination, marker.X, marker.Y, marker, conjugate: false);
            if (Math.Abs(marker.X - marker.ConjugateX) > 1e-9 || Math.Abs(marker.Y - marker.ConjugateY) > 1e-9)
                DrawMarker(context, destination, marker.ConjugateX, marker.ConjugateY, marker, conjugate: true);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Spectrum is null) return;
        if (!FrequencyCanvasCoordinateMapper.TryMap(Bounds.Size, Spectrum.PixelSize, e.GetPosition(this),
                out var x, out var y)) return;
        Focus();
        FrequencySelected?.Invoke(this, new PeriodicSpectrumSelectionEventArgs(x, y));
        e.Handled = true;
    }

    private static void DrawMarker(DrawingContext context, Rect destination, double x, double y,
        PeriodicSpectrumMarker marker, bool conjugate)
    {
        var center = new Point(destination.X + (x * destination.Width), destination.Y + (y * destination.Height));
        var brush = marker.Selected ? Brushes.Lime : marker.HighRisk ? Brushes.OrangeRed : Brushes.DeepSkyBlue;
        var pen = new Pen(brush, marker.Selected ? 2.5d : 1.5d);
        context.DrawEllipse(conjugate ? null : brush, pen, center, 5d, 5d);
        if (marker.HighRisk)
        {
            context.DrawLine(pen, new Point(center.X - 6d, center.Y - 6d), new Point(center.X + 6d, center.Y + 6d));
            context.DrawLine(pen, new Point(center.X - 6d, center.Y + 6d), new Point(center.X + 6d, center.Y - 6d));
        }
        var text = new FormattedText(marker.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            Typeface.Default, 10d, Brushes.White);
        context.DrawText(text, new Point(center.X + 7d, center.Y - 12d));
    }
}
