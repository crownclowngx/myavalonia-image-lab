using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ImageLabPlugin.Domain.SpectralArt;

internal enum SpectralPatternSourceKind { Text, LogoImage, QrImage }
internal enum SpectralPatternSamplingMode { BinaryNearest, GrayscaleArea }
internal enum SpectralPatternFitMode { Contain, Stretch }
internal enum SpectralPatternBackground { Black, White }

/// <summary>与字体、文件和 UI 无关的不可变频谱图案。</summary>
/// <remarks>
/// 权重只表示目标频点应增加多少对数功率，不表示 bit、字符或 Payload。构造函数复制调用方缓冲并验证
/// 1..512 尺寸、有限值和非空前景，使后续映射与写入器可以把本对象当作稳定事实。指纹按行主序和
/// IEEE 754 精确位模式计算；文件路径和原始文字不进入指纹，避免泄露输入来源。
/// </remarks>
internal sealed class SpectralPattern
{
    public const int MaximumEdge = 512;
    public const int MaximumSamples = MaximumEdge * MaximumEdge;
    private readonly double[] _weights;

    public SpectralPattern(
        int width,
        int height,
        ReadOnlySpan<double> weights,
        SpectralPatternSamplingMode samplingMode,
        SpectralPatternSourceKind sourceKind)
    {
        if (width is < 1 or > MaximumEdge || height is < 1 or > MaximumEdge)
            throw new ArgumentOutOfRangeException(nameof(width), "Pattern 宽高必须位于 1..512。");
        if (checked(width * height) > MaximumSamples || weights.Length != checked(width * height))
            throw new ArgumentException("Pattern 缓冲长度与尺寸不一致或超过预算。", nameof(weights));
        if (!Enum.IsDefined(samplingMode)) throw new ArgumentOutOfRangeException(nameof(samplingMode));
        if (!Enum.IsDefined(sourceKind)) throw new ArgumentOutOfRangeException(nameof(sourceKind));

        _weights = weights.ToArray();
        var hasForeground = false;
        for (var i = 0; i < _weights.Length; i++)
        {
            var value = _weights[i];
            if (!double.IsFinite(value) || value is < 0d or > 1d)
                throw new ArgumentOutOfRangeException(nameof(weights), "Pattern 权重必须是 [0,1] 内的有限值。");
            hasForeground |= value > 0d;
        }
        if (!hasForeground) throw new ArgumentException("Pattern 不能全为零。", nameof(weights));

        Width = width;
        Height = height;
        SamplingMode = samplingMode;
        SourceKind = sourceKind;
        Fingerprint = CreateFingerprint();
    }

    public int Width { get; }
    public int Height { get; }
    public SpectralPatternSamplingMode SamplingMode { get; }
    public SpectralPatternSourceKind SourceKind { get; }
    public string Fingerprint { get; }
    public ReadOnlyMemory<double> Weights => new((double[])_weights.Clone());
    internal ReadOnlySpan<double> WeightSpan => _weights;
    public double this[int x, int y] => _weights[checked((y * Width) + x)];

    private string CreateFingerprint()
    {
        var header = string.Join('|', "spectral-pattern-v1", Width, Height,
            (int)SamplingMode, (int)SourceKind);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(header));
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        foreach (var weight in _weights)
        {
            BitConverter.TryWriteBytes(bytes, BitConverter.DoubleToInt64Bits(weight));
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset())[..16].ToLower(CultureInfo.InvariantCulture);
    }
}

/// <summary>图片来源规范化的完整、显式参数。</summary>
internal sealed record SpectralPatternNormalizationOptions
{
    public SpectralPatternNormalizationOptions(
        SpectralPatternSourceKind sourceKind,
        SpectralPatternSamplingMode samplingMode,
        int targetWidth,
        int targetHeight,
        double binaryThreshold,
        bool invert,
        SpectralPatternBackground background)
    {
        if (!Enum.IsDefined(sourceKind)) throw new ArgumentOutOfRangeException(nameof(sourceKind));
        if (!Enum.IsDefined(samplingMode)) throw new ArgumentOutOfRangeException(nameof(samplingMode));
        if (!Enum.IsDefined(background)) throw new ArgumentOutOfRangeException(nameof(background));
        if (targetWidth is < 1 or > SpectralPattern.MaximumEdge ||
            targetHeight is < 1 or > SpectralPattern.MaximumEdge)
            throw new ArgumentOutOfRangeException(nameof(targetWidth));
        if (!double.IsFinite(binaryThreshold) || binaryThreshold is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(binaryThreshold));
        SourceKind = sourceKind;
        SamplingMode = samplingMode;
        TargetWidth = targetWidth;
        TargetHeight = targetHeight;
        BinaryThreshold = binaryThreshold;
        Invert = invert;
        Background = background;
    }

    public SpectralPatternSourceKind SourceKind { get; }
    public SpectralPatternSamplingMode SamplingMode { get; }
    public int TargetWidth { get; }
    public int TargetHeight { get; }
    public double BinaryThreshold { get; }
    public bool Invert { get; }
    public SpectralPatternBackground Background { get; }
}
