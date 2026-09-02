using System.Collections.ObjectModel;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.ImageOscilloscope;

internal enum ScopeDensityMode
{
    Logarithmic,
    Linear
}

internal enum ScopeClippingMode
{
    Off,
    Luma,
    RgbAny
}

/// <summary>冻结阴影与高光阈值，并在进入扫描前保证两个闭区间互不倒置。</summary>
internal readonly record struct ClippingThresholds
{
    public ClippingThresholds(int shadow, int highlight)
    {
        if ((uint)shadow > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(shadow), shadow, "阴影阈值必须位于 0..255。");
        if ((uint)highlight > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(highlight), highlight, "高光阈值必须位于 0..255。");
        if (shadow >= highlight)
            throw new ArgumentException("阴影阈值必须严格小于高光阈值。");
        Shadow = (byte)shadow;
        Highlight = (byte)highlight;
    }

    public static ClippingThresholds Default => new(5, 250);
    public byte Shadow { get; }
    public byte Highlight { get; }
}

/// <summary>一个源像素在固定白底 sRGB/BT.601 协议下的全部颜色事实。</summary>
internal readonly record struct OscilloscopePixel(
    byte Red, byte Green, byte Blue, byte Alpha, byte Luma,
    double Cb, double Cr, double Saturation, double? Hue)
{
    public double ChromaRadius => Math.Sqrt((Cb * Cb) + (Cr * Cr));
}

internal readonly record struct ScopePoint(int X, int Y);
internal sealed record ScopeReferenceTarget(string Label, ScopePoint Point);

/// <summary>只读二维计数栅格；构造时复制数组，避免 UI 修改领域累计事实。</summary>
internal sealed class ScopeCountGrid
{
    private readonly uint[] _counts;
    private readonly ReadOnlyCollection<uint> _view;

    public ScopeCountGrid(int width, int height, ReadOnlySpan<uint> counts)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (counts.Length != checked(width * height)) throw new ArgumentException("计数数组尺寸与栅格不一致。", nameof(counts));
        Width = width;
        Height = height;
        _counts = counts.ToArray();
        _view = Array.AsReadOnly(_counts);
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<uint> Counts => _view;
    public uint this[int x, int y] => _counts[checked((y * Width) + x)];
    internal ReadOnlySpan<uint> Span => _counts;
}

/// <summary>精确计数到显示强度的可丢弃投影；它不反向持有源图片或分析 Session。</summary>
internal sealed class ScopeDensityProjection
{
    private readonly float[] _tones;
    private readonly ReadOnlyCollection<float> _view;

    public ScopeDensityProjection(int width, int height, uint upperCount, ReadOnlySpan<float> tones)
    {
        if (tones.Length != checked(width * height)) throw new ArgumentException("显示数组尺寸与栅格不一致。", nameof(tones));
        Width = width;
        Height = height;
        UpperCount = upperCount;
        _tones = tones.ToArray();
        _view = Array.AsReadOnly(_tones);
    }

    public int Width { get; }
    public int Height { get; }
    public uint UpperCount { get; }
    public IReadOnlyList<float> Tones => _view;
}

/// <summary>一次主扫描产生的不可变示波器事实。</summary>
/// <remarks>
/// 大数组均由各自只读值对象独占。结果没有路径、Bitmap、取消源或回调，因此可以安全跨 Application/UI
/// 边界传递；源像素总数守恒仍由 Analyzer 的单元测试和构造后验证共同保证。
/// </remarks>
internal sealed class ImageOscilloscopeAnalysis
{
    public ImageOscilloscopeAnalysis(
        ImageSize sourceSize, ScopeCountGrid waveform, ScopeCountGrid redParade,
        ScopeCountGrid greenParade, ScopeCountGrid blueParade, ScopeCountGrid vectorscope,
        ulong[] redHistogram, ulong[] greenHistogram, ulong[] blueHistogram, ulong[] lumaHistogram,
        ulong[] saturationHistogram, double[] hueWeights, ulong hueDefinedCount,
        ulong[] chromaHistogram, double meanCb, double meanCr, double meanChromaRadius)
    {
        SourceSize = sourceSize;
        Waveform = waveform;
        RedParade = redParade;
        GreenParade = greenParade;
        BlueParade = blueParade;
        Vectorscope = vectorscope;
        RedHistogram = Freeze(redHistogram, 256);
        GreenHistogram = Freeze(greenHistogram, 256);
        BlueHistogram = Freeze(blueHistogram, 256);
        LumaHistogram = Freeze(lumaHistogram, 256);
        SaturationHistogram = Freeze(saturationHistogram, 256);
        HueWeights = Array.AsReadOnly(Copy(hueWeights, 360));
        HueDefinedCount = hueDefinedCount;
        ChromaHistogram = Freeze(chromaHistogram, 256);
        MeanCb = meanCb;
        MeanCr = meanCr;
        MeanChromaRadius = meanChromaRadius;
    }

    public ImageSize SourceSize { get; }
    public long PixelCount => SourceSize.PixelCount;
    public ScopeCountGrid Waveform { get; }
    public ScopeCountGrid RedParade { get; }
    public ScopeCountGrid GreenParade { get; }
    public ScopeCountGrid BlueParade { get; }
    public ScopeCountGrid Vectorscope { get; }
    public IReadOnlyList<ulong> RedHistogram { get; }
    public IReadOnlyList<ulong> GreenHistogram { get; }
    public IReadOnlyList<ulong> BlueHistogram { get; }
    public IReadOnlyList<ulong> LumaHistogram { get; }
    public IReadOnlyList<ulong> SaturationHistogram { get; }
    public IReadOnlyList<double> HueWeights { get; }
    public ulong HueDefinedCount { get; }
    public IReadOnlyList<ulong> ChromaHistogram { get; }
    public double MeanCb { get; }
    public double MeanCr { get; }
    public double MeanChromaRadius { get; }

    private static IReadOnlyList<ulong> Freeze(ulong[] values, int expected) => Array.AsReadOnly(Copy(values, expected));
    private static T[] Copy<T>(T[] values, int expected)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != expected) throw new ArgumentException($"数组必须包含 {expected} 项。", nameof(values));
        return (T[])values.Clone();
    }
}

/// <summary>逐项裁切计数，所有阈值比较均包含边界。</summary>
internal sealed record ClippingCounts(
    ulong LumaShadow, ulong LumaHighlight, ulong RgbShadow, ulong RgbHighlight,
    ulong RedShadow, ulong RedHighlight, ulong GreenShadow, ulong GreenHighlight,
    ulong BlueShadow, ulong BlueHighlight);

/// <summary>有界覆盖层事实；每个代理格使用位标志保存命中种类。</summary>
internal sealed class ClippingAnalysis
{
    private readonly byte[] _mask;
    private readonly ReadOnlyCollection<byte> _view;

    public ClippingAnalysis(ClippingThresholds thresholds, ClippingCounts counts, int width, int height, ReadOnlySpan<byte> mask)
    {
        if (mask.Length != checked(width * height)) throw new ArgumentException("覆盖层尺寸不一致。", nameof(mask));
        Thresholds = thresholds;
        Counts = counts;
        Width = width;
        Height = height;
        _mask = mask.ToArray();
        _view = Array.AsReadOnly(_mask);
    }

    public ClippingThresholds Thresholds { get; }
    public ClippingCounts Counts { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<byte> Mask => _view;
    internal ReadOnlySpan<byte> MaskSpan => _mask;
}

internal readonly record struct ImageProbeMapping(bool IsInside, int SourceX, int SourceY, double NormalizedX, double NormalizedY);

/// <summary>一个源像素到所有 Scope 和分布的强类型坐标协议。</summary>
internal sealed record ScopeProbe(
    int SourceX, int SourceY, OscilloscopePixel Pixel, ScopePoint Waveform,
    ScopePoint RedParade, ScopePoint GreenParade, ScopePoint BlueParade, ScopePoint Vectorscope,
    int RedHistogramBin, int GreenHistogramBin, int BlueHistogramBin, int LumaHistogramBin,
    int SaturationBin, int? HueBin, int ChromaBin);
