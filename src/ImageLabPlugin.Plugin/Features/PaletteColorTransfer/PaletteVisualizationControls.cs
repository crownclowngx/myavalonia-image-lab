using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ImageLabPlugin.Domain.ColorTransfer;

namespace ImageLabPlugin.Features.PaletteColorTransfer;

/// <summary>按调色板显示顺序绘制色块；身份与数值仍由旁边等价表格表达。</summary>
internal sealed class PaletteStripControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<PaletteEntry>> EntriesProperty =
        AvaloniaProperty.Register<PaletteStripControl, IReadOnlyList<PaletteEntry>>(nameof(Entries), Array.Empty<PaletteEntry>());
    static PaletteStripControl() => AffectsRender<PaletteStripControl>(EntriesProperty);
    public IReadOnlyList<PaletteEntry> Entries { get => GetValue(EntriesProperty); set => SetValue(EntriesProperty, value); }
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Brushes.Transparent, Bounds); if (Entries.Count == 0) return;
        var width = Bounds.Width / Entries.Count;
        for (var i = 0; i < Entries.Count; i++)
        {
            var bytes = Entries[i].Srgb.ToBytes(); var brush = new SolidColorBrush(Color.FromRgb(bytes.Red, bytes.Green, bytes.Blue));
            context.FillRectangle(brush, new Rect(i * width, 0, width, Bounds.Height));
            context.DrawRectangle(new Pen(Brushes.Gray, 1), new Rect(i * width, 0, width, Bounds.Height));
        }
    }
}

/// <summary>绘制一个已冻结量纲的 double 直方图；显示缩放不修改领域 bin。</summary>
internal sealed class ColorHistogramControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>> BinsProperty =
        AvaloniaProperty.Register<ColorHistogramControl, IReadOnlyList<double>>(nameof(Bins), Array.Empty<double>());
    public static readonly StyledProperty<bool> UseLogarithmicScaleProperty =
        AvaloniaProperty.Register<ColorHistogramControl, bool>(nameof(UseLogarithmicScale));
    static ColorHistogramControl() => AffectsRender<ColorHistogramControl>(BinsProperty, UseLogarithmicScaleProperty);
    public IReadOnlyList<double> Bins { get => GetValue(BinsProperty); set => SetValue(BinsProperty, value); }
    public bool UseLogarithmicScale { get => GetValue(UseLogarithmicScaleProperty); set => SetValue(UseLogarithmicScaleProperty, value); }
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Brushes.Transparent, Bounds); if (Bins.Count < 2 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var maximum = Bins.Max(Transform); if (maximum <= 0) return; Point? previous = null;
        for (var i = 0; i < Bins.Count; i++)
        { var point = new Point(i * Bounds.Width / (Bins.Count - 1d), Bounds.Height - (Transform(Bins[i]) * Bounds.Height / maximum)); if (previous is { } p) context.DrawLine(new Pen(Brushes.DodgerBlue, 1.5), p, point); previous = point; }
    }
    private double Transform(double value) => UseLogarithmicScale ? Math.Log10(value + 1d) : value;
}

/// <summary>把固定 128×128 a*-b* 权重网格投影为有界密度；空网格不会创建逐像素对象。</summary>
internal sealed class ColorDistributionPlaneControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>> DensityProperty =
        AvaloniaProperty.Register<ColorDistributionPlaneControl, IReadOnlyList<double>>(nameof(Density), Array.Empty<double>());
    static ColorDistributionPlaneControl() => AffectsRender<ColorDistributionPlaneControl>(DensityProperty);
    public IReadOnlyList<double> Density { get => GetValue(DensityProperty); set => SetValue(DensityProperty, value); }
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Brushes.Black, Bounds); if (Density.Count != 128 * 128) return;
        var maximum = Density.Max(); if (maximum <= 0) return; var cellWidth = Bounds.Width / 128d; var cellHeight = Bounds.Height / 128d;
        for (var y = 0; y < 128; y++) for (var x = 0; x < 128; x++)
        {
            var value = Density[(y * 128) + x]; if (value <= 0) continue;
            var intensity = (byte)Math.Clamp(Math.Round(255d * Math.Sqrt(value / maximum)), 0d, 255d);
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(intensity, (byte)(intensity / 2), 255)),
                new Rect(x * cellWidth, (127 - y) * cellHeight, Math.Ceiling(cellWidth), Math.Ceiling(cellHeight)));
        }
    }
}

/// <summary>ΔE00 固定 100-bin 图；封顶 bin 只影响显示，真实最大值由数值摘要保留。</summary>
internal sealed class PerceptualDifferenceControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>> BinsProperty =
        AvaloniaProperty.Register<PerceptualDifferenceControl, IReadOnlyList<double>>(nameof(Bins), Array.Empty<double>());
    static PerceptualDifferenceControl() => AffectsRender<PerceptualDifferenceControl>(BinsProperty);
    public IReadOnlyList<double> Bins { get => GetValue(BinsProperty); set => SetValue(BinsProperty, value); }
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Brushes.Transparent, Bounds); if (Bins.Count != 100) return;
        var maximum = Bins.Max(); if (maximum <= 0) return; var width = Bounds.Width / 100d;
        for (var i = 0; i < 100; i++)
        { var height = Bins[i] * Bounds.Height / maximum; context.FillRectangle(Brushes.OrangeRed, new Rect(i * width, Bounds.Height - height, Math.Max(1, width), height)); }
    }
}
