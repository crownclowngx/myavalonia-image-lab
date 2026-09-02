using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.ColorTransfer;

/// <summary>用固定 100-bin 数组汇总逐像素 ΔE00，避免为 16MP 图片常驻 double 列表。</summary>
internal sealed class PerceptualDifferenceAnalyzer(SrgbColorSpace srgb, CieLabColorSpace lab, CieDeltaE deltaE)
{
    public DifferenceSummary Analyze(PixelImage target, PixelImage result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(result);
        if (target.Size != result.Size) throw new ArgumentException("感知误差要求目标与结果尺寸相同。");
        var histogram = new double[100]; var sum = 0d; var weightSum = 0d; var zeroWeight = 0d; var maximum = 0d; long changed = 0;
        var left = target.Rgba.Span; var right = result.Rgba.Span;
        for (var y = 0; y < target.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < target.Size.Width; x++)
            {
                var offset = ((y * target.Size.Width) + x) * 4; var alpha = left[offset + 3]; if (alpha == 0) continue;
                if (left[offset] != right[offset] || left[offset + 1] != right[offset + 1] || left[offset + 2] != right[offset + 2]) changed++;
                var first = ToLab(left[offset], left[offset + 1], left[offset + 2]);
                var second = ToLab(right[offset], right[offset + 1], right[offset + 2]);
                var difference = deltaE.Ciede2000(first, second); var weight = alpha / 255d;
                sum += difference * weight; weightSum += weight; maximum = Math.Max(maximum, difference);
                if (difference <= 1e-12) zeroWeight += weight;
                histogram[Math.Min(99, (int)Math.Floor(difference))] += weight;
            }
        }
        if (weightSum <= 0d) throw new InvalidOperationException("目标图没有可见像素，无法计算感知误差。");
        return new DifferenceSummary(sum / weightSum, Quantile(histogram, zeroWeight, 0.5d), Quantile(histogram, zeroWeight, 0.95d),
            maximum, changed, Array.AsReadOnly(histogram));
    }

    private CieLabColor ToLab(byte r, byte g, byte b) => lab.ToLab(srgb.ToXyz(srgb.Decode(SrgbColor.FromBytes(r, g, b))));
    private static double Quantile(double[] bins, double zeroWeight, double q)
    { var target = bins.Sum() * q; if (target <= zeroWeight) return 0d; var sum = 0d; for (var i = 0; i < bins.Length; i++) { sum += bins[i]; if (sum >= target) return i + 0.5d; } return 99.5d; }
}
