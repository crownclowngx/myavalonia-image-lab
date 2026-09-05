using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.FrequencyFiltering;

internal readonly record struct FrequencyOutlier(int X, int Y, double Value);
internal sealed record FrequencySideEffectDiagnostics(
    double FilteredMinimum, double FilteredMaximum, double FilteredMean,
    long FilteredBelowZero, long FilteredAbove255, IReadOnlyList<FrequencyOutlier> Outliers,
    double MeanAbsoluteDifference, double SourceGradientEnergy, double ResultGradientEnergy,
    IReadOnlyList<double> SourceHorizontalProfile, IReadOnlyList<double> ResultHorizontalProfile,
    IReadOnlyList<double> SourceVerticalProfile, IReadOnlyList<double> ResultVerticalProfile);

/// <summary>计算 raw 越界、差异、梯度能量和有限剖面，不修改结果也不推断主观质量。</summary>
/// <remarks>
/// “越界”和“梯度能量变化”是可重复数值事实；是否感到 Ringing、模糊或更清晰仍需结合滤波家族、
/// 边缘剖面和用户观察解释。位置摘要最多保留 32 项，避免极端图片为诊断再次分配全尺寸坐标集合。
/// </remarks>
internal sealed class FrequencySideEffectAnalyzer
{
    private const int MaximumOutlierSummary = 32;

    public FrequencySideEffectDiagnostics Analyze(ImageChannelPlane source, FrequencyFilterPlaneResult filtered,
        ImageChannelPlane projected, double normalizedX = 0.5d, double normalizedY = 0.5d,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(filtered); ArgumentNullException.ThrowIfNull(projected);
        if (source.Size != filtered.Size || source.Size != projected.Size) throw new ArgumentException("诊断平面尺寸必须一致。");
        if (!double.IsFinite(normalizedX) || normalizedX is < 0d or > 1d ||
            !double.IsFinite(normalizedY) || normalizedY is < 0d or > 1d) throw new ArgumentOutOfRangeException(nameof(normalizedX));
        var sourceValues = source.Values.Span; var filteredValues = filtered.ValueSpan; var resultValues = projected.Values.Span;
        double min = double.PositiveInfinity, max = double.NegativeInfinity, sum = 0d, absolute = 0d;
        long low = 0, high = 0; var outliers = new List<FrequencyOutlier>(MaximumOutlierSummary);
        for (var i = 0; i < filteredValues.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var value = filteredValues[i]; min = Math.Min(min, value); max = Math.Max(max, value); sum += value;
            var outside = value < 0d || value > 255d;
            if (value < 0d) low++; else if (value > 255d) high++;
            if (outside && outliers.Count < MaximumOutlierSummary)
                outliers.Add(new FrequencyOutlier(i % source.Size.Width, i / source.Size.Width, value));
            absolute += Math.Abs(resultValues[i] - sourceValues[i]);
        }
        var row = Math.Clamp((int)Math.Round(normalizedY * (source.Size.Height - 1)), 0, source.Size.Height - 1);
        var column = Math.Clamp((int)Math.Round(normalizedX * (source.Size.Width - 1)), 0, source.Size.Width - 1);
        return new FrequencySideEffectDiagnostics(min, max, sum / filteredValues.Length, low, high, outliers,
            absolute / filteredValues.Length, GradientEnergy(sourceValues, source.Size), GradientEnergy(resultValues, source.Size),
            ProfileRow(sourceValues, source.Size, row), ProfileRow(resultValues, source.Size, row),
            ProfileColumn(sourceValues, source.Size, column), ProfileColumn(resultValues, source.Size, column));
    }

    private static double GradientEnergy(ReadOnlySpan<double> values, ImageSize size)
    {
        if (size.Width < 3 || size.Height < 3) return 0d;
        double energy = 0d; long count = 0;
        for (var y = 1; y < size.Height - 1; y++)
            for (var x = 1; x < size.Width - 1; x++)
            {
                var gx = (values[(y * size.Width) + x + 1] - values[(y * size.Width) + x - 1]) * 0.5d;
                var gy = (values[((y + 1) * size.Width) + x] - values[((y - 1) * size.Width) + x]) * 0.5d;
                energy += (gx * gx) + (gy * gy); count++;
            }
        return count == 0 ? 0d : energy / count;
    }

    private static IReadOnlyList<double> ProfileRow(ReadOnlySpan<double> values, ImageSize size, int row) =>
        values.Slice(row * size.Width, size.Width).ToArray();
    private static IReadOnlyList<double> ProfileColumn(ReadOnlySpan<double> values, ImageSize size, int column)
    {
        var result = new double[size.Height];
        for (var y = 0; y < size.Height; y++) result[y] = values[(y * size.Width) + column];
        return result;
    }
}
