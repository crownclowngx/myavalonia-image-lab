namespace ImageLabPlugin.Domain.Frequency;

/// <summary>以有界直方图估计二维频谱各径向桶的对数功率中位数和 MAD。</summary>
/// <remarks>
/// 该服务只描述“频谱背景随半径变化”这一数学事实，不知道周期噪声、频谱艺术、Document 或 UI。
/// 实现继续使用 128 个径向桶与每桶 256 个量化格，避免为 2048×2048 频谱按桶收集并排序数百万个对象。
/// 三次固定顺序扫描分别产生对数功率、径向中位数和绝对偏差中位数；取消只在完整扫描边界观察，
/// 因而调用方要么得到完整一致的结果，要么得到取消异常，不会消费半成品。
/// </remarks>
internal sealed class RadialLogPowerBaseline
{
    internal const int RadialBinCount = 128;
    internal const int HistogramBinCount = 256;
    internal const double RobustScale = 1.4826d;
    internal const double Epsilon = 1e-6d;

    public RadialLogPowerBaselineResult Analyze(
        FrequencySpectrum spectrum,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        var logs = new double[spectrum.ValueCount];
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        var values = spectrum.Values.Span;
        for (var i = 0; i < values.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var magnitudeSquared = (values[i].Real * values[i].Real) +
                                   (values[i].Imaginary * values[i].Imaginary);
            var value = Math.Log(1d + magnitudeSquared);
            if (!double.IsFinite(value))
                throw new InvalidDataException("频谱对数功率出现非有限值。");
            logs[i] = value;
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        if (logs.Length == 0) throw new InvalidDataException("频谱不能为空。");
        var medians = new double[RadialBinCount];
        var deviations = new double[RadialBinCount];
        if (maximum <= minimum)
            return new RadialLogPowerBaselineResult(logs, medians, deviations);

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
            medians[radial] = counts[radial] == 0
                ? 0d
                : Quantile(histograms, radial, counts[radial], minimum, maximum);

        Array.Clear(histograms);
        Array.Clear(counts);
        var range = Math.Max(Epsilon, maximum - minimum);
        for (var i = 0; i < logs.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var radial = RadialBin(i % spectrum.PaddedWidth, i / spectrum.PaddedWidth,
                spectrum.PaddedWidth, spectrum.PaddedHeight);
            var deviation = Math.Abs(logs[i] - medians[radial]);
            var quantized = Math.Clamp(
                (int)Math.Floor(deviation / range * (HistogramBinCount - 1)),
                0,
                HistogramBinCount - 1);
            histograms[(radial * HistogramBinCount) + quantized]++;
            counts[radial]++;
        }

        for (var radial = 0; radial < RadialBinCount; radial++)
            deviations[radial] = counts[radial] == 0
                ? Epsilon
                : Math.Max(Epsilon, Quantile(histograms, radial, counts[radial], 0d, range));
        return new RadialLogPowerBaselineResult(logs, medians, deviations);
    }

    internal static int RadialBin(int internalX, int internalY, int width, int height)
    {
        var radius = FrequencyCoordinates.FromInternal(internalX, internalY, width, height).Radius;
        return Math.Clamp((int)Math.Floor(radius * RadialBinCount), 0, RadialBinCount - 1);
    }

    private static int Quantize(double value, double minimum, double maximum) =>
        Math.Clamp((int)Math.Floor((value - minimum) / (maximum - minimum) *
                                  (HistogramBinCount - 1)), 0, HistogramBinCount - 1);

    private static double Quantile(
        int[] histogram,
        int radial,
        int count,
        double minimum,
        double maximum)
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

/// <summary>径向背景分析产生的只读数组，仅供一次受控领域计算消费。</summary>
internal sealed class RadialLogPowerBaselineResult(
    double[] logPowers,
    double[] medians,
    double[] medianAbsoluteDeviations)
{
    internal ReadOnlySpan<double> LogPowers => logPowers;
    internal ReadOnlySpan<double> Medians => medians;
    internal ReadOnlySpan<double> MedianAbsoluteDeviations => medianAbsoluteDeviations;
}
