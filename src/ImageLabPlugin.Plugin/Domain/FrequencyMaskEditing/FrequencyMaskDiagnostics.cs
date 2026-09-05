using System.Numerics;
using ImageLabPlugin.Domain.Shared.Spectral;

namespace ImageLabPlugin.Domain.FrequencyMaskEditing;

internal sealed record FrequencyMaskStatistics(
    double MinimumGain,
    double MaximumGain,
    double MeanGain,
    long NonAllPassBins,
    double NonAllPassRatio,
    double MaximumConjugateError,
    double SourceSpectrumEnergy,
    double EffectiveSpectrumEnergy,
    double RetainedEnergyRatio);

/// <summary>只计算遮罩范围、对称性和频谱能量事实，不修改遮罩或推断“质量提升”。</summary>
internal sealed class FrequencyMaskDiagnostics
{
    public FrequencyMaskStatistics Analyze(FrequencySpectrum spectrum, FrequencyGainMask mask,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        ArgumentNullException.ThrowIfNull(mask);
        if (spectrum.PaddedWidth != mask.Width || spectrum.PaddedHeight != mask.Height)
            throw new ArgumentException("频谱与遮罩尺寸不一致。", nameof(mask));

        var gains = mask.GainSpan;
        var values = spectrum.Values.Span;
        double minimum = 1d, maximum = 0d, sum = 0d, symmetry = 0d, sourceEnergy = 0d, effectiveEnergy = 0d;
        long changed = 0;
        for (var y = 0; y < mask.Height; y++)
            for (var x = 0; x < mask.Width; x++)
            {
                var index = (y * mask.Width) + x;
                if ((index & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
                var gain = gains[index];
                minimum = Math.Min(minimum, gain);
                maximum = Math.Max(maximum, gain);
                sum += gain;
                if (gain != 1d) changed++;
                var conjugate = FrequencyCoordinates.ConjugateIndex(x, y, mask.Width, mask.Height);
                symmetry = Math.Max(symmetry, Math.Abs(gain - gains[(conjugate.Y * mask.Width) + conjugate.X]));
                var energy = Complex.Abs(values[index]);
                energy *= energy;
                sourceEnergy += energy;
                effectiveEnergy += energy * gain * gain;
            }

        return new FrequencyMaskStatistics(minimum, maximum, sum / gains.Length, changed, changed / (double)gains.Length,
            symmetry, sourceEnergy, effectiveEnergy, sourceEnergy == 0d ? 1d : effectiveEnergy / sourceEnergy);
    }
}
