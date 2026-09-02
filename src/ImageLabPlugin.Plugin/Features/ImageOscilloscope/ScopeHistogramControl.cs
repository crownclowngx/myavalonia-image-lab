using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ImageLabPlugin.Domain.ImageOscilloscope;

namespace ImageLabPlugin.Features.ImageOscilloscope;

/// <summary>绘制 R/G/B/Y 直方图或颜色分布；旁边文字提供等价通道说明。</summary>
/// <remarks>控件只对不可变 bin 做显示归一化，不参与颜色换算、计数累计或探针取样。</remarks>
public sealed class ScopeHistogramControl : Control
{
    public static readonly StyledProperty<object?> AnalysisProperty =
        AvaloniaProperty.Register<ScopeHistogramControl, object?>(nameof(Analysis));
    public static readonly StyledProperty<object?> ProbeProperty =
        AvaloniaProperty.Register<ScopeHistogramControl, object?>(nameof(Probe));
    public static readonly StyledProperty<string?> ChartKindProperty =
        AvaloniaProperty.Register<ScopeHistogramControl, string?>(nameof(ChartKind));

    static ScopeHistogramControl() => AffectsRender<ScopeHistogramControl>(AnalysisProperty, ProbeProperty, ChartKindProperty);

    public object? Analysis { get => GetValue(AnalysisProperty); set => SetValue(AnalysisProperty, value); }
    public object? Probe { get => GetValue(ProbeProperty); set => SetValue(ProbeProperty, value); }
    public string? ChartKind { get => GetValue(ChartKindProperty); set => SetValue(ChartKindProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(14, 18, 24)), Bounds);
        if (Analysis is not ImageOscilloscopeAnalysis analysis) return;
        if (ChartKind == "Distribution")
        {
            DrawSeries(context, analysis.SaturationHistogram.Select(value => (double)value).ToArray(), Brushes.DeepSkyBlue);
            DrawSeries(context, analysis.ChromaHistogram.Select(value => (double)value).ToArray(), Brushes.Gold);
            DrawSeries(context, analysis.HueWeights, Brushes.MediumOrchid);
        }
        else
        {
            DrawSeries(context, analysis.RedHistogram.Select(value => (double)value).ToArray(), Brushes.IndianRed);
            DrawSeries(context, analysis.GreenHistogram.Select(value => (double)value).ToArray(), Brushes.MediumSeaGreen);
            DrawSeries(context, analysis.BlueHistogram.Select(value => (double)value).ToArray(), Brushes.CornflowerBlue);
            DrawSeries(context, analysis.LumaHistogram.Select(value => (double)value).ToArray(), Brushes.White);
        }
        DrawProbe(context);
        context.DrawRectangle(null, new Pen(Brushes.Gray, 1d), Bounds.Deflate(0.5d));
    }

    private void DrawSeries(DrawingContext context, IReadOnlyList<double> values, IBrush brush)
    {
        var maximum = values.DefaultIfEmpty().Max();
        if (maximum <= 0d || values.Count < 2) return;
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            drawing.BeginFigure(new Point(0d, Bounds.Height - (values[0] / maximum * Bounds.Height)), false);
            for (var index = 1; index < values.Count; index++)
                drawing.LineTo(new Point(index / (double)(values.Count - 1) * Bounds.Width,
                    Bounds.Height - (values[index] / maximum * Bounds.Height)));
        }
        context.DrawGeometry(null, new Pen(brush, 1.25d), geometry);
    }

    private void DrawProbe(DrawingContext context)
    {
        if (Probe is not ScopeProbe probe) return;
        if (ChartKind == "Distribution")
        {
            DrawBin(context, probe.SaturationBin, 256);
            DrawBin(context, probe.ChromaBin, 256);
            if (probe.HueBin is { } hue) DrawBin(context, hue, 360);
            return;
        }
        foreach (var bin in new[] { probe.RedHistogramBin, probe.GreenHistogramBin, probe.BlueHistogramBin, probe.LumaHistogramBin })
            DrawBin(context, bin, 256);
    }

    private void DrawBin(DrawingContext context, int bin, int binCount)
    {
        var x = (bin + 0.5d) / binCount * Bounds.Width;
        context.DrawLine(new Pen(Brushes.White, 1d), new Point(x, 0d), new Point(x, Bounds.Height));
    }
}
