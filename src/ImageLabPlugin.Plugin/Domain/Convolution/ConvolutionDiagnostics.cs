using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Convolution;

internal sealed record ConvolutionDifferenceResult(
    PixelImage Absolute,
    PixelImage Signed,
    double MeanAbsoluteError,
    double RootMeanSquareError,
    int MaximumAbsoluteDifference,
    long ChangedPixels);

/// <summary>生成同尺寸 RGB 绝对差异与带正负方向的亮度差异。</summary>
internal sealed class ConvolutionDifferenceProjector
{
    public ConvolutionDifferenceResult Project(PixelImage source, PixelImage result, int amplification = 4,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(result);
        if (source.Size != result.Size) throw new ArgumentException("差异两图尺寸必须一致。");
        if (amplification is not (1 or 4 or 16)) throw new ArgumentOutOfRangeException(nameof(amplification));
        var absolute = new byte[checked((int)(source.Size.PixelCount * 4))]; var signed = new byte[absolute.Length];
        var sourceBytes = source.Rgba.Span; var resultBytes = result.Rgba.Span;
        double sumAbsolute = 0, sumSquare = 0; var maximum = 0; long changed = 0;
        for (var index = 0; index < source.Size.PixelCount; index++)
        {
            if ((index & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var offset = checked((int)index * 4); var any = false; double signedLuma = 0;
            for (var channel = 0; channel < 3; channel++)
            {
                var difference = resultBytes[offset + channel] - sourceBytes[offset + channel];
                var magnitude = Math.Abs(difference); any |= magnitude != 0; maximum = Math.Max(maximum, magnitude);
                sumAbsolute += magnitude; sumSquare += difference * difference;
                absolute[offset + channel] = (byte)Math.Min(255, magnitude * amplification);
                signedLuma += difference * (channel == 0 ? 0.299 : channel == 1 ? 0.587 : 0.114);
            }
            if (any) changed++;
            var signedValue = Math.Clamp((int)Math.Round(128 + (signedLuma * amplification)), 0, 255);
            // 中性灰为零；正值增加红色，负值增加蓝色，同时绿色明度变化，避免只靠色相传意。
            var signedDelta = signedValue - 128;
            signed[offset] = (byte)Math.Clamp(128 + Math.Max(0, signedDelta), 0, 255);
            signed[offset + 1] = (byte)Math.Clamp(128 - (Math.Abs(signedDelta) / 2), 0, 255);
            signed[offset + 2] = (byte)Math.Clamp(128 + Math.Max(0, -signedDelta), 0, 255);
            absolute[offset + 3] = signed[offset + 3] = 255;
        }
        var samples = Math.Max(1d, source.Size.PixelCount * 3d);
        return new ConvolutionDifferenceResult(new PixelImage(source.Size, absolute), new PixelImage(source.Size, signed),
            sumAbsolute / samples, Math.Sqrt(sumSquare / samples), maximum, changed);
    }
}

internal sealed record ConvolutionContribution(
    int KernelX, int KernelY, int RequestedX, int RequestedY, int? MappedX, int? MappedY,
    bool IsConstant, double Sample, double Coefficient, double Product);
internal sealed record ConvolutionPixelReport(
    int X, int Y, (byte R, byte G, byte B, byte A) SourcePixel, (byte R, byte G, byte B, byte A) ResultPixel,
    double Accumulator, double Divisor, double DividedValue, double Bias, double BiasedValue, long RoundedValue,
    byte FinalByte, bool LowClipped, bool HighClipped, IReadOnlyList<ConvolutionContribution> Contributions,
    double? SecondaryAccumulator = null, double? SecondaryDividedValue = null, double? Magnitude = null,
    IReadOnlyList<ConvolutionContribution>? SecondaryContributions = null);

/// <summary>按与执行器相同的坐标和行优先顺序复算单个像素贡献。</summary>
internal sealed class ConvolutionPixelInspector
{
    public ConvolutionPixelReport Inspect(PixelImage source, PixelImage result, ReadOnlySpan<double> plane,
        ConvolutionKernel kernel, BorderDefinition border, KernelNormalizationDefinition normalization,
        double bias, int x, int y)
    {
        if ((uint)x >= (uint)source.Size.Width || (uint)y >= (uint)source.Size.Height || source.Size != result.Size)
            throw new ArgumentOutOfRangeException(nameof(x), "探针坐标或图片尺寸无效。");
        if (plane.Length != source.Size.PixelCount) throw new ArgumentException("探针输入平面尺寸不一致。", nameof(plane));
        var contributions = new List<ConvolutionContribution>(); double accumulator = 0;
        for (var row = 0; row < kernel.Size; row++)
            for (var column = 0; column < kernel.Size; column++)
            {
                var coefficient = kernel[row, column]; if (coefficient == 0) continue;
                var kx = column - kernel.Radius; var ky = row - kernel.Radius;
                var requestedX = x - kx; var requestedY = y - ky;
                var mappedX = BorderIndexMapper.Map(requestedX, source.Size.Width, border.Mode);
                var mappedY = BorderIndexMapper.Map(requestedY, source.Size.Height, border.Mode);
                var constant = mappedX.IsConstant || mappedY.IsConstant;
                var sample = constant ? border.ConstantValue : plane[(mappedY.Index * source.Size.Width) + mappedX.Index];
                var product = sample * coefficient; accumulator += product;
                contributions.Add(new(kx, ky, requestedX, requestedY, constant ? null : mappedX.Index,
                    constant ? null : mappedY.Index, constant, sample, coefficient, product));
            }
        var divisor = ConvolutionNormalizer.ResolveDivisor(kernel, normalization); var divided = accumulator / divisor;
        var biased = divided + bias; var rounded = (long)Math.Round(biased, MidpointRounding.AwayFromZero);
        return new(x, y, source.GetPixel(x, y), result.GetPixel(x, y), accumulator, divisor, divided, bias, biased,
            rounded, (byte)Math.Clamp(rounded, 0, 255), rounded < 0, rounded > 255, contributions);
    }

    /// <summary>分别保留 Gx/Gy 的完整贡献，再在除数后组合 Magnitude；偏置只在最后应用一次。</summary>
    public ConvolutionPixelReport InspectGradient(PixelImage source, PixelImage result, ReadOnlySpan<double> plane,
        ConvolutionKernel xKernel, ConvolutionKernel yKernel, BorderDefinition border,
        KernelNormalizationDefinition normalization, double bias, int x, int y)
    {
        var xReport = Inspect(source, result, plane, xKernel, border, normalization, 0, x, y);
        var yReport = Inspect(source, result, plane, yKernel, border, normalization, 0, x, y);
        var magnitude = Math.Sqrt((xReport.DividedValue * xReport.DividedValue) + (yReport.DividedValue * yReport.DividedValue));
        var biased = magnitude + bias; var rounded = (long)Math.Round(biased, MidpointRounding.AwayFromZero);
        return xReport with
        {
            Bias = bias,
            BiasedValue = biased,
            RoundedValue = rounded,
            FinalByte = (byte)Math.Clamp(rounded, 0, 255),
            LowClipped = rounded < 0,
            HighClipped = rounded > 255,
            SecondaryAccumulator = yReport.Accumulator,
            SecondaryDividedValue = yReport.DividedValue,
            Magnitude = magnitude,
            SecondaryContributions = yReport.Contributions
        };
    }
}
