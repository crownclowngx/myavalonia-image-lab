using ImageLabPlugin.Domain.ColorTransfer;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class LabStatisticsTransferTests
{
    [Fact]
    public void Strength零逐字节等于目标且不做色域映射()
    {
        var target = ColorTransferTestFactory.Image(2, 1, 10, 20, 30, 128, 70, 80, 90, 0);
        var reference = ColorTransferTestFactory.Image(1, 1, 200, 100, 50, 255);
        var core = ColorTransferTestFactory.Create();
        var targetStats = core.Distributions.Analyze(target, default); var referenceStats = core.Distributions.Analyze(reference, default);
        var result = core.Transfer.Transfer(target, targetStats, referenceStats,
            new ColorTransferRecipe(ColorTransferMode.FullLab, 0), default);
        Assert.Equal(target.Rgba.ToArray(), result.Image.Rgba.ToArray());
        Assert.Equal(0, result.Gamut.ChromaCompressedCount); Assert.Equal(0, result.Difference.ChangedPixelCount);
        Assert.Equal(0d, result.Difference.P50); Assert.NotNull(result.BeforeReferenceCloseness); Assert.NotNull(result.AfterReferenceCloseness);
    }

    [Fact]
    public void 不同尺寸目标参考可迁移且输出尺寸Alpha跟随目标()
    {
        var target = ColorTransferTestFactory.Image(2, 1, 20, 40, 60, 64, 200, 180, 160, 255);
        var reference = ColorTransferTestFactory.Image(1, 2, 255, 0, 0, 255, 255, 255, 0, 128);
        var core = ColorTransferTestFactory.Create();
        var result = core.Transfer.Transfer(target, core.Distributions.Analyze(target, default),
            core.Distributions.Analyze(reference, default), new ColorTransferRecipe(ColorTransferMode.FullLab, 1), default);
        Assert.Equal(target.Size, result.Image.Size); Assert.Equal((byte)64, result.Image.GetAlpha(0, 0));
        Assert.Equal((byte)255, result.Image.GetAlpha(1, 0)); Assert.Equal(target.Rgba.ToArray(), target.Rgba.ToArray());
    }

    [Fact]
    public void 保留目标L模式避免迁移L通道公式()
    {
        var target = ColorTransferTestFactory.Image(1, 1, 90, 90, 90, 255);
        var reference = ColorTransferTestFactory.Image(1, 1, 250, 10, 10, 255);
        var core = ColorTransferTestFactory.Create(); var targetStats = core.Distributions.Analyze(target, default);
        var result = core.Transfer.Transfer(target, targetStats, core.Distributions.Analyze(reference, default),
            new ColorTransferRecipe(ColorTransferMode.PreserveTargetLightness, 1), default);
        Assert.InRange(Math.Abs(result.Distribution.Statistics.MeanLab.L - targetStats.Statistics.MeanLab.L), 0, 1.5);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void 非法迁移强度在领域边界失败(double strength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColorTransferRecipe(ColorTransferMode.FullLab, strength).Validate());
    }
}
