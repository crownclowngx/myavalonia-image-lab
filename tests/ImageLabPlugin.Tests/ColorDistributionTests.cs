using ImageLabPlugin.Domain.ColorTransfer;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class ColorDistributionTests
{
    [Fact]
    public void Alpha权重透明隐藏Rgb和直方图守恒()
    {
        var image = ColorTransferTestFactory.Image(3, 1,
            255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 0);
        var result = ColorTransferTestFactory.Create().Distributions.Analyze(image, default);
        Assert.Equal(2, result.Statistics.VisiblePixelCount);
        Assert.Equal(1d + (128d / 255d), result.Statistics.EffectiveWeight, 12);
        Assert.Equal(result.Statistics.EffectiveWeight, result.RgbHistogram.Take(256).Sum(), 12);
        Assert.Equal(0d, result.RgbHistogram[512 + 255]);
    }

    [Fact]
    public void 灰阶只进入HueN而不进入H零与Hs平面()
    {
        var image = ColorTransferTestFactory.Image(1, 1, 128, 128, 128, 255);
        var result = ColorTransferTestFactory.Create().Distributions.Analyze(image, default);
        Assert.Equal(1d, result.Statistics.UndefinedHueWeight); Assert.Equal(0d, result.Statistics.DefinedHueWeight);
        Assert.Equal(0d, result.HsvHistogram.Take(180).Sum()); Assert.Equal(0d, result.HueSaturationDensity.Sum());
    }

    [Fact]
    public void JensenShannon相同为零且互斥对称有限()
    {
        Assert.Equal(0d, ColorDistributionAnalyzer.JensenShannonDistance([1d, 2d], [1d, 2d]), 12);
        var first = ColorDistributionAnalyzer.JensenShannonDistance([1d, 0d], [0d, 1d]);
        var second = ColorDistributionAnalyzer.JensenShannonDistance([0d, 1d], [1d, 0d]);
        Assert.Equal(first, second, 12); Assert.Equal(Math.Sqrt(Math.Log(2d)), first, 12);
    }

    [Fact]
    public void 全透明图片返回结构化失败而非NaN()
    {
        var image = ColorTransferTestFactory.Image(1, 1, 77, 88, 99, 0);
        Assert.Throws<InvalidOperationException>(() => ColorTransferTestFactory.Create().Distributions.Analyze(image, default));
    }

    [Fact]
    public void 已取消的完整扫描不返回部分统计()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var image = ColorTransferTestFactory.Image(1, 1, 1, 2, 3, 255);
        Assert.Throws<OperationCanceledException>(() =>
            ColorTransferTestFactory.Create().Distributions.Analyze(image, cancellation.Token));
    }
}
