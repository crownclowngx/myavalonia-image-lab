using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.FrequencyFiltering;

internal enum FrequencyFilterKind { LowPass, HighPass, BandPass, BandStop }
internal enum FrequencyFilterFamily { Ideal, Butterworth, Gaussian }
internal enum FrequencyProjectionMode { Direct, Centered, Additive }

/// <summary>一次频域滤波所需的不可变规范参数。</summary>
/// <remarks>
/// 构造函数同时完成校验和“不适用参数规范化”：单截止滤波的外截止固定为 1，非 Butterworth 家族的阶数固定为 1。
/// 因此数学效果相同的配方不会因隐藏控件残值产生不同指纹，也不会把非法状态传入数值核心。
/// </remarks>
internal sealed class FrequencyFilterRecipe
{
    public FrequencyFilterRecipe(FrequencyFilterKind kind, FrequencyFilterFamily family, double innerCutoff,
        double outerCutoff, int butterworthOrder, FrequencyProjectionMode projectionMode, double projectionGain,
        ImageChannel channel)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(family)) throw new ArgumentOutOfRangeException(nameof(family));
        if (!Enum.IsDefined(projectionMode)) throw new ArgumentOutOfRangeException(nameof(projectionMode));
        if (!Enum.IsDefined(channel)) throw new ArgumentOutOfRangeException(nameof(channel));
        ValidateCutoff(innerCutoff, nameof(innerCutoff));
        if (kind is FrequencyFilterKind.BandPass or FrequencyFilterKind.BandStop)
        {
            ValidateCutoff(outerCutoff, nameof(outerCutoff));
            if (innerCutoff >= outerCutoff)
                throw new ArgumentException("带通/带阻必须满足内截止小于外截止。", nameof(outerCutoff));
        }
        if (family == FrequencyFilterFamily.Butterworth && butterworthOrder is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(butterworthOrder), "Butterworth 阶数必须位于 1 至 12。");
        if (!double.IsFinite(projectionGain) || projectionGain is < 0d or > 4d)
            throw new ArgumentOutOfRangeException(nameof(projectionGain), "显示增益必须有限且位于 0 至 4。");

        Kind = kind;
        Family = family;
        InnerCutoff = innerCutoff;
        OuterCutoff = kind is FrequencyFilterKind.BandPass or FrequencyFilterKind.BandStop ? outerCutoff : 1d;
        ButterworthOrder = family == FrequencyFilterFamily.Butterworth ? butterworthOrder : 1;
        ProjectionMode = projectionMode;
        ProjectionGain = projectionMode == FrequencyProjectionMode.Direct ? 1d : projectionGain;
        Channel = channel;
    }

    public FrequencyFilterKind Kind { get; }
    public FrequencyFilterFamily Family { get; }
    public double InnerCutoff { get; }
    public double OuterCutoff { get; }
    public int ButterworthOrder { get; }
    public FrequencyProjectionMode ProjectionMode { get; }
    public double ProjectionGain { get; }
    public ImageChannel Channel { get; }
    public bool UsesTwoCutoffs => Kind is FrequencyFilterKind.BandPass or FrequencyFilterKind.BandStop;

    /// <summary>包含滤波数学、通道和输出投影语义，用于拒绝导出过期结果。</summary>
    public string Fingerprint()
    {
        var canonical = string.Join('|', "frequency-filter-v1", (int)Kind, (int)Family,
            InnerCutoff.ToString("R", CultureInfo.InvariantCulture),
            OuterCutoff.ToString("R", CultureInfo.InvariantCulture), ButterworthOrder,
            (int)ProjectionMode, ProjectionGain.ToString("R", CultureInfo.InvariantCulture), (int)Channel);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16].ToLowerInvariant();
    }

    /// <summary>只包含决定遮罩和 IFFT 的参数，便于投影变化时安全复用 raw 结果。</summary>
    public string MathematicalFingerprint()
    {
        var canonical = string.Join('|', "frequency-filter-math-v1", (int)Kind, (int)Family,
            InnerCutoff.ToString("R", CultureInfo.InvariantCulture),
            OuterCutoff.ToString("R", CultureInfo.InvariantCulture), ButterworthOrder, (int)Channel);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16].ToLowerInvariant();
    }

    private static void ValidateCutoff(double value, string parameter)
    {
        if (!double.IsFinite(value) || value <= 0d || value > 1d)
            throw new ArgumentOutOfRangeException(parameter, "截止半径必须有限且位于 (0,1]。");
    }
}

internal readonly record struct FrequencyTransitionBand(double RadiusAt90Percent, double RadiusAt10Percent)
{
    public double Width => Math.Max(0d, RadiusAt10Percent - RadiusAt90Percent);
}

internal readonly record struct RadialResponseSample(double Radius, double Gain);

/// <summary>拥有不可变的二维实数增益遮罩与有限径向曲线。</summary>
internal sealed class FrequencyFilterMask
{
    private readonly double[] _gains;
    private readonly RadialResponseSample[] _radialSamples;

    public FrequencyFilterMask(int width, int height, ReadOnlySpan<double> gains,
        ReadOnlySpan<RadialResponseSample> radialSamples, string mathematicalFingerprint)
    {
        if (width <= 0 || height <= 0 || gains.Length != checked(width * height))
            throw new ArgumentException("遮罩缓冲长度与宽高不一致。", nameof(gains));
        ArgumentException.ThrowIfNullOrWhiteSpace(mathematicalFingerprint);
        Width = width;
        Height = height;
        MathematicalFingerprint = mathematicalFingerprint;
        _gains = gains.ToArray();
        _radialSamples = radialSamples.ToArray();
        GainMask = new FrequencyGainMask(width, height, gains, mathematicalFingerprint);
    }

    public int Width { get; }
    public int Height { get; }
    public string MathematicalFingerprint { get; }
    /// <summary>与其他频域产品共享的实数增益事实源；滤波器特有的径向样本仍留在本类型。</summary>
    public FrequencyGainMask GainMask { get; }
    public ReadOnlyMemory<double> Gains => new((double[])_gains.Clone());
    internal ReadOnlySpan<double> GainSpan => _gains;
    public IReadOnlyList<RadialResponseSample> RadialSamples => Array.AsReadOnly(_radialSamples);
    public double this[int internalX, int internalY] => _gains[(internalY * Width) + internalX];
}

/// <summary>频谱乘法和 IFFT 的不可变 raw double 结果。</summary>
internal sealed class FrequencyFilterPlaneResult
{
    private readonly double[] _values;
    public FrequencyFilterPlaneResult(ImageSize size, ReadOnlySpan<double> values, double maximumImaginaryResidual,
        string mathematicalFingerprint)
    {
        if (values.Length != size.PixelCount) throw new ArgumentException("滤波平面长度与图片尺寸不一致。", nameof(values));
        if (!double.IsFinite(maximumImaginaryResidual) || maximumImaginaryResidual < 0d)
            throw new ArgumentOutOfRangeException(nameof(maximumImaginaryResidual));
        Size = size;
        MaximumImaginaryResidual = maximumImaginaryResidual;
        MathematicalFingerprint = mathematicalFingerprint;
        _values = values.ToArray();
    }
    public ImageSize Size { get; }
    public double MaximumImaginaryResidual { get; }
    public string MathematicalFingerprint { get; }
    public ReadOnlyMemory<double> Values => new((double[])_values.Clone());
    internal ReadOnlySpan<double> ValueSpan => _values;
}

/// <summary>空间公平比较专用的完整 padded IFFT 平面，不作为图片结果直接导出。</summary>
internal sealed class PaddedFrequencyPlane
{
    private readonly double[] _values;
    public PaddedFrequencyPlane(int width, int height, ReadOnlySpan<double> values, double maximumImaginaryResidual)
    {
        if (values.Length != checked(width * height)) throw new ArgumentException("padded 平面长度错误。", nameof(values));
        Width = width; Height = height; MaximumImaginaryResidual = maximumImaginaryResidual; _values = values.ToArray();
    }
    public int Width { get; }
    public int Height { get; }
    public double MaximumImaginaryResidual { get; }
    internal ReadOnlySpan<double> ValueSpan => _values;
}

internal sealed record FrequencyProjectionStatistics(double Minimum, double Maximum, double Mean,
    long LowClippedSamples, long HighClippedSamples, int ColorReconstructionClippedPixels);

internal sealed class FrequencyProjectionResult
{
    private readonly double[] _projectedValues;
    public FrequencyProjectionResult(PixelImage image, ImageChannelPlane plane, FrequencyProjectionStatistics statistics)
    { Image = image; Plane = plane; Statistics = statistics; _projectedValues = plane.Values.ToArray(); }
    public PixelImage Image { get; }
    public ImageChannelPlane Plane { get; }
    public FrequencyProjectionStatistics Statistics { get; }
    internal ReadOnlySpan<double> ValueSpan => _projectedValues;
}
