using ImageLabPlugin.Domain.Frequency;

namespace ImageLabPlugin.Domain.PeriodicNoiseRemoval;

/// <summary>以有界直方图估计各径向桶的对数功率中位数和 MAD。</summary>
/// <remarks>
/// 自然图像的频谱背景随半径下降，不能使用单一全局亮度阈值。本服务先扫描有限的 log-power 范围，再以 128 个径向桶、
/// 每桶 256 个量化格近似中位数；第三次扫描用相同结构估计绝对偏差中位数。空间复杂度固定，不为每个桶保留或排序像素对象。
/// </remarks>
internal sealed class RadialSpectrumBaseline
{
    internal const int RadialBinCount = 128;
    internal const int HistogramBinCount = 256;
    internal const double RobustScale = 1.4826d;
    internal const double Epsilon = 1e-6d;

    public RadialBaselineResult Analyze(FrequencySpectrum spectrum, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        var logs = new double[spectrum.ValueCount];
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        var values = spectrum.Values.Span;
        for (var i = 0; i < values.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var magnitudeSquared = (values[i].Real * values[i].Real) + (values[i].Imaginary * values[i].Imaginary);
            var value = Math.Log(1d + magnitudeSquared);
            if (!double.IsFinite(value)) throw new InvalidDataException("频谱对数功率出现非有限值。");
            logs[i] = value;
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        if (logs.Length == 0) throw new InvalidDataException("频谱不能为空。");
        var medians = new double[RadialBinCount];
        var deviations = new double[RadialBinCount];
        if (maximum <= minimum)
            return new RadialBaselineResult(logs, medians, deviations);

        var histograms = new int[RadialBinCount * HistogramBinCount];
        var counts = new int[RadialBinCount];
        for (var i = 0; i < logs.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var radial = RadialBin(i % spectrum.PaddedWidth, i / spectrum.PaddedWidth,
                spectrum.PaddedWidth, spectrum.PaddedHeight);
            var quantized = Quantize(logs[i], minimum, maximum);
            histograms[(radial * HistogramBinCount) + quantized]++;
            counts[radial]++;
        }
        for (var radial = 0; radial < RadialBinCount; radial++)
            medians[radial] = counts[radial] == 0 ? 0d : Quantile(histograms, radial, counts[radial], minimum, maximum);

        Array.Clear(histograms);
        Array.Clear(counts);
        var range = Math.Max(Epsilon, maximum - minimum);
        for (var i = 0; i < logs.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var radial = RadialBin(i % spectrum.PaddedWidth, i / spectrum.PaddedWidth,
                spectrum.PaddedWidth, spectrum.PaddedHeight);
            var deviation = Math.Abs(logs[i] - medians[radial]);
            var quantized = Math.Clamp((int)Math.Floor(deviation / range * (HistogramBinCount - 1)), 0, HistogramBinCount - 1);
            histograms[(radial * HistogramBinCount) + quantized]++;
            counts[radial]++;
        }
        for (var radial = 0; radial < RadialBinCount; radial++)
            deviations[radial] = counts[radial] == 0 ? Epsilon : Math.Max(Epsilon,
                Quantile(histograms, radial, counts[radial], 0d, range));
        return new RadialBaselineResult(logs, medians, deviations);
    }

    internal static int RadialBin(int internalX, int internalY, int width, int height)
    {
        var radius = FrequencyCoordinates.FromInternal(internalX, internalY, width, height).Radius;
        return Math.Clamp((int)Math.Floor(radius * RadialBinCount), 0, RadialBinCount - 1);
    }

    private static int Quantize(double value, double minimum, double maximum) =>
        Math.Clamp((int)Math.Floor((value - minimum) / (maximum - minimum) * (HistogramBinCount - 1)), 0, HistogramBinCount - 1);

    private static double Quantile(int[] histogram, int radial, int count, double minimum, double maximum)
    {
        var target = (count - 1) / 2;
        var accumulated = 0;
        var offset = radial * HistogramBinCount;
        for (var bin = 0; bin < HistogramBinCount; bin++)
        {
            accumulated += histogram[offset + bin];
            if (accumulated > target)
                return minimum + ((bin + 0.5d) / HistogramBinCount * (maximum - minimum));
        }
        return maximum;
    }
}

/// <summary>径向背景分析产生的只读数组，仅供候选检测器在一次调用内消费。</summary>
internal sealed class RadialBaselineResult(double[] logPowers, double[] medians, double[] medianAbsoluteDeviations)
{
    internal ReadOnlySpan<double> LogPowers => logPowers;
    internal ReadOnlySpan<double> Medians => medians;
    internal ReadOnlySpan<double> MedianAbsoluteDeviations => medianAbsoluteDeviations;
}
