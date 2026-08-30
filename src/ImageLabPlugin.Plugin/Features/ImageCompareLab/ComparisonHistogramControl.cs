using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ImageLabPlugin.Features.ImageCompareLab;

/// <summary>绘制已计算的双 256-bin 直方图；切换线性/log 只影响纵轴变换，不修改领域计数。</summary>
public sealed class ComparisonHistogramControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<long>?> ReferenceBinsProperty =
        AvaloniaProperty.Register<ComparisonHistogramControl, IReadOnlyList<long>?>(nameof(ReferenceBins));
    public static readonly StyledProperty<IReadOnlyList<long>?> CandidateBinsProperty =
        AvaloniaProperty.Register<ComparisonHistogramControl, IReadOnlyList<long>?>(nameof(CandidateBins));
    public static readonly StyledProperty<bool> UseLogarithmicScaleProperty =
        AvaloniaProperty.Register<ComparisonHistogramControl, bool>(nameof(UseLogarithmicScale));

    static ComparisonHistogramControl() =>
        AffectsRender<ComparisonHistogramControl>(ReferenceBinsProperty, CandidateBinsProperty, UseLogarithmicScaleProperty);

    public IReadOnlyList<long>? ReferenceBins { get => GetValue(ReferenceBinsProperty); set => SetValue(ReferenceBinsProperty, value); }
    public IReadOnlyList<long>? CandidateBins { get => GetValue(CandidateBinsProperty); set => SetValue(CandidateBinsProperty, value); }
    public bool UseLogarithmicScale { get => GetValue(UseLogarithmicScaleProperty); set => SetValue(UseLogarithmicScaleProperty, value); }

    public ComparisonHistogramControl()
    {
        PointerMoved += (_, args) =>
        {
            var bin = MapBin(args.GetPosition(this).X, Bounds.Width);
            if (ReferenceBins is { Count: 256 } reference && CandidateBins is { Count: 256 } candidate)
            {
                ToolTip.SetTip(this, $"bin {bin}；参考 {reference[bin]:N0}；待比较 {candidate[bin]:N0}；差值 {candidate[bin] - reference[bin]:+#;-#;0}");
                ToolTip.SetIsOpen(this, true);
            }
        };
        PointerExited += (_, _) => ToolTip.SetIsOpen(this, false);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Brushes.Transparent, Bounds);
        DrawSeries(context, ReferenceBins, new Pen(Brushes.DodgerBlue, 1.5));
        DrawSeries(context, CandidateBins, new Pen(Brushes.Orange, 1.5));
    }

    private void DrawSeries(DrawingContext context, IReadOnlyList<long>? bins, Pen pen)
    {
        if (bins is null || bins.Count != 256 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var maximum = bins.Max(); if (maximum <= 0) return;
        var maxValue = Transform(maximum);
        Point? previous = null;
        for (var i = 0; i < 256; i++)
        {
            var point = new Point((i / 255d) * Bounds.Width, Bounds.Height - ((Transform(bins[i]) / maxValue) * Bounds.Height));
            if (previous is { } value) context.DrawLine(pen, value, point);
            previous = point;
        }
    }

    private double Transform(long count) => UseLogarithmicScale ? Math.Log10(count + 1d) : count;
    internal static int MapBin(double x, double width) => width <= 0d ? 0 : Math.Clamp((int)Math.Round((x / width) * 255d), 0, 255);
}
