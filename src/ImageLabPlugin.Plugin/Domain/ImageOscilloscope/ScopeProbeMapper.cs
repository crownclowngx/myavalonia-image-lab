using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.ImageOscilloscope;

/// <summary>把源像素映射到所有 Scope/bin；不处理 Pointer、letterbox 或 Document 状态。</summary>
internal sealed class ScopeProbeMapper(OscilloscopeColorConverter converter)
{
    private static readonly double MaximumChromaRadius = Math.Sqrt(0.5d);

    public ScopeProbe Map(PixelImage source, int sourceX, int sourceY, int waveformWidth)
    {
        ArgumentNullException.ThrowIfNull(source);
        var original = source.GetPixel(sourceX, sourceY);
        var pixel = converter.Convert(original.R, original.G, original.B, original.A);
        var scopeX = ImageOscilloscopeAnalyzer.MapHorizontal(sourceX, source.Size.Width, waveformWidth);
        var vector = ImageOscilloscopeAnalyzer.MapVectorscope(pixel.Cb, pixel.Cr);
        return new ScopeProbe(sourceX, sourceY, pixel,
            new ScopePoint(scopeX, 255 - pixel.Luma),
            new ScopePoint(scopeX, 255 - pixel.Red),
            new ScopePoint(scopeX, 255 - pixel.Green),
            new ScopePoint(scopeX, 255 - pixel.Blue), vector,
            pixel.Red, pixel.Green, pixel.Blue, pixel.Luma,
            ImageOscilloscopeAnalyzer.QuantizeUnit(pixel.Saturation),
            pixel.Hue is { } hue ? Math.Min(359, (int)Math.Floor(hue)) : null,
            ImageOscilloscopeAnalyzer.QuantizeUnit(pixel.ChromaRadius / MaximumChromaRadius));
    }
}

/// <summary>把 contain 布局中的显示坐标换算为源像素；边界外明确返回无命中。</summary>
/// <remarks>
/// 先求等比显示矩形并扣除 letterbox，再按半开区间映射到源像素。右/下边界属于矩形外，
/// 可避免把空白区域夹到最后像素；合法区域内的最后一个像素中心仍稳定落到末尾索引。
/// </remarks>
internal sealed class ImageProbeCoordinateMapper
{
    public ImageProbeMapping Map(double pointerX, double pointerY, double controlWidth, double controlHeight,
        int sourceWidth, int sourceHeight)
    {
        if (!double.IsFinite(pointerX) || !double.IsFinite(pointerY) || controlWidth <= 0d || controlHeight <= 0d ||
            sourceWidth <= 0 || sourceHeight <= 0) return new ImageProbeMapping(false, 0, 0, 0d, 0d);
        var scale = Math.Min(controlWidth / sourceWidth, controlHeight / sourceHeight);
        var displayWidth = sourceWidth * scale;
        var displayHeight = sourceHeight * scale;
        var left = (controlWidth - displayWidth) / 2d;
        var top = (controlHeight - displayHeight) / 2d;
        if (pointerX < left || pointerY < top || pointerX >= left + displayWidth || pointerY >= top + displayHeight)
            return new ImageProbeMapping(false, 0, 0, 0d, 0d);
        var normalizedX = (pointerX - left) / displayWidth;
        var normalizedY = (pointerY - top) / displayHeight;
        return new ImageProbeMapping(true,
            Math.Min(sourceWidth - 1, (int)Math.Floor(normalizedX * sourceWidth)),
            Math.Min(sourceHeight - 1, (int)Math.Floor(normalizedY * sourceHeight)),
            normalizedX, normalizedY);
    }
}

/// <summary>用与像素探针完全相同的颜色公式生成六个 Vectorscope 纯色参考目标。</summary>
internal sealed class VectorscopeReferenceTargetProvider(OscilloscopeColorConverter converter)
{
    public IReadOnlyList<ScopeReferenceTarget> Create() =>
    [
        Target("R", 255, 0, 0), Target("Mg", 255, 0, 255), Target("B", 0, 0, 255),
        Target("Cy", 0, 255, 255), Target("G", 0, 255, 0), Target("Yl", 255, 255, 0)
    ];

    private ScopeReferenceTarget Target(string label, byte red, byte green, byte blue)
    {
        var pixel = converter.Convert(red, green, blue, 255);
        return new ScopeReferenceTarget(label, ImageOscilloscopeAnalyzer.MapVectorscope(pixel.Cb, pixel.Cr));
    }
}
