using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.PeriodicNoiseRemoval;

internal sealed record PeriodicCandidateSuppression(PeriodicFrequency Frequency, double OriginalMagnitude,
    double FilteredMagnitude, double SuppressionRatio);

/// <summary>周期陷波的一组可复核数值事实，不给出“修复成功”主观分数。</summary>
internal sealed record PeriodicNoiseLossDiagnostics(
    PeriodicNotchMaskStatistics Mask,
    double OriginalSpectrumEnergy,
    double RemovedSpectrumEnergy,
    double RemovedSpectrumEnergyRatio,
    double RawMinimum,
    double RawMaximum,
    long RawBelowZero,
    long RawAbove255,
    int ColorReconstructionClippedPixels,
    double MeanAbsoluteChannelDifference,
    double MaximumAbsoluteChannelDifference,
    double MaximumImaginaryResidual,
    IReadOnlyList<PeriodicCandidateSuppression> CandidateSuppressions,
    FullReferenceQualityMetrics Quality);

/// <summary>统计遮罩能量、候选峰抑制、raw 越界和空间差异。</summary>
/// <remarks>
/// 频谱移除率按功率 <c>|F|²</c> 与振幅增益平方 <c>H²</c> 计算；空间统计使用 IFFT 的 raw double 与回写后的
/// 选定通道。服务只报告可重复事实，不修改结果，也不把 PSNR/SSIM 或能量损失解释为主观质量结论。
/// </remarks>
internal sealed class PeriodicNoiseLossAnalyzer
{
    public PeriodicNoiseLossDiagnostics Analyze(FrequencySpectrum spectrum, PeriodicNotchMask mask,
        FrequencyMaskApplicationResult raw, ImageChannelPlane sourcePlane, ImageChannelPlane resultPlane,
        int clippedPixels, IReadOnlyList<PeriodicFrequencyCandidate> selectedCandidates,
        FullReferenceQualityMetrics quality, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(sourcePlane);
        ArgumentNullException.ThrowIfNull(resultPlane);
        ArgumentNullException.ThrowIfNull(selectedCandidates);
        var spectrumValues = spectrum.Values.Span;
        var gains = mask.GainMask.GainSpan;
        double originalEnergy = 0d, filteredEnergy = 0d;
        for (var i = 0; i < spectrumValues.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var energy = (spectrumValues[i].Real * spectrumValues[i].Real) +
                (spectrumValues[i].Imaginary * spectrumValues[i].Imaginary);
            originalEnergy += energy;
            filteredEnergy += energy * gains[i] * gains[i];
        }

        var rawValues = raw.ValueSpan;
        var source = sourcePlane.Values.Span;
        var result = resultPlane.Values.Span;
        double rawMinimum = double.PositiveInfinity, rawMaximum = double.NegativeInfinity;
        double absolute = 0d, maximumAbsolute = 0d;
        long below = 0, above = 0;
        for (var i = 0; i < rawValues.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            rawMinimum = Math.Min(rawMinimum, rawValues[i]);
            rawMaximum = Math.Max(rawMaximum, rawValues[i]);
            if (rawValues[i] < 0d) below++;
            else if (rawValues[i] > 255d) above++;
            var difference = Math.Abs(result[i] - source[i]);
            absolute += difference;
            maximumAbsolute = Math.Max(maximumAbsolute, difference);
        }

        var suppressions = new List<PeriodicCandidateSuppression>(Math.Min(32, selectedCandidates.Count));
        foreach (var candidate in selectedCandidates.Take(32))
        {
            var index = candidate.CanonicalFrequency.ToInternal(spectrum.PaddedWidth, spectrum.PaddedHeight);
            var original = spectrum[index.X, index.Y].Magnitude;
            var filtered = original * mask.GainMask[index.X, index.Y];
            suppressions.Add(new PeriodicCandidateSuppression(candidate.CanonicalFrequency, original, filtered,
                original <= 0d ? 0d : 1d - (filtered / original)));
        }
        var removed = Math.Max(0d, originalEnergy - filteredEnergy);
        return new PeriodicNoiseLossDiagnostics(mask.Statistics, originalEnergy, removed,
            originalEnergy <= 0d ? 0d : removed / originalEnergy, rawMinimum, rawMaximum, below, above,
            clippedPixels, absolute / rawValues.Length, maximumAbsolute, raw.MaximumImaginaryResidual,
            suppressions.AsReadOnly(), quality);
    }
}
