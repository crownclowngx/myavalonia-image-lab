using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>冻结同尺寸比较的数值、Alpha、符号与常量内存语义。</summary>
public sealed class ImageComparisonQualityTests
{
    private readonly ImagePairValidator _validator = new();

    [Fact]
    public void 完全一致图片得到无穷PSNR与一的全局Ssim()
    {
        var image = Image(2, 2, [10, 20, 30, 255, 40, 50, 60, 200, 70, 80, 90, 0, 1, 2, 3, 4]);
        var metrics = new FullReferenceQualityAnalyzer(_validator).Analyze(image, image.Clone());

        Assert.True(double.IsPositiveInfinity(metrics.PsnrLumaDb));
        Assert.True(double.IsPositiveInfinity(metrics.PsnrRgbDb));
        Assert.Equal(1d, metrics.GlobalSsimLuma, 12);
        Assert.Equal(0d, metrics.MeanAbsoluteErrorRgb);
        Assert.Equal(0, metrics.ChangedPixelCountRgb);
        Assert.Equal(0, metrics.ChangedPixelCountAlpha);
    }

    [Fact]
    public void 单像素红色最大变化符合三样本Rgb公式()
    {
        var reference = Image(1, 1, [0, 0, 0, 255]);
        var candidate = Image(1, 1, [255, 0, 0, 255]);
        var metrics = new FullReferenceQualityAnalyzer(_validator).Analyze(reference, candidate);

        Assert.Equal(255d * 255d / 3d, metrics.MeanSquaredErrorRgb, 8);
        Assert.Equal(85d, metrics.MeanAbsoluteErrorRgb, 8);
        Assert.Equal(255, metrics.MaximumAbsoluteErrorRgb);
        Assert.Equal(1, metrics.ChangedPixelCountRgb);
        Assert.Equal(1d, metrics.ChangedPixelRatioRgb);
    }

    [Fact]
    public void 仅Alpha变化不影响颜色指标但单独准确报告()
    {
        var reference = Image(1, 1, [10, 20, 30, 0]);
        var candidate = Image(1, 1, [10, 20, 30, 255]);
        var metrics = new FullReferenceQualityAnalyzer(_validator).Analyze(reference, candidate);

        Assert.True(double.IsPositiveInfinity(metrics.PsnrRgbDb));
        Assert.Equal(0, metrics.ChangedPixelCountRgb);
        Assert.Equal(255d, metrics.MeanAbsoluteErrorAlpha);
        Assert.Equal(255, metrics.MaximumAbsoluteErrorAlpha);
        Assert.Equal(1, metrics.ChangedPixelCountAlpha);
    }

    [Fact]
    public void 完全透明像素中的Rgb仍参与颜色指标()
    {
        var reference = Image(1, 1, [0, 0, 0, 0]);
        var candidate = Image(1, 1, [1, 2, 3, 0]);
        var metrics = new FullReferenceQualityAnalyzer(_validator).Analyze(reference, candidate);

        Assert.Equal(1, metrics.ChangedPixelCountRgb);
        Assert.False(double.IsPositiveInfinity(metrics.PsnrRgbDb));
        Assert.Equal(0, metrics.ChangedPixelCountAlpha);
    }

    [Fact]
    public void 像素报告变化符号固定为待比较减参考()
    {
        var report = new ImagePairPixelInspector(_validator).Inspect(
            Image(1, 1, [20, 30, 40, 50]), Image(1, 1, [10, 50, 35, 70]), new ImagePoint(0, 0));

        Assert.Equal(-10, report.DeltaRed);
        Assert.Equal(20, report.DeltaGreen);
        Assert.Equal(-5, report.DeltaBlue);
        Assert.Equal(20, report.DeltaAlpha);
        Assert.Equal(20, report.MaximumRgbDifference);
    }

    [Fact]
    public void 尺寸不匹配返回结构化宽高差且阻断分析()
    {
        var reference = Image(2, 1, [0, 0, 0, 255, 0, 0, 0, 255]);
        var candidate = Image(1, 2, [0, 0, 0, 255, 0, 0, 0, 255]);
        var mismatch = _validator.Validate(reference, candidate)!;

        Assert.Equal(-1, mismatch.WidthDifference);
        Assert.Equal(1, mismatch.HeightDifference);
        Assert.Contains("没有生成可比较指标", mismatch.ToUserMessage(), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new FullReferenceQualityAnalyzer(_validator).Analyze(reference, candidate));
    }

    [Fact]
    public void 取消不会返回半成品()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        var image = Image(1, 1, [1, 2, 3, 4]);
        Assert.Throws<OperationCanceledException>(() => new FullReferenceQualityAnalyzer(_validator).Analyze(image, image.Clone(), source.Token));
    }

    private static PixelImage Image(int width, int height, byte[] rgba) => new(new ImageSize(width, height), rgba);
}
