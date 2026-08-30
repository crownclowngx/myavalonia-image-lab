namespace ImageLabPlugin.Domain.Frequency;

internal enum FrequencyRegion { Dc, Low, Medium, High }

internal readonly record struct FrequencyBandBoundaries
{
    public FrequencyBandBoundaries(double low, double high)
    {
        if (!(low > 0d && low < high && high < 1d))
            throw new ArgumentOutOfRangeException(nameof(low), "频带边界必须满足 0 < low < high < 1。 ");
        Low = low;
        High = high;
    }

    public double Low { get; }
    public double High { get; }
    public FrequencyRegion Classify(double radius, bool isDc) => isDc ? FrequencyRegion.Dc :
        radius <= Low ? FrequencyRegion.Low : radius <= High ? FrequencyRegion.Medium : FrequencyRegion.High;
    public static FrequencyBandBoundaries Default => new(0.15d, 0.50d);
}

internal sealed record RadialEnergyReport(
    IReadOnlyList<double> Bins,
    double DcShare,
    double LowShare,
    double MediumShare,
    double HighShare,
    double TotalEnergy);

/// <summary>按统一归一化半径累计 256 bin 与四类 Parseval 频域能量。</summary>
internal sealed class RadialEnergyAnalyzer
{
    public const int BinCount = 256;

    public RadialEnergyReport Analyze(FrequencySpectrum spectrum, FrequencyBandBoundaries boundaries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        var bins = new double[BinCount];
        Span<double> regions = stackalloc double[4];
        double total = 0d;
        for (var y = 0; y < spectrum.PaddedHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < spectrum.PaddedWidth; x++)
            {
                var point = FrequencyCoordinates.FromInternal(x, y, spectrum.PaddedWidth, spectrum.PaddedHeight);
                var magnitude = spectrum[x, y].Magnitude;
                var energy = magnitude * magnitude;
                if (!double.IsFinite(energy)) continue;
                total += energy;
                bins[Math.Min(BinCount - 1, (int)(point.Radius * BinCount))] += energy;
                regions[(int)boundaries.Classify(point.Radius, x == 0 && y == 0)] += energy;
            }
        }

        if (total > 0d)
        {
            for (var i = 0; i < bins.Length; i++) bins[i] /= total;
            for (var i = 0; i < regions.Length; i++) regions[i] /= total;
        }
        return new RadialEnergyReport(bins, regions[0], regions[1], regions[2], regions[3], total);
    }
}
