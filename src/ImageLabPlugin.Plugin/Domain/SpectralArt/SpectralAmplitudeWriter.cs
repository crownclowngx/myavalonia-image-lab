using System.Numerics;
using ImageLabPlugin.Domain.Shared.Spectral;

namespace ImageLabPlugin.Domain.SpectralArt;

/// <summary>幅度写入产生的可复算频域事实。</summary>
internal sealed record SpectralAmplitudeWriteStatistics(
    int ChangedIndependentBins,
    int ChangedTotalBins,
    double SourceEnergy,
    double ResultEnergy,
    double MaximumPhaseDeviation,
    double MaximumConjugateResidual,
    double MinimumAppliedScale,
    double MaximumAppliedScale);

/// <summary>只修改调用方工作副本的幅度，并成对提交中心共轭系数。</summary>
/// <remarks>
/// 每个主频点先与共轭点取共同幅度 Mpair，再在 L=log(1+Mpair²) 上增加
/// strength×patternWeight×max(1.4826×MAD,0.15)。非零系数沿用规范代表的相位；若一对系数都为零，
/// V1 固定使用零相位。主点写入后立即把其复共轭提交到副本点，因而实值 IFFT 的不变量不是事后修补。
/// 本服务不会创建或保存第二个完整工作数组；源 FrequencySpectrum 始终保持只读。
/// </remarks>
internal sealed class SpectralAmplitudeWriter(RadialLogPowerBaseline radialBaseline)
{
    public const double MinimumRobustScale = 0.15d;
    public const double MaximumStrength = 8d;
    private const double PhaseEpsilon = 1e-12d;
    private static readonly double MaximumLogPower = Math.Log(double.MaxValue);

    public SpectralAmplitudeWriteStatistics ApplyInPlace(
        FrequencySpectrum source,
        Complex[] ownedWorkingSpectrum,
        SpectralPatternMapping mapping,
        double strength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ownedWorkingSpectrum);
        ArgumentNullException.ThrowIfNull(mapping);
        if (ownedWorkingSpectrum.Length != source.ValueCount ||
            mapping.SpectrumWidth != source.PaddedWidth || mapping.SpectrumHeight != source.PaddedHeight)
            throw new ArgumentException("工作频谱、映射与源频谱尺寸必须一致。");
        if (!double.IsFinite(strength) || strength is < 0d or > MaximumStrength)
            throw new ArgumentOutOfRangeException(nameof(strength), "强度必须是 [0,8] 内的有限值。");

        var sourceEnergy = TotalEnergy(source.Values.Span, cancellationToken);
        if (strength == 0d)
            return new SpectralAmplitudeWriteStatistics(0, 0, sourceEnergy, sourceEnergy,
                0d, 0d, 0d, 0d);

        var baseline = radialBaseline.Analyze(source, cancellationToken);
        var sourceValues = source.Values.Span;
        var weights = mapping.MainWeightSpan;
        var changed = 0;
        var minimumScale = double.PositiveInfinity;
        var maximumScale = 0d;
        double maximumPhaseDeviation = 0d;
        double maximumConjugateResidual = 0d;

        for (var localY = 0; localY < mapping.Height; localY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var displayY = mapping.Top + localY;
            for (var localX = 0; localX < mapping.Width; localX++)
            {
                var weight = weights[(localY * mapping.Width) + localX];
                if (weight <= 0d) continue;
                var displayX = mapping.Left + localX;
                var point = FrequencyCoordinates.FromDisplay(displayX, displayY,
                    source.PaddedWidth, source.PaddedHeight);
                var conjugate = FrequencyCoordinates.ConjugateIndex(point.InternalX, point.InternalY,
                    source.PaddedWidth, source.PaddedHeight);
                var index = (point.InternalY * source.PaddedWidth) + point.InternalX;
                var conjugateIndex = (conjugate.Y * source.PaddedWidth) + conjugate.X;
                var canonical = sourceValues[index];
                var mirror = sourceValues[conjugateIndex];
                var pairMagnitude = (canonical.Magnitude + mirror.Magnitude) * 0.5d;
                var logPower = Math.Log(1d + (pairMagnitude * pairMagnitude));
                var radialBin = RadialLogPowerBaseline.RadialBin(point.InternalX, point.InternalY,
                    source.PaddedWidth, source.PaddedHeight);
                var scale = Math.Max(MinimumRobustScale,
                    RadialLogPowerBaseline.RobustScale * baseline.MedianAbsoluteDeviations[radialBin]);
                var targetLogPower = logPower + (strength * weight * scale);
                if (!double.IsFinite(targetLogPower) || targetLogPower >= MaximumLogPower)
                    throw new InvalidDataException("幅度指数超出有限值范围，未提交半成品。");
                var targetMagnitude = Math.Sqrt(Math.Exp(targetLogPower) - 1d);
                if (!double.IsFinite(targetMagnitude))
                    throw new InvalidDataException("目标幅度出现非有限值，未提交半成品。");

                var phase = canonical.Magnitude > PhaseEpsilon
                    ? canonical.Phase
                    : mirror.Magnitude > PhaseEpsilon ? -mirror.Phase : 0d;
                var written = Complex.FromPolarCoordinates(targetMagnitude, phase);
                ownedWorkingSpectrum[index] = written;
                ownedWorkingSpectrum[conjugateIndex] = Complex.Conjugate(written);
                changed++;
                minimumScale = Math.Min(minimumScale, scale);
                maximumScale = Math.Max(maximumScale, scale);
                if (canonical.Magnitude > PhaseEpsilon)
                    maximumPhaseDeviation = Math.Max(maximumPhaseDeviation,
                        WrappedPhaseDistance(canonical.Phase, written.Phase));
                maximumConjugateResidual = Math.Max(maximumConjugateResidual,
                    Complex.Abs(ownedWorkingSpectrum[conjugateIndex] -
                                Complex.Conjugate(ownedWorkingSpectrum[index])));
            }
        }

        var resultEnergy = TotalEnergy(ownedWorkingSpectrum, cancellationToken);
        return new SpectralAmplitudeWriteStatistics(changed, checked(changed * 2), sourceEnergy,
            resultEnergy, maximumPhaseDeviation, maximumConjugateResidual,
            double.IsPositiveInfinity(minimumScale) ? 0d : minimumScale, maximumScale);
    }

    private static double TotalEnergy(ReadOnlySpan<Complex> values, CancellationToken cancellationToken)
    {
        double total = 0d;
        for (var i = 0; i < values.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            total += (values[i].Real * values[i].Real) + (values[i].Imaginary * values[i].Imaginary);
        }
        if (!double.IsFinite(total)) throw new InvalidDataException("频谱总能量出现非有限值。");
        return total;
    }

    private static double WrappedPhaseDistance(double first, double second)
    {
        var delta = Math.Abs(first - second) % (2d * Math.PI);
        return Math.Min(delta, (2d * Math.PI) - delta);
    }
}
