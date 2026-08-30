using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Comparison;

/// <summary>把无倍率基础差异场着色为 RGB 绝对差异图。</summary>
internal sealed class ImageDifferenceProxyProjector
{
    public DifferenceProjectionResult Project(ImageDifferenceProxy source, int amplification, CancellationToken cancellationToken = default)
    {
        ValidateAmplification(amplification);
        var count = checked((int)source.Size.PixelCount);
        var rgba = new byte[count * 4]; var saturated = 0;
        var red = source.Red.Span; var green = source.Green.Span; var blue = source.Blue.Span;
        for (var i = 0; i < count; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var r = red[i] * amplification; var g = green[i] * amplification; var b = blue[i] * amplification;
            if (r > 255 || g > 255 || b > 255) saturated++;
            var offset = i * 4;
            rgba[offset] = (byte)Math.Min(255, r); rgba[offset + 1] = (byte)Math.Min(255, g);
            rgba[offset + 2] = (byte)Math.Min(255, b); rgba[offset + 3] = 255;
        }
        var options = new DifferenceProjectionOptions(DifferenceProjectionKind.Rgb, amplification);
        return new DifferenceProjectionResult(new PixelImage(source.Size, rgba), saturated, options);
    }

    internal static void ValidateAmplification(int value)
    {
        if (value is not (1 or 2 or 4 or 8 or 16 or 32))
            throw new ArgumentOutOfRangeException(nameof(value), value, "差异倍率只能是 1、2、4、8、16 或 32。 ");
    }
}

/// <summary>固定量纲的 256 色伪彩色投影，不根据单张图片的极值自动归一化。</summary>
/// <remarks>
/// 色表采用五个显式锚点的线性插值：深蓝→青→绿→黄→白。固定映射确保相同“原始差异×倍率”在不同会话中
/// 始终得到相同颜色；UI 还需同时显示数值图例，不能仅靠颜色传意。
/// </remarks>
internal sealed class DifferenceHeatmapProjector
{
    private static readonly RgbaPixel[] Palette = CreatePalette();
    internal static IReadOnlyList<RgbaPixel> ColorTable => Palette;

    public DifferenceProjectionResult Project(
        ImageDifferenceProxy source,
        HeatmapScalarSource scalarSource,
        int amplification,
        CancellationToken cancellationToken = default)
    {
        ImageDifferenceProxyProjector.ValidateAmplification(amplification);
        var values = scalarSource == HeatmapScalarSource.MaximumRgb ? source.MaximumRgb.Span : source.Luma.Span;
        var rgba = new byte[checked(values.Length * 4)]; var saturated = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var raw = values[i] * amplification;
            if (raw > 255) saturated++;
            var color = Palette[Math.Min(255, raw)]; var offset = i * 4;
            rgba[offset] = color.R; rgba[offset + 1] = color.G; rgba[offset + 2] = color.B; rgba[offset + 3] = 255;
        }
        var options = new DifferenceProjectionOptions(DifferenceProjectionKind.Heatmap, amplification, scalarSource);
        return new DifferenceProjectionResult(new PixelImage(source.Size, rgba), saturated, options);
    }

    private static RgbaPixel[] CreatePalette()
    {
        var anchors = new[]
        {
            (Index: 0, Color: new RgbaPixel(0, 0, 32, 255)),
            (Index: 64, Color: new RgbaPixel(0, 96, 192, 255)),
            (Index: 128, Color: new RgbaPixel(0, 192, 112, 255)),
            (Index: 192, Color: new RgbaPixel(240, 208, 32, 255)),
            (Index: 255, Color: new RgbaPixel(255, 255, 255, 255))
        };
        var result = new RgbaPixel[256];
        for (var segment = 0; segment < anchors.Length - 1; segment++)
        {
            var start = anchors[segment]; var end = anchors[segment + 1];
            for (var i = start.Index; i <= end.Index; i++)
            {
                var t = (i - start.Index) / (double)(end.Index - start.Index);
                result[i] = new RgbaPixel(Lerp(start.Color.R, end.Color.R, t), Lerp(start.Color.G, end.Color.G, t), Lerp(start.Color.B, end.Color.B, t), 255);
            }
        }
        return result;
    }

    private static byte Lerp(byte start, byte end, double t) => (byte)Math.Round(start + ((end - start) * t));
}
