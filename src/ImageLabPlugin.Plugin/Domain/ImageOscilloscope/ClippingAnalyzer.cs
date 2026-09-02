using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.ImageOscilloscope;

/// <summary>独立重扫源图，只生成阈值相关计数和最大边 1024 的保守覆盖层。</summary>
/// <remarks>
/// 每个源像素映射到唯一代理格；同一代理格内使用位或聚合，所以孤立裁切点不会因缩小而消失。
/// 位 0/1 表示亮度阴影/高光，位 2/3 表示 RGB 任一通道阴影/高光。
/// </remarks>
internal sealed class ClippingAnalyzer(OscilloscopeColorConverter converter)
{
    public const int MaximumProxyEdge = 1024;
    internal const byte LumaShadowBit = 1;
    internal const byte LumaHighlightBit = 2;
    internal const byte RgbShadowBit = 4;
    internal const byte RgbHighlightBit = 8;

    public ClippingAnalysis Analyze(PixelImage source, ClippingThresholds thresholds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (proxyWidth, proxyHeight) = CalculateProxySize(source.Size);
        var mask = new byte[checked(proxyWidth * proxyHeight)];
        ulong lumaShadow = 0, lumaHighlight = 0, rgbShadow = 0, rgbHighlight = 0;
        ulong redShadow = 0, redHighlight = 0, greenShadow = 0, greenHighlight = 0, blueShadow = 0, blueHighlight = 0;
        var rgba = source.Rgba.Span;
        for (var y = 0; y < source.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var proxyY = (int)(((long)y * proxyHeight) / source.Size.Height);
            for (var x = 0; x < source.Size.Width; x++)
            {
                var offset = checked(((y * source.Size.Width) + x) * 4);
                var pixel = converter.Convert(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
                var flags = (byte)0;
                if (pixel.Luma <= thresholds.Shadow) { lumaShadow++; flags |= LumaShadowBit; }
                if (pixel.Luma >= thresholds.Highlight) { lumaHighlight++; flags |= LumaHighlightBit; }
                var minimum = Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue));
                var maximum = Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue));
                if (minimum <= thresholds.Shadow) { rgbShadow++; flags |= RgbShadowBit; }
                if (maximum >= thresholds.Highlight) { rgbHighlight++; flags |= RgbHighlightBit; }
                if (pixel.Red <= thresholds.Shadow) redShadow++; if (pixel.Red >= thresholds.Highlight) redHighlight++;
                if (pixel.Green <= thresholds.Shadow) greenShadow++; if (pixel.Green >= thresholds.Highlight) greenHighlight++;
                if (pixel.Blue <= thresholds.Shadow) blueShadow++; if (pixel.Blue >= thresholds.Highlight) blueHighlight++;
                var proxyX = (int)(((long)x * proxyWidth) / source.Size.Width);
                mask[checked((proxyY * proxyWidth) + proxyX)] |= flags;
            }
        }

        return new ClippingAnalysis(thresholds,
            new ClippingCounts(lumaShadow, lumaHighlight, rgbShadow, rgbHighlight,
                redShadow, redHighlight, greenShadow, greenHighlight, blueShadow, blueHighlight),
            proxyWidth, proxyHeight, mask);
    }

    public PixelImage CreateOverlay(ClippingAnalysis analysis, ScopeClippingMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var rgba = new byte[checked(analysis.Width * analysis.Height * 4)];
        var mask = analysis.MaskSpan;
        for (var index = 0; index < mask.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var shadowBit = mode == ScopeClippingMode.Luma ? LumaShadowBit : RgbShadowBit;
            var highlightBit = mode == ScopeClippingMode.Luma ? LumaHighlightBit : RgbHighlightBit;
            var offset = index * 4;
            if (mode != ScopeClippingMode.Off && (mask[index] & highlightBit) != 0)
            {
                var x = index % analysis.Width; var y = index / analysis.Width;
                rgba[offset] = 255; rgba[offset + 1] = 74; rgba[offset + 2] = 44;
                rgba[offset + 3] = (byte)(((x + y) & 3) < 2 ? 185 : 90);
            }
            else if (mode != ScopeClippingMode.Off && (mask[index] & shadowBit) != 0)
            {
                var x = index % analysis.Width; var y = index / analysis.Width;
                rgba[offset] = 40; rgba[offset + 1] = 122; rgba[offset + 2] = 255;
                rgba[offset + 3] = (byte)((x % 4 == 0 || y % 4 == 0) ? 185 : 90);
            }
        }
        return new PixelImage(new ImageSize(analysis.Width, analysis.Height), rgba);
    }

    internal static (int Width, int Height) CalculateProxySize(ImageSize size)
    {
        var scale = Math.Min(1d, MaximumProxyEdge / (double)Math.Max(size.Width, size.Height));
        return (Math.Max(1, (int)Math.Round(size.Width * scale, MidpointRounding.ToEven)),
                Math.Max(1, (int)Math.Round(size.Height * scale, MidpointRounding.ToEven)));
    }
}

/// <summary>为 UI 创建白底可见色代理，不参与任何 Scope 计数。</summary>
internal sealed class ImageOscilloscopePreviewProjector(OscilloscopeColorConverter converter)
{
    public PixelImage Project(PixelImage source, CancellationToken cancellationToken)
    {
        var (width, height) = ClippingAnalyzer.CalculateProxySize(source.Size);
        var rgba = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceY = Math.Min(source.Size.Height - 1, (int)(((long)y * source.Size.Height) / height));
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(source.Size.Width - 1, (int)(((long)x * source.Size.Width) / width));
                var original = source.GetPixel(sourceX, sourceY);
                var pixel = converter.Convert(original.R, original.G, original.B, original.A);
                var offset = ((y * width) + x) * 4;
                rgba[offset] = pixel.Red; rgba[offset + 1] = pixel.Green; rgba[offset + 2] = pixel.Blue; rgba[offset + 3] = 255;
            }
        }
        return new PixelImage(new ImageSize(width, height), rgba);
    }
}
