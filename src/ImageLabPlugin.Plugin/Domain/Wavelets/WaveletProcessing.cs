using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Wavelets;

/// <summary>按稳定 ID 解析两个已登记策略；不使用反射发现或服务定位。</summary>
internal sealed class WaveletTransformCatalog(IEnumerable<IWaveletTransform> transforms)
{
    private readonly IReadOnlyDictionary<WaveletTransformId, IWaveletTransform> _transforms =
        transforms.ToDictionary(transform => transform.Id);

    public IWaveletTransform Resolve(WaveletTransformId id) => _transforms.TryGetValue(id, out var transform)
        ? transform
        : throw new ArgumentOutOfRangeException(nameof(id), id, "未登记的小波策略。");
}

/// <summary>只负责从最细 HH 的绝对值中位数估计噪声尺度，不替用户改动阈值。</summary>
internal sealed class WaveletNoiseEstimator
{
    private const double GaussianMedian = 0.67448975d;

    public WaveletNoiseEstimate Estimate(WaveletPyramid pyramid)
    {
        ArgumentNullException.ThrowIfNull(pyramid);
        var region = pyramid.GetLevel(1).DiagonalDetail;
        var absolute = new double[checked((int)region.SampleCount)];
        var values = pyramid.Coefficients.Span;
        var cursor = 0;
        for (var y = region.Y; y < region.Bottom; y++)
            for (var x = region.X; x < region.Right; x++)
                absolute[cursor++] = Math.Abs(values[(y * pyramid.PaddedSize.Width) + x]);
        if (absolute.Length < 4)
            return new(false, 0d, absolute.Length, "最细 HH 样本少于 4 个，不能给出稳定的 MAD 建议。");
        Array.Sort(absolute);
        var median = absolute.Length % 2 == 0
            ? (absolute[(absolute.Length / 2) - 1] + absolute[absolute.Length / 2]) * 0.5d
            : absolute[absolute.Length / 2];
        var sigma = median / GaussianMedian;
        return new(true, sigma, absolute.Length,
            sigma == 0d ? "最细 HH 全零；通用阈值建议为 0。" : "sigma = median(|HH1|) / 0.67448975。");
    }
}

/// <summary>在金字塔私有副本上应用 Hard/Soft 阈值，并返回新的不可变金字塔。</summary>
internal sealed class WaveletThresholdProcessor
{
    public (WaveletPyramid Pyramid, WaveletThresholdStatistics Statistics) Apply(
        WaveletPyramid baseline,
        WaveletDenoiseRecipe recipe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(recipe);
        if (baseline.Transform != recipe.Transform || baseline.Channel != recipe.Channel || baseline.Levels.Count != recipe.Levels)
            throw new ArgumentException("去噪配方与基线金字塔的小波、通道或层数不一致。", nameof(recipe));
        var result = baseline.CloneCoefficients();
        long originalNonZero = 0, retainedNonZero = 0, changed = 0;
        foreach (var levelNumber in recipe.TargetLevels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var level = baseline.GetLevel(levelNumber);
            foreach (var subband in recipe.TargetSubbands)
            {
                var region = level.GetRegion(subband);
                for (var y = region.Y; y < region.Bottom; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (var x = region.X; x < region.Right; x++)
                    {
                        var index = (y * baseline.PaddedSize.Width) + x;
                        var value = result[index];
                        if (value != 0d) originalNonZero++;
                        var updated = recipe.Mode switch
                        {
                            WaveletThresholdMode.Hard => Math.Abs(value) < recipe.Threshold ? 0d : value,
                            WaveletThresholdMode.Soft => Math.CopySign(Math.Max(Math.Abs(value) - recipe.Threshold, 0d), value),
                            _ => throw new ArgumentOutOfRangeException(nameof(recipe), "未知阈值模式。")
                        };
                        if (updated != value) changed++;
                        if (updated != 0d) retainedNonZero++;
                        result[index] = updated;
                    }
                }
            }
        }
        return (new WaveletPyramid(baseline.Transform, baseline.Channel, baseline.OriginalSize,
            baseline.PaddedSize, result, baseline.Levels), new(originalNonZero, retainedNonZero, changed));
    }
}

/// <summary>把真实系数映射到有界灰度像素；投影绝不回写金字塔。</summary>
internal sealed class WaveletSubbandProjector
{
    public WaveletProjection Project(
        WaveletPyramid pyramid,
        int level,
        WaveletSubband subband,
        WaveletProjectionMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pyramid);
        var region = pyramid.GetLevel(level).GetRegion(subband);
        var source = pyramid.Coefficients.Span;
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        var maximumAbsolute = 0d;
        for (var y = region.Y; y < region.Bottom; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = region.X; x < region.Right; x++)
            {
                var value = source[(y * pyramid.PaddedSize.Width) + x];
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
                maximumAbsolute = Math.Max(maximumAbsolute, Math.Abs(value));
            }
        }

        var rgba = new byte[checked((int)(region.SampleCount * 4))];
        for (var y = 0; y < region.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < region.Width; x++)
            {
                var value = source[((region.Y + y) * pyramid.PaddedSize.Width) + region.X + x];
                var normalized = mode switch
                {
                    WaveletProjectionMode.Symmetric => maximumAbsolute == 0d ? 0.5d : 0.5d + (0.5d * value / maximumAbsolute),
                    WaveletProjectionMode.Linear => maximum == minimum ? 0.5d : (value - minimum) / (maximum - minimum),
                    WaveletProjectionMode.Logarithmic => maximumAbsolute == 0d ? 0.5d :
                        0.5d + (0.5d * Math.Sign(value) * Math.Log(1d + Math.Abs(value)) / Math.Log(1d + maximumAbsolute)),
                    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知投影模式。")
                };
                var gray = (byte)Math.Clamp((int)Math.Round(normalized * 255d, MidpointRounding.AwayFromZero), 0, 255);
                var offset = ((y * region.Width) + x) * 4;
                rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = gray;
                rgba[offset + 3] = 255;
            }
        }
        return new(new PixelImage(new(region.Width, region.Height), rgba), minimum, maximum, maximumAbsolute, region);
    }
}

/// <summary>把逆变换后的单通道写回源 RGBA，并报告 double 重建误差与字节裁切。</summary>
internal sealed class WaveletImageReconstructor(WaveletTransformCatalog catalog, ImageChannelConverter channelConverter)
{
    public WaveletReconstructionResult Reconstruct(
        PixelImage source,
        ImageChannelPlane originalPlane,
        WaveletPyramid pyramid,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(originalPlane);
        var plane = catalog.Resolve(pyramid.Transform).Inverse(pyramid, cancellationToken);
        if (plane.Size != originalPlane.Size || plane.Channel != originalPlane.Channel)
            throw new ArgumentException("重建平面与原始分析平面不一致。", nameof(originalPlane));
        double maximum = 0d, squared = 0d;
        var first = originalPlane.Values.Span;
        var second = plane.Values.Span;
        for (var i = 0; i < first.Length; i++)
        {
            var error = Math.Abs(first[i] - second[i]);
            maximum = Math.Max(maximum, error);
            squared += error * error;
        }
        // Wavelet 协议冻结为 AwayFromZero；其他既有产品仍可沿用转换器默认的 ToEven，避免无关 Golden 漂移。
        var applied = channelConverter.Apply(source, plane, MidpointRounding.AwayFromZero);
        return new(plane, applied.Image, applied.ClippedPixelCount, maximum, Math.Sqrt(squared / first.Length));
    }
}
