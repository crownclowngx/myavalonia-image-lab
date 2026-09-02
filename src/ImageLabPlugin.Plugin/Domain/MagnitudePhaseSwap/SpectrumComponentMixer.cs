using System.Numerics;
using ImageLabPlugin.Domain.Frequency;

namespace ImageLabPlugin.Domain.MagnitudePhaseSwap;

/// <summary>描述一次共轭安全分量组合的频域诊断。</summary>
internal sealed record SpectrumMixDiagnostics(long UndefinedPhaseCount, double BorrowedPhaseEnergyRatio,
    long PiAmbiguityCount, long SelfConjugateZeroCrossingCount, double MaximumConjugateError,
    double RelativeMagnitudeError, double WeightedPhaseErrorRadians);

/// <summary>拥有将被 IFFT 原地消费的唯一工作频谱。</summary>
internal sealed record SpectrumMixResult(Complex[] OwnedSpectrum, SpectrumMixDiagnostics Diagnostics);

internal sealed record MagnitudePhaseFrequencyProbe(int DisplayX, int DisplayY, int InternalX, int InternalY,
    int CenteredKx, int CenteredKy, bool IsSelfConjugate, double MagnitudeA, double? PhaseA,
    double MagnitudeB, double? PhaseB, double ResultMagnitude, double? ResultPhase);

/// <summary>按配方组合两张同形频谱，并显式维持实值图像所需的共轭不变量。</summary>
/// <remarks>
/// FFT 使用未中心化工作坐标，共轭点为 <c>((N-u)%N,(N-v)%N)</c>。每对只处理行主序较小代表，
/// 另一项精确写为共轭；DC/Nyquist 等自共轭点只允许实数。相位供体低于各自
/// <c>max(1E-12,maxMagnitude*1E-12)</c> 时以稳定 0 占位，并单独记录借用数量与能量，绝不伪装成已定义 0°。
/// </remarks>
internal sealed class SpectrumComponentMixer
{
    public MagnitudePhaseFrequencyProbe Inspect(FrequencySpectrum sourceA, FrequencySpectrum sourceB,
        MagnitudePhaseRecipe recipe, int displayX, int displayY, double thresholdA, double thresholdB)
    {
        ArgumentNullException.ThrowIfNull(sourceA); ArgumentNullException.ThrowIfNull(sourceB); ArgumentNullException.ThrowIfNull(recipe);
        var size = recipe.CanvasSize;
        if ((uint)displayX >= (uint)size || (uint)displayY >= (uint)size) throw new ArgumentOutOfRangeException(nameof(displayX));
        var internalX = (displayX + (size / 2)) % size; var internalY = (displayY + (size / 2)) % size;
        var index = (internalY * size) + internalX;
        var conjugate = (((size - internalY) % size) * size) + ((size - internalX) % size);
        var representative = Math.Min(index, conjugate);
        var a = sourceA.Values.Span; var b = sourceB.Values.Span;
        if (!double.IsFinite(thresholdA) || thresholdA <= 0d || !double.IsFinite(thresholdB) || thresholdB <= 0d)
            throw new ArgumentOutOfRangeException(nameof(thresholdA));
        var magnitude = SelectMagnitude(a[representative].Magnitude, b[representative].Magnitude,
            thresholdA, thresholdB, recipe);
        var phase = SelectPhase(a[representative], b[representative], thresholdA, thresholdB, recipe).Phase;
        Complex result;
        if (representative == conjugate) result = new Complex(magnitude * (Math.Cos(phase) < 0d ? -1d : 1d), 0d);
        else
        {
            result = Complex.FromPolarCoordinates(magnitude, phase);
            if (index != representative) result = Complex.Conjugate(result);
        }
        return new MagnitudePhaseFrequencyProbe(displayX, displayY, internalX, internalY,
            internalX <= size / 2 ? internalX : internalX - size,
            internalY <= size / 2 ? internalY : internalY - size, index == conjugate,
            a[index].Magnitude, a[index].Magnitude > thresholdA ? a[index].Phase : null,
            b[index].Magnitude, b[index].Magnitude > thresholdB ? b[index].Phase : null,
            result.Magnitude, result.Magnitude > 0d ? result.Phase : null);
    }

    public SpectrumMixResult Mix(FrequencySpectrum sourceA, FrequencySpectrum sourceB,
        MagnitudePhaseRecipe recipe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceA);
        ArgumentNullException.ThrowIfNull(sourceB);
        ArgumentNullException.ThrowIfNull(recipe);
        if (sourceA.PaddedWidth != recipe.CanvasSize || sourceA.PaddedHeight != recipe.CanvasSize ||
            sourceB.PaddedWidth != recipe.CanvasSize || sourceB.PaddedHeight != recipe.CanvasSize)
            throw new ArgumentException("A/B 频谱必须与配方规范画布同形。");
        var a = sourceA.Values.Span;
        var b = sourceB.Values.Span;
        var result = new Complex[a.Length];
        var thresholdA = Threshold(a);
        var thresholdB = Threshold(b);
        long undefined = 0, piAmbiguities = 0, selfCrossings = 0;
        double borrowedEnergy = 0d, totalEnergy = 0d;
        for (var index = 0; index < result.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var x = index % recipe.CanvasSize;
            var y = index / recipe.CanvasSize;
            var conjugate = (((recipe.CanvasSize - y) % recipe.CanvasSize) * recipe.CanvasSize) +
                            ((recipe.CanvasSize - x) % recipe.CanvasSize);
            if (index > conjugate) continue;
            var magnitude = SelectMagnitude(a[index].Magnitude, b[index].Magnitude,
                thresholdA, thresholdB, recipe);
            var phaseInfo = SelectPhase(a[index], b[index], thresholdA, thresholdB, recipe);
            var pairCount = index == conjugate ? 1 : 2;
            undefined += phaseInfo.Undefined ? pairCount : 0;
            piAmbiguities += phaseInfo.PiAmbiguous ? pairCount : 0;
            var energy = magnitude * magnitude;
            totalEnergy += energy * (index == conjugate ? 1d : 2d);
            if (phaseInfo.Undefined && magnitude > 0d) borrowedEnergy += energy * (index == conjugate ? 1d : 2d);
            if (index == conjugate)
            {
                // 自共轭频点只能落在实轴。插值路径若跨过 ±π/2，就以确定性符号翻转表达过零。
                var real = magnitude * (Math.Cos(phaseInfo.Phase) < 0d ? -1d : 1d);
                if (magnitude > 0d && Math.Abs(Math.Cos(phaseInfo.Phase)) < 1e-12) { real = 0d; selfCrossings++; }
                result[index] = new Complex(real, 0d);
            }
            else
            {
                result[index] = Complex.FromPolarCoordinates(magnitude, phaseInfo.Phase);
                result[conjugate] = Complex.Conjugate(result[index]);
            }
        }
        var maximumConjugateError = ValidateConjugate(result, recipe.CanvasSize, cancellationToken);
        var diagnostics = new SpectrumMixDiagnostics(undefined,
            totalEnergy <= 0d ? 0d : borrowedEnergy / totalEnergy, piAmbiguities, selfCrossings,
            maximumConjugateError, RelativeMagnitudeError(result, a, b, recipe),
            WeightedPhaseError(result, a, b, thresholdA, thresholdB, recipe));
        return new SpectrumMixResult(result, diagnostics);
    }

    internal static double InterpolatePhase(double phaseA, double phaseB, double amount, out bool piAmbiguous)
    {
        var delta = WrapToPi(phaseB - phaseA);
        piAmbiguous = Math.Abs(Math.Abs(delta) - Math.PI) <= 1e-12;
        // π 有两条等长圆弧；固定选择正向 +π，保证平台、扫描顺序和端点测试一致。
        if (piAmbiguous) delta = Math.PI;
        return WrapToPi(phaseA + (amount * delta));
    }

    internal static double WrapToPi(double value)
    {
        var wrapped = value - (2d * Math.PI * Math.Floor((value + Math.PI) / (2d * Math.PI)));
        return wrapped <= -Math.PI ? Math.PI : wrapped;
    }

    private static double SelectMagnitude(double a, double b, double thresholdA, double thresholdB,
        MagnitudePhaseRecipe recipe) => recipe.MagnitudeMode switch
    {
        MagnitudeComponentMode.SourceA => a,
        MagnitudeComponentMode.SourceB => b,
        MagnitudeComponentMode.LinearAtoB => a + ((b - a) * recipe.MagnitudeAmount),
        MagnitudeComponentMode.UnitNonZero => recipe.PhaseMode switch
        {
            PhaseComponentMode.SourceA => a > thresholdA ? 1d : 0d,
            PhaseComponentMode.SourceB => b > thresholdB ? 1d : 0d,
            _ => 0d
        },
        _ => throw new ArgumentOutOfRangeException()
    };

    private static (double Phase, bool Undefined, bool PiAmbiguous) SelectPhase(Complex a, Complex b,
        double thresholdA, double thresholdB, MagnitudePhaseRecipe recipe)
    {
        var definedA = a.Magnitude > thresholdA;
        var definedB = b.Magnitude > thresholdB;
        return recipe.PhaseMode switch
        {
            PhaseComponentMode.SourceA => (definedA ? a.Phase : 0d, !definedA, false),
            PhaseComponentMode.SourceB => (definedB ? b.Phase : 0d, !definedB, false),
            PhaseComponentMode.Zero => (0d, false, false),
            PhaseComponentMode.ShortestArcAtoB => Interpolated(definedA ? a.Phase : 0d,
                definedB ? b.Phase : 0d, recipe.PhaseAmount, !definedA || !definedB),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static (double, bool, bool) Interpolated(double a, double b, double amount, bool undefined)
    {
        var phase = InterpolatePhase(a, b, amount, out var ambiguity);
        return (phase, undefined, ambiguity);
    }

    private static double Threshold(ReadOnlySpan<Complex> values)
    {
        double maximum = 0d;
        foreach (var value in values) maximum = Math.Max(maximum, value.Magnitude);
        return Math.Max(1e-12, maximum * 1e-12);
    }

    private static double ValidateConjugate(Complex[] values, int size, CancellationToken cancellationToken)
    {
        double maximum = 0d;
        for (var y = 0; y < size; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < size; x++)
            {
                var other = values[(((size - y) % size) * size) + ((size - x) % size)];
                maximum = Math.Max(maximum, (values[(y * size) + x] - Complex.Conjugate(other)).Magnitude);
            }
        }
        if (maximum > 1e-9) throw new InvalidDataException($"组合频谱共轭误差 {maximum:E3} 超出 1E-9 门禁。");
        return maximum;
    }

    private static double RelativeMagnitudeError(Complex[] result, ReadOnlySpan<Complex> a,
        ReadOnlySpan<Complex> b, MagnitudePhaseRecipe recipe)
    {
        var expected = recipe.MagnitudeMode == MagnitudeComponentMode.SourceB ? b : a;
        if (recipe.MagnitudeMode is MagnitudeComponentMode.LinearAtoB or MagnitudeComponentMode.UnitNonZero) return 0d;
        double error = 0d, norm = 0d;
        for (var i = 0; i < result.Length; i++)
        {
            var delta = result[i].Magnitude - expected[i].Magnitude;
            error += delta * delta;
            norm += expected[i].Magnitude * expected[i].Magnitude;
        }
        return Math.Sqrt(error / Math.Max(1e-30, norm));
    }

    private static double WeightedPhaseError(Complex[] result, ReadOnlySpan<Complex> a, ReadOnlySpan<Complex> b,
        double thresholdA, double thresholdB, MagnitudePhaseRecipe recipe)
    {
        if (recipe.PhaseMode is PhaseComponentMode.Zero or PhaseComponentMode.ShortestArcAtoB) return 0d;
        var expected = recipe.PhaseMode == PhaseComponentMode.SourceB ? b : a;
        var threshold = recipe.PhaseMode == PhaseComponentMode.SourceB ? thresholdB : thresholdA;
        double weighted = 0d, weights = 0d;
        for (var i = 0; i < result.Length; i++)
        {
            if (expected[i].Magnitude <= threshold || result[i].Magnitude <= 0d) continue;
            var weight = result[i].Magnitude;
            weighted += weight * Math.Abs(WrapToPi(result[i].Phase - expected[i].Phase));
            weights += weight;
        }
        return weights <= 0d ? 0d : weighted / weights;
    }
}
