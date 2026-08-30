using System.Numerics;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.FrequencyFiltering;

/// <summary>只负责复制缓存频谱、逐频点乘增益并执行 IFFT。</summary>
internal sealed class FrequencyFilterEngine(Fft2DTransform fft)
{
    public FrequencyFilterPlaneResult Apply(FrequencySpectrum spectrum, FrequencyFilterMask mask,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum); ArgumentNullException.ThrowIfNull(mask);
        if (mask.Width != spectrum.PaddedWidth || mask.Height != spectrum.PaddedHeight)
            throw new ArgumentException("遮罩与频谱尺寸不一致。", nameof(mask));

        var padded = ApplyPadded(spectrum, mask, cancellationToken);
        var raw = new double[checked((int)spectrum.SourceSize.PixelCount)];
        for (var y = 0; y < spectrum.SourceSize.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            padded.ValueSpan.Slice(y * spectrum.PaddedWidth, spectrum.SourceSize.Width)
                .CopyTo(raw.AsSpan(y * spectrum.SourceSize.Width, spectrum.SourceSize.Width));
        }
        return new FrequencyFilterPlaneResult(spectrum.SourceSize, raw, padded.MaximumImaginaryResidual, mask.MathematicalFingerprint);
    }

    /// <summary>保留完整补零网格，供相同 Wrap 边界的空间核路径进行 raw-double 比较。</summary>
    public PaddedFrequencyPlane ApplyPadded(FrequencySpectrum spectrum, FrequencyFilterMask mask,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum); ArgumentNullException.ThrowIfNull(mask);
        if (mask.Width != spectrum.PaddedWidth || mask.Height != spectrum.PaddedHeight)
            throw new ArgumentException("遮罩与频谱尺寸不一致。", nameof(mask));
        // 工作副本拥有本次执行的唯一写权限。Session 的频谱是跨滑块请求复用的事实，绝不能原地乘遮罩。
        var working = spectrum.CreateWorkingCopy();
        var gains = mask.GainSpan;
        for (var i = 0; i < working.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            working[i] *= gains[i];
        }
        fft.Inverse(working, spectrum.PaddedWidth, spectrum.PaddedHeight, cancellationToken);
        var raw = new double[working.Length];
        double maximumImaginary = 0d;
        for (var i = 0; i < working.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var value = working[i];
            if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary))
                throw new InvalidDataException("IFFT 产生了非有限结果，未提交半成品。");
            maximumImaginary = Math.Max(maximumImaginary, Math.Abs(value.Imaginary));
            raw[i] = value.Real;
        }
        // 径向实数遮罩应保持共轭对称；超限意味着坐标或遮罩实现有错，不能静默丢弃虚部。
        if (maximumImaginary > 1e-8)
            throw new InvalidDataException($"IFFT 虚部残差 {maximumImaginary:E3} 超出 1E-8 数值门禁。");
        return new PaddedFrequencyPlane(spectrum.PaddedWidth, spectrum.PaddedHeight, raw, maximumImaginary);
    }
}

/// <summary>把 raw 滤波信号按 Direct、Centered 或 Additive 语义投影并回写选定通道。</summary>
internal sealed class FrequencySignalProjector(ImageChannelConverter channelConverter)
{
    public FrequencyProjectionResult Project(PixelImage source, ImageChannelPlane sourcePlane,
        FrequencyFilterPlaneResult filtered, FrequencyFilterRecipe recipe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(sourcePlane);
        ArgumentNullException.ThrowIfNull(filtered); ArgumentNullException.ThrowIfNull(recipe);
        if (source.Size != sourcePlane.Size || source.Size != filtered.Size || sourcePlane.Channel != recipe.Channel)
            throw new ArgumentException("源图、源通道、滤波平面或配方通道不一致。");
        if (!StringComparer.Ordinal.Equals(filtered.MathematicalFingerprint, recipe.MathematicalFingerprint()))
            throw new InvalidOperationException("raw 滤波结果与当前数学配方不一致。");

        var sourceValues = sourcePlane.Values.Span;
        var raw = filtered.ValueSpan;
        var projected = new double[raw.Length];
        double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity, sum = 0d;
        long low = 0, high = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            // Centered 的 128 是观察零均值信号的显示偏置，只能加一次；Additive 则把 raw 高频只加回源信号一次。
            var value = recipe.ProjectionMode switch
            {
                FrequencyProjectionMode.Direct => raw[i],
                FrequencyProjectionMode.Centered => 128d + (recipe.ProjectionGain * raw[i]),
                FrequencyProjectionMode.Additive => sourceValues[i] + (recipe.ProjectionGain * raw[i]),
                _ => throw new ArgumentOutOfRangeException(nameof(recipe))
            };
            projected[i] = value;
            minimum = Math.Min(minimum, value); maximum = Math.Max(maximum, value); sum += value;
            if (value < 0d) low++; else if (value > 255d) high++;
        }
        var plane = new ImageChannelPlane(source.Size, recipe.Channel, projected);
        var reconstruction = channelConverter.Apply(source, plane, MidpointRounding.AwayFromZero);
        return new FrequencyProjectionResult(reconstruction.Image, plane,
            new FrequencyProjectionStatistics(minimum, maximum, sum / projected.Length, low, high, reconstruction.ClippedPixelCount));
    }
}
