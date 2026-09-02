using System.Diagnostics;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.SvdDecomposition;

/// <summary>抽取三种固定颜色策略所需的矩阵，并协调逐通道分解。</summary>
/// <remarks>
/// V1 的策略集合是封闭且很小的，所以这里使用一个完整 switch，而不是接口工厂或反射目录。Cb/Cr 在进入
/// SVD 前减 128，避免色度中性偏置独占一个大奇异值；重建合成时只加回一次同一个 neutral。
/// </remarks>
internal sealed class SvdColorStrategyExecutor(
    ImageChannelConverter channelConverter,
    JacobiSvdDecomposer decomposer)
{
    public SvdDecompositionSet Decompose(PixelImage image, string proxyFingerprint,
        SvdColorStrategy strategy, ImageChannel singleChannel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyFingerprint);
        var channels = strategy switch
        {
            SvdColorStrategy.SingleChannel => new[] { singleChannel },
            SvdColorStrategy.IndependentRgb => new[] { ImageChannel.Red, ImageChannel.Green, ImageChannel.Blue },
            SvdColorStrategy.IndependentYCbCr => new[] { ImageChannel.Luma, ImageChannel.ChromaBlue, ImageChannel.ChromaRed },
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "未知 SVD 颜色策略。")
        };
        var stopwatch = Stopwatch.StartNew();
        var factors = new List<SvdChannelFactors>(channels.Length);
        foreach (var channel in channels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plane = channelConverter.Extract(image, channel, cancellationToken);
            var neutral = ImageChannelConverter.NeutralValue(channel);
            var centered = plane.Values.ToArray();
            if (neutral != 0d)
                for (var index = 0; index < centered.Length; index++) centered[index] -= neutral;
            var matrix = new DenseMatrix(image.Size.Height, image.Size.Width, centered);
            factors.Add(new(channel, neutral, matrix, decomposer.Decompose(matrix, cancellationToken)));
        }
        return new(strategy, singleChannel, proxyFingerprint, factors, stopwatch.Elapsed);
    }
}

internal sealed record SvdImageReconstruction(
    PixelImage Image,
    SvdClippingDiagnostics Clipping);

/// <summary>把一组 Rank-k raw 矩阵一次性合成为 RGBA 图片。</summary>
/// <remarks>
/// 单通道委托现有 ImageChannelConverter；RGB 与 YCbCr 都在一次像素循环中组合，避免按通道反复回写产生
/// 顺序相关舍入。只有此图片投影边界使用 AwayFromZero 舍入和 [0,255] 裁切，double 矩阵本身不裁切。
/// </remarks>
internal sealed class SvdImageReconstructor(ImageChannelConverter channelConverter)
{
    public SvdImageReconstruction Reconstruct(PixelImage source, SvdDecompositionSet decomposition,
        IReadOnlyList<DenseMatrix> matrices, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(decomposition);
        if (matrices.Count != decomposition.Channels.Count) throw new ArgumentException("重建矩阵数量与颜色策略不一致。", nameof(matrices));
        return decomposition.Strategy switch
        {
            SvdColorStrategy.SingleChannel => ReconstructSingle(source, decomposition.Channels[0], matrices[0]),
            SvdColorStrategy.IndependentRgb => ReconstructRgb(source, decomposition.Channels, matrices, cancellationToken),
            SvdColorStrategy.IndependentYCbCr => ReconstructYCbCr(source, decomposition.Channels, matrices, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(decomposition))
        };
    }

    private SvdImageReconstruction ReconstructSingle(PixelImage source, SvdChannelFactors channel, DenseMatrix matrix)
    {
        var values = matrix.Values.ToArray();
        if (channel.Neutral != 0d)
            for (var index = 0; index < values.Length; index++) values[index] += channel.Neutral;
        var plane = new ImageChannelPlane(source.Size, channel.Channel, values);
        var result = channelConverter.Apply(source, plane, MidpointRounding.AwayFromZero);
        return new(result.Image, new(result.ClippedPixelCount, result.ClippedComponentCount));
    }

    private static SvdImageReconstruction ReconstructRgb(PixelImage source,
        IReadOnlyList<SvdChannelFactors> channels, IReadOnlyList<DenseMatrix> matrices, CancellationToken token)
    {
        EnsureChannels(channels, [ImageChannel.Red, ImageChannel.Green, ImageChannel.Blue]);
        var image = source.Clone();
        var clippedPixels = 0;
        var clippedComponents = 0;
        for (var y = 0; y < source.Size.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var index = (y * source.Size.Width) + x;
                var red = Quantize(matrices[0].Values.Span[index], out var redClipped);
                var green = Quantize(matrices[1].Values.Span[index], out var greenClipped);
                var blue = Quantize(matrices[2].Values.Span[index], out var blueClipped);
                clippedComponents += (redClipped ? 1 : 0) + (greenClipped ? 1 : 0) + (blueClipped ? 1 : 0);
                if (redClipped || greenClipped || blueClipped) clippedPixels++;
                image.SetRgb(x, y, red, green, blue);
            }
        }
        return new(image, new(clippedPixels, clippedComponents));
    }

    private static SvdImageReconstruction ReconstructYCbCr(PixelImage source,
        IReadOnlyList<SvdChannelFactors> channels, IReadOnlyList<DenseMatrix> matrices, CancellationToken token)
    {
        EnsureChannels(channels, [ImageChannel.Luma, ImageChannel.ChromaBlue, ImageChannel.ChromaRed]);
        var image = source.Clone();
        var clippedPixels = 0;
        var clippedComponents = 0;
        for (var y = 0; y < source.Size.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var index = (y * source.Size.Width) + x;
                // Cb/Cr 在矩阵中已经中心化；这里只各加回一次 128，再交给共享 BT.601 逆变换。
                var rgb = YCbCrColorSpace.ToRgb(
                    matrices[0].Values.Span[index],
                    matrices[1].Values.Span[index] + channels[1].Neutral,
                    matrices[2].Values.Span[index] + channels[2].Neutral);
                var red = Quantize(rgb.Red, out var redClipped);
                var green = Quantize(rgb.Green, out var greenClipped);
                var blue = Quantize(rgb.Blue, out var blueClipped);
                clippedComponents += (redClipped ? 1 : 0) + (greenClipped ? 1 : 0) + (blueClipped ? 1 : 0);
                if (redClipped || greenClipped || blueClipped) clippedPixels++;
                image.SetRgb(x, y, red, green, blue);
            }
        }
        return new(image, new(clippedPixels, clippedComponents));
    }

    private static void EnsureChannels(IReadOnlyList<SvdChannelFactors> actual, IReadOnlyList<ImageChannel> expected)
    {
        if (actual.Count != expected.Count || actual.Where((item, index) => item.Channel != expected[index]).Any())
            throw new InvalidOperationException("颜色策略通道顺序不完整或不确定。");
    }

    private static byte Quantize(double value, out bool clipped)
    {
        clipped = value < 0d || value > 255d;
        return (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
    }
}

/// <summary>计算理论尾能量、直接矩阵残差与最终 8 位图片质量。</summary>
/// <remarks>
/// 理论尾能量验证奇异值协议，直接残差验证实际 Rank-k 循环；最终 PSNR/SSIM 则包含颜色转换、舍入和裁切。
/// 三者回答不同问题，不能用任一项替代另外两项，也不会据此自动选择“最佳 k”。
/// </remarks>
internal sealed class SvdReconstructionAnalyzer(
    SingularValueEnergyAnalyzer energyAnalyzer,
    FullReferenceQualityAnalyzer qualityAnalyzer)
{
    public (IReadOnlyList<SvdMatrixError> Errors, FullReferenceQualityMetrics Quality, double? AggregateEnergy) Analyze(
        PixelImage source, PixelImage reconstructed, SvdDecompositionSet decomposition,
        IReadOnlyList<DenseMatrix> matrices, int rank, CancellationToken cancellationToken = default)
    {
        var errors = new List<SvdMatrixError>(matrices.Count);
        double aggregateTotal = 0d, aggregateTail = 0d;
        for (var matrixIndex = 0; matrixIndex < matrices.Count; matrixIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceMatrix = decomposition.Channels[matrixIndex].SourceMatrix;
            var factors = decomposition.Channels[matrixIndex].Factors;
            var result = matrices[matrixIndex];
            var theoreticalSquared = energyAnalyzer.TailEnergy(factors, rank);
            double residualScale = 0d, residualSum = 1d, sourceScale = 0d, sourceSum = 1d;
            var minimum = double.PositiveInfinity;
            var maximum = double.NegativeInfinity;
            for (var index = 0; index < result.Values.Length; index++)
            {
                var value = result.Values.Span[index];
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
                JacobiSvdDecomposer.AddScaledSquare(sourceMatrix.Values.Span[index] - value, ref residualScale, ref residualSum);
                JacobiSvdDecomposer.AddScaledSquare(sourceMatrix.Values.Span[index], ref sourceScale, ref sourceSum);
            }
            var direct = residualScale == 0d ? 0d : residualScale * Math.Sqrt(residualSum);
            var sourceNorm = sourceScale == 0d ? 0d : sourceScale * Math.Sqrt(sourceSum);
            var directSquared = direct * direct;
            var allowed = Math.Max(1e-8, theoreticalSquared * 2e-7);
            if (Math.Abs(theoreticalSquared - directSquared) > allowed)
                throw new InvalidOperationException("理论尾能量与直接 Rank-k 残差不一致，已阻断结果提交。");
            var total = energyAnalyzer.Analyze(factors).TotalEnergy;
            aggregateTotal += total;
            aggregateTail += theoreticalSquared;
            errors.Add(new(decomposition.Channels[matrixIndex].Channel, Math.Sqrt(Math.Max(0d, theoreticalSquared)), direct,
                sourceNorm == 0d ? 0d : direct / sourceNorm, total == 0d ? null : Math.Clamp(1d - (theoreticalSquared / total), 0d, 1d), minimum, maximum));
        }
        return (errors, qualityAnalyzer.Analyze(source, reconstructed, cancellationToken),
            aggregateTotal == 0d ? null : Math.Clamp(1d - (aggregateTail / aggregateTotal), 0d, 1d));
    }
}
