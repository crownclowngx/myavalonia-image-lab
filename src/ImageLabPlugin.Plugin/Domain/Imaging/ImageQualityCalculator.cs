namespace ImageLabPlugin.Domain.Imaging;

internal readonly record struct ImageQualityMetrics(double Psnr, double Ssim);

/// <summary>计算原图与输出图之间的客观质量指标。</summary>
internal static class ImageQualityCalculator
{
    public static ImageQualityMetrics Compare(PixelImage original, PixelImage modified)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(modified);
        if (original.Size != modified.Size)
        {
            throw new ArgumentException("质量比较要求两张图片尺寸一致。", nameof(modified));
        }

        var first = ColorSpaceConverter.ExtractLuma(original);
        var second = ColorSpaceConverter.ExtractLuma(modified);
        var count = original.Size.PixelCount;
        double squaredError = 0;
        double meanFirst = 0;
        double meanSecond = 0;

        for (var y = 0; y < original.Size.Height; y++)
        {
            for (var x = 0; x < original.Size.Width; x++)
            {
                var a = first[x, y];
                var b = second[x, y];
                var delta = a - b;
                squaredError += delta * delta;
                meanFirst += a;
                meanSecond += b;
            }
        }

        var mse = squaredError / count;
        var psnr = mse == 0 ? double.PositiveInfinity : 10d * Math.Log10((255d * 255d) / mse);
        meanFirst /= count;
        meanSecond /= count;

        double varianceFirst = 0;
        double varianceSecond = 0;
        double covariance = 0;
        for (var y = 0; y < original.Size.Height; y++)
        {
            for (var x = 0; x < original.Size.Width; x++)
            {
                var a = first[x, y] - meanFirst;
                var b = second[x, y] - meanSecond;
                varianceFirst += a * a;
                varianceSecond += b * b;
                covariance += a * b;
            }
        }

        var denominator = Math.Max(1d, count - 1d);
        varianceFirst /= denominator;
        varianceSecond /= denominator;
        covariance /= denominator;
        var c1 = Math.Pow(0.01d * 255d, 2d);
        var c2 = Math.Pow(0.03d * 255d, 2d);
        var ssim = ((2d * meanFirst * meanSecond + c1) * (2d * covariance + c2)) /
            ((meanFirst * meanFirst + meanSecond * meanSecond + c1) *
             (varianceFirst + varianceSecond + c2));
        return new ImageQualityMetrics(psnr, Math.Clamp(ssim, -1d, 1d));
    }
}
