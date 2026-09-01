using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Frequency;

/// <summary>频谱增益乘法和 IFFT 的不可变 raw-double 结果。</summary>
internal sealed class FrequencyMaskApplicationResult
{
    private readonly double[] _values;

    public FrequencyMaskApplicationResult(ImageSize size, ReadOnlySpan<double> values, double maximumImaginaryResidual, string maskFingerprint)
    {
        if (values.Length != size.PixelCount) throw new ArgumentException("IFFT 平面长度与图片尺寸不一致。", nameof(values));
        if (!double.IsFinite(maximumImaginaryResidual) || maximumImaginaryResidual < 0d)
            throw new ArgumentOutOfRangeException(nameof(maximumImaginaryResidual));
        ArgumentException.ThrowIfNullOrWhiteSpace(maskFingerprint);
        Size = size;
        MaximumImaginaryResidual = maximumImaginaryResidual;
        MaskFingerprint = maskFingerprint;
        _values = values.ToArray();
    }

    public ImageSize Size { get; }
    public double MaximumImaginaryResidual { get; }
    public string MaskFingerprint { get; }
    public ReadOnlyMemory<double> Values => new((double[])_values.Clone());
    internal ReadOnlySpan<double> ValueSpan => _values;
}

/// <summary>保留完整补零网格的共享 IFFT 结果，仅供需要相同 Wrap 语义的领域比较使用。</summary>
internal sealed class PaddedFrequencyMaskApplicationResult
{
    private readonly double[] _values;

    public PaddedFrequencyMaskApplicationResult(int width, int height, ReadOnlySpan<double> values,
        double maximumImaginaryResidual, string maskFingerprint)
    {
        if (width <= 0 || height <= 0 || values.Length != checked(width * height))
            throw new ArgumentException("padded IFFT 平面尺寸不一致。", nameof(values));
        Width = width;
        Height = height;
        MaximumImaginaryResidual = maximumImaginaryResidual;
        MaskFingerprint = maskFingerprint;
        _values = values.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public double MaximumImaginaryResidual { get; }
    public string MaskFingerprint { get; }
    internal ReadOnlySpan<double> ValueSpan => _values;
}

/// <summary>复制只读频谱、应用一张实数增益遮罩并执行 IFFT。</summary>
/// <remarks>
/// 该服务无状态，可由 Frequency Filter 与频谱遮罩编辑器共享。工作频谱始终是 Session 缓存的副本；
/// 虚部残差超过 1E-8 说明共轭不变量或数值实现已经失效，因此必须失败，不能静默只取实部。
/// </remarks>
internal sealed class FrequencyMaskApplier(FrequencyInverseTransformer inverseTransformer)
{
    /// <summary>保留既有手工组合入口；实际 IFFT 仍统一委托共享逆变换器。</summary>
    internal FrequencyMaskApplier(Fft2DTransform fft) : this(new FrequencyInverseTransformer(fft))
    {
    }

    public FrequencyMaskApplicationResult Apply(FrequencySpectrum spectrum, FrequencyGainMask mask,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        ArgumentNullException.ThrowIfNull(mask);
        if (mask.Width != spectrum.PaddedWidth || mask.Height != spectrum.PaddedHeight)
            throw new ArgumentException("遮罩与频谱尺寸不一致。", nameof(mask));

        var padded = ApplyPadded(spectrum, mask, cancellationToken);
        var cropped = inverseTransformer.Crop(padded.ValueSpan, padded.Width, padded.Height,
            spectrum.SourceSize, cancellationToken);
        return new FrequencyMaskApplicationResult(spectrum.SourceSize, cropped, padded.MaximumImaginaryResidual,
            mask.Fingerprint);
    }

    public PaddedFrequencyMaskApplicationResult ApplyPadded(FrequencySpectrum spectrum, FrequencyGainMask mask,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        ArgumentNullException.ThrowIfNull(mask);
        if (mask.Width != spectrum.PaddedWidth || mask.Height != spectrum.PaddedHeight)
            throw new ArgumentException("遮罩与频谱尺寸不一致。", nameof(mask));
        var working = spectrum.CreateWorkingCopy();
        var gains = mask.GainSpan;
        for (var i = 0; i < working.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            working[i] *= gains[i];
        }
        var inverse = inverseTransformer.InverseOwned(
            working,
            spectrum.PaddedWidth,
            spectrum.PaddedHeight,
            cancellationToken);
        return new PaddedFrequencyMaskApplicationResult(
            inverse.Width,
            inverse.Height,
            inverse.ValueSpan,
            inverse.MaximumImaginaryResidual,
            mask.Fingerprint);
    }
}
