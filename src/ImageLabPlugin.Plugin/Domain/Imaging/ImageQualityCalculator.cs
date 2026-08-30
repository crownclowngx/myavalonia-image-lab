using ImageLabPlugin.Domain.Comparison;

namespace ImageLabPlugin.Domain.Imaging;

internal readonly record struct ImageQualityMetrics(double Psnr, double Ssim);

/// <summary>计算原图与输出图之间的客观质量指标。</summary>
internal static class ImageQualityCalculator
{
    public static ImageQualityMetrics Compare(PixelImage original, PixelImage modified)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(modified);
        // 兼容入口只映射既有 Y-PSNR 与全局 Y-SSIM；实际计算统一走 O(1) 额外内存的新分析器。
        var metrics = new FullReferenceQualityAnalyzer(new ImagePairValidator()).Analyze(original, modified);
        return new ImageQualityMetrics(metrics.PsnrLumaDb, metrics.GlobalSsimLuma);
    }
}
