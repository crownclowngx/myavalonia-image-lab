using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.HybridImage;

internal sealed record HybridRawStatistics(
    double Minimum,
    double Maximum,
    double Mean,
    int UnderflowCount,
    int OverflowCount,
    int ClippedPixelCount,
    double ClippedRatio);

internal sealed record HybridCompositionResult(
    HybridLumaPlane LowA,
    HybridLumaPlane HighB,
    HybridLumaPlane Raw,
    PixelImage Quantized,
    HybridRawStatistics Statistics);

/// <summary>组合 Gaussian 低频 A 与有符号高频 B，并只在最终边界量化。</summary>
/// <remarks>
/// 高频严格定义为 B-Gaussian(B)，负值不会加 0.5 或提前裁切。ToEven 和 [0,255] 裁切只作用于最终
/// 显示/导出副本；统计仍基于未量化 raw，从而能解释增益造成的上下溢，而不是隐藏归一化。
/// </remarks>
internal sealed class HybridImageComposer(GaussianPlaneFilter filter)
{
    public HybridCompositionResult Compose(HybridLumaPlane sourceA, HybridLumaPlane alignedB,
        double lowSigma, double highSigma, double lowGain, double highGain,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceA);
        ArgumentNullException.ThrowIfNull(alignedB);
        if (sourceA.Size != alignedB.Size) throw new ArgumentException("A/B 亮度平面尺寸必须一致。", nameof(alignedB));
        ValidateGain(lowGain, nameof(lowGain));
        ValidateGain(highGain, nameof(highGain));

        // gain=0 时短路对应卷积，但仍返回尺寸一致的零分量，保持结果 DTO 的原子性。
        var low = lowGain == 0d ? Zero(sourceA.Size) : filter.Apply(sourceA, lowSigma, cancellationToken);
        HybridLumaPlane high;
        if (highGain == 0d)
        {
            high = Zero(sourceA.Size);
        }
        else
        {
            var blurredB = filter.Apply(alignedB, highSigma, cancellationToken);
            var values = new double[checked((int)alignedB.Size.PixelCount)];
            for (var i = 0; i < values.Length; i++)
            {
                if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
                values[i] = alignedB.Values.Span[i] - blurredB.Values.Span[i];
            }
            high = new HybridLumaPlane(alignedB.Size, values);
        }

        var raw = new double[checked((int)sourceA.Size.PixelCount)];
        var rgba = new byte[checked(raw.Length * 4)];
        var underflow = 0;
        var overflow = 0;
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        double sum = 0d;
        for (var i = 0; i < raw.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var value = (lowGain * low.Values.Span[i]) + (highGain * high.Values.Span[i]);
            if (!double.IsFinite(value)) throw new InvalidOperationException("混合结果出现 NaN 或 Infinity。");
            raw[i] = value;
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
            sum += value;
            if (value < 0d) underflow++;
            else if (value > 1d) overflow++;
            var level = (byte)Math.Clamp((int)Math.Round(value * 255d, MidpointRounding.ToEven), 0, 255);
            var offset = i * 4;
            rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = level;
            rgba[offset + 3] = 255;
        }
        var clipped = checked(underflow + overflow);
        return new HybridCompositionResult(low, high, new HybridLumaPlane(sourceA.Size, raw),
            new PixelImage(sourceA.Size, rgba), new HybridRawStatistics(minimum, maximum,
                sum / raw.Length, underflow, overflow, clipped, clipped / (double)raw.Length));
    }

    internal static PixelImage Quantize(HybridLumaPlane plane, CancellationToken cancellationToken = default)
    {
        var rgba = new byte[checked((int)(plane.Size.PixelCount * 4))];
        for (var i = 0; i < plane.Values.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var level = (byte)Math.Clamp((int)Math.Round(plane.Values.Span[i] * 255d,
                MidpointRounding.ToEven), 0, 255);
            var offset = i * 4;
            rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = level;
            rgba[offset + 3] = 255;
        }
        return new PixelImage(plane.Size, rgba);
    }

    private static HybridLumaPlane Zero(ImageSize size) => new(size, new double[checked((int)size.PixelCount)]);
    private static void ValidateGain(double gain, string name)
    {
        if (!double.IsFinite(gain) || gain is < 0d or > HybridImageRecipe.MaximumGain)
            throw new ArgumentOutOfRangeException(name, "增益必须是 [0,2] 内的有限值。");
    }
}
