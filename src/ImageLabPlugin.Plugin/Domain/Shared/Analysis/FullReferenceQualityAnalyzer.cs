using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Shared.Analysis;

/// <summary>完整图片的全参考质量与误差统计。</summary>
/// <remarks>
/// Y/RGB 指标不使用 Alpha 加权；完全透明像素中保存的 RGB 字节仍参与计算。Alpha 单独统计，避免“颜色完全一致”
/// 被误解为整张 RGBA 完全一致。PSNR 的单位是 dB，零误差以正无穷表示。
/// </remarks>
internal sealed record FullReferenceQualityMetrics(
    double MeanSquaredErrorLuma,
    double MeanSquaredErrorRgb,
    double PsnrLumaDb,
    double PsnrRgbDb,
    double GlobalSsimLuma,
    double MeanAbsoluteErrorRgb,
    double RootMeanSquareErrorRgb,
    int MaximumAbsoluteErrorRgb,
    long ChangedPixelCountRgb,
    double ChangedPixelRatioRgb,
    double MeanAbsoluteErrorAlpha,
    double RootMeanSquareErrorAlpha,
    int MaximumAbsoluteErrorAlpha,
    long ChangedPixelCountAlpha,
    double ChangedPixelRatioAlpha);

/// <summary>以固定扫描顺序完成质量统计，不创建全尺寸亮度或差异数组。</summary>
/// <remarks>
/// 全局 SSIM-Y 沿用项目既有的全图均值、样本方差与样本协方差语义，并非滑动窗口 SSIM Map 的平均。
/// 在线协方差使用 Welford/Chan 形式：每加入一对样本，先更新均值，再累计二阶中心矩。这样内存为 O(1)，
/// 同时比“平方和减均值平方”更不易发生大数相消。
/// </remarks>
internal sealed class FullReferenceQualityAnalyzer(ImagePairValidator validator)
{
    public FullReferenceQualityMetrics Analyze(
        PixelImage reference,
        PixelImage candidate,
        CancellationToken cancellationToken = default)
    {
        validator.EnsureComparable(reference, candidate);
        var first = reference.Rgba.Span;
        var second = candidate.Rgba.Span;
        long pixelCount = reference.Size.PixelCount;
        double squaredLuma = 0d, squaredRgb = 0d, absoluteRgb = 0d;
        double squaredAlpha = 0d, absoluteAlpha = 0d;
        double meanReference = 0d, meanCandidate = 0d;
        double m2Reference = 0d, m2Candidate = 0d, covariance = 0d;
        long changedRgb = 0, changedAlpha = 0, seen = 0;
        var maximumRgb = 0;
        var maximumAlpha = 0;

        for (var y = 0; y < reference.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowOffset = y * reference.Size.Width * 4;
            for (var x = 0; x < reference.Size.Width; x++)
            {
                var offset = rowOffset + (x * 4);
                var deltaR = second[offset] - first[offset];
                var deltaG = second[offset + 1] - first[offset + 1];
                var deltaB = second[offset + 2] - first[offset + 2];
                var deltaA = second[offset + 3] - first[offset + 3];
                var absR = Math.Abs(deltaR); var absG = Math.Abs(deltaG); var absB = Math.Abs(deltaB);
                var absA = Math.Abs(deltaA);
                squaredRgb += (deltaR * deltaR) + (deltaG * deltaG) + (deltaB * deltaB);
                absoluteRgb += absR + absG + absB;
                squaredAlpha += deltaA * deltaA;
                absoluteAlpha += absA;
                maximumRgb = Math.Max(maximumRgb, Math.Max(absR, Math.Max(absG, absB)));
                maximumAlpha = Math.Max(maximumAlpha, absA);
                if ((deltaR | deltaG | deltaB) != 0) changedRgb++;
                if (deltaA != 0) changedAlpha++;

                var lumaReference = ColorSpaceConverter.ToLuma(first[offset], first[offset + 1], first[offset + 2]);
                var lumaCandidate = ColorSpaceConverter.ToLuma(second[offset], second[offset + 1], second[offset + 2]);
                var lumaDelta = lumaCandidate - lumaReference;
                squaredLuma += lumaDelta * lumaDelta;

                seen++;
                var oldMeanReference = meanReference;
                var referenceDeviation = lumaReference - meanReference;
                meanReference += referenceDeviation / seen;
                var candidateDeviation = lumaCandidate - meanCandidate;
                meanCandidate += candidateDeviation / seen;
                m2Reference += referenceDeviation * (lumaReference - meanReference);
                m2Candidate += candidateDeviation * (lumaCandidate - meanCandidate);
                covariance += (lumaReference - oldMeanReference) * (lumaCandidate - meanCandidate);
            }
        }

        var rgbSamples = 3d * pixelCount;
        var mseLuma = squaredLuma / pixelCount;
        var mseRgb = squaredRgb / rgbSamples;
        var denominator = Math.Max(1d, pixelCount - 1d);
        var varianceReference = m2Reference / denominator;
        var varianceCandidate = m2Candidate / denominator;
        var sampleCovariance = covariance / denominator;
        var c1 = Math.Pow(0.01d * 255d, 2d);
        var c2 = Math.Pow(0.03d * 255d, 2d);
        var ssim = ((2d * meanReference * meanCandidate + c1) * (2d * sampleCovariance + c2)) /
            ((meanReference * meanReference + meanCandidate * meanCandidate + c1) *
             (varianceReference + varianceCandidate + c2));

        return new FullReferenceQualityMetrics(
            mseLuma,
            mseRgb,
            ToPsnr(mseLuma),
            ToPsnr(mseRgb),
            Math.Clamp(ssim, -1d, 1d),
            absoluteRgb / rgbSamples,
            Math.Sqrt(mseRgb),
            maximumRgb,
            changedRgb,
            changedRgb / (double)pixelCount,
            absoluteAlpha / pixelCount,
            Math.Sqrt(squaredAlpha / pixelCount),
            maximumAlpha,
            changedAlpha,
            changedAlpha / (double)pixelCount);
    }

    private static double ToPsnr(double meanSquaredError) => meanSquaredError == 0d
        ? double.PositiveInfinity
        : 10d * Math.Log10((255d * 255d) / meanSquaredError);
}
