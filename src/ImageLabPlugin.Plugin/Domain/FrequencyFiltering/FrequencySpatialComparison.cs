using System.Diagnostics;
using System.Numerics;
using ImageLabPlugin.Domain.Convolution;
using ImageLabPlugin.Domain.Frequency;

namespace ImageLabPlugin.Domain.FrequencyFiltering;

internal sealed record FrequencyImpulseKernel(ConvolutionKernel Kernel, double SumBeforeCorrection,
    double SumAfterCorrection, double RetainedL1Ratio, double RetainedL2Ratio, double MaximumImaginaryResidual);

/// <summary>由零相位实数遮罩派生周期冲激响应，并截取有限奇数核。</summary>
internal sealed class FrequencyImpulseResponseFactory(Fft2DTransform fft)
{
    public FrequencyImpulseKernel Create(FrequencyFilterMask mask, FrequencyFilterKind kind, int kernelSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (kernelSize is not (7 or 15 or 31)) throw new ArgumentOutOfRangeException(nameof(kernelSize), "空间核仅支持 7、15、31。");
        if (kernelSize > mask.Width || kernelSize > mask.Height) throw new InvalidOperationException("当前 FFT 网格小于所选截断核。");
        var spectrum = new Complex[checked(mask.Width * mask.Height)]; var gains = mask.GainSpan;
        for (var i = 0; i < spectrum.Length; i++) spectrum[i] = new Complex(gains[i], 0d);
        fft.Inverse(spectrum, mask.Width, mask.Height, cancellationToken);
        double maxImaginary = 0d, fullL1 = 0d, fullL2 = 0d;
        foreach (var value in spectrum) { maxImaginary = Math.Max(maxImaginary, Math.Abs(value.Imaginary)); fullL1 += Math.Abs(value.Real); fullL2 += value.Real * value.Real; }
        if (maxImaginary > 1e-8) throw new InvalidDataException($"冲激响应虚部残差 {maxImaginary:E3} 超限。");
        var coefficients = new double[checked(kernelSize * kernelSize)]; var radius = kernelSize / 2;
        double keptL1 = 0d, keptL2 = 0d;
        for (var row = 0; row < kernelSize; row++)
        for (var column = 0; column < kernelSize; column++)
        {
            // IFFT 的空间原点在 (0,0)；循环取模等价于 fftshift 后从图像中心截取窗口。
            var sourceX = (column - radius + mask.Width) % mask.Width;
            var sourceY = (row - radius + mask.Height) % mask.Height;
            var value = spectrum[(sourceY * mask.Width) + sourceX].Real;
            coefficients[(row * kernelSize) + column] = value; keptL1 += Math.Abs(value); keptL2 += value * value;
        }
        var before = coefficients.Sum();
        var target = kind is FrequencyFilterKind.LowPass or FrequencyFilterKind.BandStop ? 1d : 0d;
        // DC 差值只加到中心。高通/带通目标和为 0，绝不能用普通 sum normalization 对零和核做除法。
        coefficients[(radius * kernelSize) + radius] += target - before;
        return new FrequencyImpulseKernel(new ConvolutionKernel(kernelSize, coefficients), before, coefficients.Sum(),
            fullL1 == 0d ? 1d : Math.Clamp(keptL1 / fullL1, 0d, 1d),
            fullL2 == 0d ? 1d : Math.Clamp(keptL2 / fullL2, 0d, 1d), maxImaginary);
    }
}

internal sealed record FrequencySpatialComparison(FrequencyImpulseKernel ImpulseKernel, double MeanAbsoluteError,
    double MaximumAbsoluteError, TimeSpan FrequencyElapsed, TimeSpan SpatialElapsed, int MeasurementCount);

/// <summary>在相同 padded 网格、Wrap 边界和 raw double 层比较频域结果与有限截断核近似。</summary>
internal sealed class FrequencySpatialComparator(FrequencyFilterEngine engine, SpatialConvolver convolver,
    FrequencyImpulseResponseFactory impulseFactory)
{
    private const long MaximumMultiplyAdds = 350_000_000;

    public FrequencySpatialComparison Compare(ReadOnlySpan<double> paddedSource, FrequencySpectrum spectrum,
        FrequencyFilterMask mask, FrequencyFilterKind kind, int kernelSize, CancellationToken cancellationToken = default)
    {
        if (paddedSource.Length != spectrum.ValueCount) throw new ArgumentException("padded 源平面长度错误。", nameof(paddedSource));
        var operations = checked((long)paddedSource.Length * kernelSize * kernelSize);
        if (operations > MaximumMultiplyAdds)
            throw new InvalidOperationException($"空间近似需要约 {operations:N0} 次乘加，超过 {MaximumMultiplyAdds:N0} 的交互预算。");
        var impulse = impulseFactory.Create(mask, kind, kernelSize, cancellationToken);
        var frequencyTimes = new long[3]; var spatialTimes = new long[3]; PaddedFrequencyPlane? frequency = null; RawConvolutionResult? spatial = null;
        // 一次预热不计时，随后三次测量取中位数；只覆盖数学核心，不含解码、Bitmap、UI 或文件 IO。
        _ = engine.ApplyPadded(spectrum, mask, cancellationToken);
        _ = convolver.ConvolveRaw(paddedSource, spectrum.PaddedWidth, spectrum.PaddedHeight, impulse.Kernel,
            new BorderDefinition(BorderMode.Wrap), new KernelNormalizationDefinition(KernelNormalizationMode.None), cancellationToken);
        for (var i = 0; i < 3; i++)
        {
            var watch = Stopwatch.StartNew(); frequency = engine.ApplyPadded(spectrum, mask, cancellationToken); frequencyTimes[i] = watch.ElapsedTicks;
            watch.Restart(); spatial = convolver.ConvolveRaw(paddedSource, spectrum.PaddedWidth, spectrum.PaddedHeight, impulse.Kernel,
                new BorderDefinition(BorderMode.Wrap), new KernelNormalizationDefinition(KernelNormalizationMode.None), cancellationToken); spatialTimes[i] = watch.ElapsedTicks;
        }
        Array.Sort(frequencyTimes); Array.Sort(spatialTimes);
        var f = frequency!.ValueSpan; var s = spatial!.ValueSpan; double sum = 0d, maximum = 0d;
        for (var i = 0; i < f.Length; i++) { var error = Math.Abs(f[i] - s[i]); sum += error; maximum = Math.Max(maximum, error); }
        return new FrequencySpatialComparison(impulse, sum / f.Length, maximum,
            TimeSpan.FromSeconds(frequencyTimes[1] / (double)Stopwatch.Frequency),
            TimeSpan.FromSeconds(spatialTimes[1] / (double)Stopwatch.Frequency), 3);
    }
}
