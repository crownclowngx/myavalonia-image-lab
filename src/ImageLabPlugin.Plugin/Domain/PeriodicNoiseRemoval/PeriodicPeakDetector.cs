using ImageLabPlugin.Domain.Frequency;

namespace ImageLabPlugin.Domain.PeriodicNoiseRemoval;

/// <summary>从只读 FFT 产生有界、确定性排序的候选频率对与保守建议。</summary>
/// <remarks>
/// 检测顺序固定为：对数功率、径向中位数/MAD 背景、3×3 严格局部最大值、局部突出度、共轭归并、稳定排序与
/// 环面非极大值抑制。候选只是值得复核的峰，不是噪声结论；取消会抛出且不会返回部分列表。
/// </remarks>
internal sealed class PeriodicPeakDetector(RadialLogPowerBaseline baseline, PeriodicPeakRiskAssessor riskAssessor)
{
    private readonly record struct Peak(int X, int Y, int CanonicalIndex, PeriodicFrequency Frequency,
        double Score, double Prominence, double Compactness);

    public PeriodicNoiseDetectionResult Detect(FrequencySpectrum spectrum, PeriodicNoiseDetectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        ArgumentNullException.ThrowIfNull(settings);
        var radial = baseline.Analyze(spectrum, cancellationToken);
        var logs = radial.LogPowers;
        var peaks = new List<Peak>(Math.Min(4096, spectrum.ValueCount / 16));
        var width = spectrum.PaddedWidth;
        var height = spectrum.PaddedHeight;

        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                var frequency = PeriodicFrequency.FromInternal(x, y, width, height);
                if (frequency.Radius < settings.DcExclusionRadius || !IsStrictLocalMaximum(logs, x, y, width, height))
                    continue;
                var radialBin = RadialLogPowerBaseline.RadialBin(x, y, width, height);
                var scale = Math.Max(RadialLogPowerBaseline.Epsilon,
                    RadialLogPowerBaseline.RobustScale * radial.MedianAbsoluteDeviations[radialBin]);
                var score = (logs[index] - radial.Medians[radialBin]) / scale;
                var prominence = LocalProminence(logs, x, y, width, height);
                if (score < settings.RobustScoreThreshold || prominence < settings.ProminenceThreshold) continue;

                var conjugate = FrequencyCoordinates.ConjugateIndex(x, y, width, height);
                var conjugateIndex = (conjugate.Y * width) + conjugate.X;
                // 一对共轭峰只留下自然线性索引较小者，避免显示、排序和自动建议重复。
                if (index > conjugateIndex) continue;
                var compactness = LocalCompactness(logs, x, y, width, height, prominence);
                peaks.Add(new Peak(x, y, index, PeriodicFrequency.Canonical(frequency), score, prominence, compactness));
                if (peaks.Count > 8192)
                    TrimWeakest(peaks, 4096);
            }
        }

        var ordered = peaks.OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Prominence)
            .ThenBy(item => item.CanonicalIndex)
            .ToArray();
        var accepted = new List<PeriodicFrequencyCandidate>(settings.MaximumCandidates);
        foreach (var peak in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (accepted.Any(item => PeriodicFrequency.ToroidalDistance(
                    item.CanonicalFrequency, peak.Frequency) < settings.SuppressionRadius ||
                PeriodicFrequency.ToroidalDistance(item.ConjugateFrequency, peak.Frequency) < settings.SuppressionRadius))
                continue;
            var dense = ordered.Count(item => item.CanonicalIndex != peak.CanonicalIndex &&
                PeriodicFrequency.ToroidalDistance(item.Frequency, peak.Frequency) <= 0.04d);
            var risk = riskAssessor.Assess(peak.Frequency, peak.Prominence, peak.Compactness, dense, settings);
            accepted.Add(new PeriodicFrequencyCandidate(peak.Frequency, peak.Frequency.Conjugate(), peak.Score,
                peak.Prominence, peak.Compactness, risk.Level, risk.Reasons, peak.CanonicalIndex));
            if (accepted.Count == settings.MaximumCandidates) break;
        }
        var suggestions = accepted.Where(item => item.IsSafeSuggestion)
            .Take(settings.MaximumSuggestions)
            .Select(item => new PeriodicNotch(item.CanonicalFrequency, PeriodicNotchOrigin.Automatic))
            .ToArray();
        return new PeriodicNoiseDetectionResult(accepted, suggestions);
    }

    private static bool IsStrictLocalMaximum(ReadOnlySpan<double> values, int x, int y, int width, int height)
    {
        var index = (y * width) + x;
        var center = values[index];
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            var nx = (x + dx + width) % width;
            var ny = (y + dy + height) % height;
            var neighborIndex = (ny * width) + nx;
            var neighbor = values[neighborIndex];
            if (neighbor > center || (neighbor == center && neighborIndex < index)) return false;
        }
        return true;
    }

    private static double LocalProminence(ReadOnlySpan<double> values, int x, int y, int width, int height)
    {
        double sum = 0d;
        var count = 0;
        for (var dy = -2; dy <= 2; dy++)
        for (var dx = -2; dx <= 2; dx++)
        {
            if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1) continue;
            sum += values[(((y + dy + height) % height) * width) + ((x + dx + width) % width)];
            count++;
        }
        return values[(y * width) + x] - (sum / count);
    }

    private static double LocalCompactness(ReadOnlySpan<double> values, int x, int y, int width, int height,
        double prominence)
    {
        var center = values[(y * width) + x];
        var broad = 0;
        var count = 0;
        var threshold = center - Math.Max(0.05d, prominence * 0.5d);
        for (var dy = -2; dy <= 2; dy++)
        for (var dx = -2; dx <= 2; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            count++;
            if (values[(((y + dy + height) % height) * width) + ((x + dx + width) % width)] >= threshold) broad++;
        }
        return 1d - (broad / (double)count);
    }

    private static void TrimWeakest(List<Peak> peaks, int count)
    {
        var strongest = peaks.OrderByDescending(item => item.Score).ThenBy(item => item.CanonicalIndex)
            .Take(count).ToArray();
        peaks.Clear();
        peaks.AddRange(strongest);
    }
}
