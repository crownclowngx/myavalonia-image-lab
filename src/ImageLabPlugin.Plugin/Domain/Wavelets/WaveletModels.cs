using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Wavelets;

/// <summary>V1 冻结的小波策略稳定标识；持久化与报告只保存这些 ID，不保存 CLR 类型名。</summary>
internal enum WaveletTransformId
{
    Haar,
    Cdf53
}

/// <summary>packed 金字塔中的四类子带。</summary>
internal enum WaveletSubband
{
    Approximation,
    HorizontalDetail,
    VerticalDetail,
    DiagonalDetail
}

/// <summary>系数显示方式。它只影响投影，不进入变换或去噪数学配方。</summary>
internal enum WaveletProjectionMode
{
    Symmetric,
    Linear,
    Logarithmic
}

internal enum WaveletThresholdMode { Hard, Soft }
internal enum WaveletThresholdSource { Manual, Universal }

/// <summary>使用左闭右开坐标表示 packed 系数中的矩形，消除边界是否包含的歧义。</summary>
internal readonly record struct WaveletRegion(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
    public long SampleCount => (long)Width * Height;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>描述一级变换前的有效 LL 区域以及变换后的四个互不重叠子带。</summary>
internal sealed record WaveletLevelDescriptor(
    int Level,
    int ActiveWidth,
    int ActiveHeight,
    WaveletRegion Approximation,
    WaveletRegion HorizontalDetail,
    WaveletRegion VerticalDetail,
    WaveletRegion DiagonalDetail)
{
    public WaveletRegion GetRegion(WaveletSubband subband) => subband switch
    {
        WaveletSubband.Approximation => Approximation,
        WaveletSubband.HorizontalDetail => HorizontalDetail,
        WaveletSubband.VerticalDetail => VerticalDetail,
        WaveletSubband.DiagonalDetail => DiagonalDetail,
        _ => throw new ArgumentOutOfRangeException(nameof(subband), subband, "未知小波子带。")
    };
}

/// <summary>不可变去噪配方；目标层和子带在构造时归一化，避免 UI 异步修改集合。</summary>
internal sealed record WaveletDenoiseRecipe
{
    public WaveletDenoiseRecipe(
        WaveletTransformId transform,
        ImageChannel channel,
        int levels,
        WaveletThresholdMode mode,
        WaveletThresholdSource source,
        double threshold,
        IEnumerable<int> targetLevels,
        IEnumerable<WaveletSubband> targetSubbands)
    {
        if (levels is < 1 or > WaveletLimits.MaximumLevels)
            throw new ArgumentOutOfRangeException(nameof(levels), $"分解层数必须位于 1–{WaveletLimits.MaximumLevels}。");
        if (!double.IsFinite(threshold) || threshold < 0d)
            throw new ArgumentOutOfRangeException(nameof(threshold), "阈值必须是大于或等于零的有限数。");
        var normalizedLevels = targetLevels.Distinct().Order().ToArray();
        if (normalizedLevels.Length == 0 || normalizedLevels.Any(level => level < 1 || level > levels))
            throw new ArgumentException("目标层必须是当前分解范围内的非空集合。", nameof(targetLevels));
        var normalizedSubbands = targetSubbands.Distinct().Order().ToArray();
        if (normalizedSubbands.Length == 0 || normalizedSubbands.Contains(WaveletSubband.Approximation))
            throw new ArgumentException("去噪只能选择 LH、HL、HH 细节子带，LL 不允许被修改。", nameof(targetSubbands));

        Transform = transform;
        Channel = channel;
        Levels = levels;
        Mode = mode;
        Source = source;
        Threshold = threshold;
        TargetLevels = normalizedLevels;
        TargetSubbands = normalizedSubbands;
    }

    public WaveletTransformId Transform { get; }
    public ImageChannel Channel { get; }
    public int Levels { get; }
    public WaveletThresholdMode Mode { get; }
    public WaveletThresholdSource Source { get; }
    public double Threshold { get; }
    public IReadOnlyList<int> TargetLevels { get; }
    public IReadOnlyList<WaveletSubband> TargetSubbands { get; }

    public string Fingerprint()
    {
        var canonical = string.Join('|',
            Transform, Channel, Levels, Mode, Source,
            Threshold.ToString("R", CultureInfo.InvariantCulture),
            string.Join(',', TargetLevels), string.Join(',', TargetSubbands));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16].ToLowerInvariant();
    }
}

internal static class WaveletLimits
{
    public const int MaximumLevels = 6;
    public const long MaximumPixels = 16_000_000;
    public const int MaximumScanThresholds = 21;
    public const int MaximumScanCases = 60;
}

internal sealed record WaveletNoiseEstimate(bool IsAvailable, double Sigma, int SampleCount, string Explanation)
{
    public double UniversalThreshold => IsAvailable && SampleCount > 0
        ? Sigma * Math.Sqrt(2d * Math.Log(SampleCount))
        : 0d;
}

internal sealed record WaveletThresholdStatistics(long OriginalNonZero, long RetainedNonZero, long ChangedCoefficients)
{
    public double RetainedRatio => OriginalNonZero == 0 ? 1d : RetainedNonZero / (double)OriginalNonZero;
}

internal sealed record WaveletProjection(PixelImage Image, double Minimum, double Maximum, double Scale, WaveletRegion Region);

internal sealed record WaveletReconstructionResult(
    ImageChannelPlane Plane,
    PixelImage Image,
    int ClippedPixelCount,
    double MaximumAbsoluteError,
    double RootMeanSquareError);

/// <summary>二维变换策略的最小共同契约；所有实现都负责确定性扩展、packed 分解和逆变换。</summary>
internal interface IWaveletTransform
{
    WaveletTransformId Id { get; }
    WaveletPyramid Forward(ImageChannelPlane plane, int levels, CancellationToken cancellationToken = default);
    ImageChannelPlane Inverse(WaveletPyramid pyramid, CancellationToken cancellationToken = default);
    ImageChannelPlane InverseToLevel(WaveletPyramid pyramid, int targetLevel, CancellationToken cancellationToken = default);
}
