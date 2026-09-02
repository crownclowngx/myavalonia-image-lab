using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Steganography;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class LsbStatisticsTests
{
    [Fact]
    public void GammaQ与高精度参考值一致()
    {
        Assert.Equal(Math.Exp(-1), RegularizedGamma.Upper(1, 1), 12);
        Assert.Equal(0.3173105078629141, RegularizedGamma.Upper(0.5, 0.5), 12);
    }

    [Fact]
    public void 全零与五五开位分布熵符合Golden()
    {
        var cover = Image(2, 2, [0, 0, 0, 0]);
        var balanced = Image(2, 2, [0, 1, 0, 1]);
        var layout = new LsbSlotLayout(cover);
        var recipe = new LsbRecipe(LsbChannelStrategy.Red, 0, LsbPlacementKind.Sequential, 0);

        var result = new LsbStatisticsAnalyzer().Compare(cover, balanced, layout, recipe, [0, 1, 2, 3], LsbStatisticsScope.EligibleImage, CancellationToken.None);

        Assert.Equal(0, result.Cover.Distribution.OneRatio);
        Assert.Equal(0, result.Cover.Distribution.BinaryEntropy);
        Assert.Equal(0.5, result.Stego.Distribution.OneRatio);
        Assert.Equal(1, result.Stego.Distribution.BinaryEntropy);
    }

    [Fact]
    public void 二乘二矩阵水平垂直邻接四格可手算()
    {
        // 0 1 / 1 0：水平与垂直各有一个 01 和一个 10。
        var image = Image(2, 2, [0, 1, 1, 0]);
        var layout = new LsbSlotLayout(image);
        var recipe = new LsbRecipe(LsbChannelStrategy.Red, 0, LsbPlacementKind.Sequential, 0);
        var statistics = new LsbStatisticsAnalyzer().Compare(image, image, layout, recipe, [0, 1, 2, 3], LsbStatisticsScope.EligibleImage, CancellationToken.None).Cover;

        Assert.Equal(new LsbAdjacency(0, 1, 1, 0), statistics.Horizontal);
        Assert.Equal(new LsbAdjacency(0, 1, 1, 0), statistics.Vertical);
        Assert.Equal(1, statistics.Horizontal.TransitionRate);
    }

    [Fact]
    public void SelectedScope只统计给定位置且无邻接时返回NA()
    {
        var image = Image(3, 1, [0, 1, 1]);
        var layout = new LsbSlotLayout(image);
        var recipe = new LsbRecipe(LsbChannelStrategy.Red, 0, LsbPlacementKind.Sequential, 0);
        var statistics = new LsbStatisticsAnalyzer().Compare(image, image, layout, recipe, [0, 2], LsbStatisticsScope.SelectedSlots, CancellationToken.None).Cover;

        Assert.Equal(2, statistics.SampleCount);
        Assert.Null(statistics.Horizontal.TransitionRate);
        Assert.Null(statistics.Vertical.TransitionRate);
    }

    [Fact]
    public void Rgb策略同时保留三通道分项与按真实样本数聚合()
    {
        var image = Image(2, 1, [0, 1]);
        var layout = new LsbSlotLayout(image);
        var recipe = new LsbRecipe(LsbChannelStrategy.RgbRoundRobin, 0, LsbPlacementKind.Sequential, 0);
        var comparison = new LsbStatisticsAnalyzer().Compare(image, image, layout, recipe, [0, 1, 2, 3, 4, 5], LsbStatisticsScope.EligibleImage, CancellationToken.None);

        Assert.Equal(6, comparison.Cover.SampleCount);
        Assert.Equal(new[] { LsbChannel.Red, LsbChannel.Green, LsbChannel.Blue }, comparison.ByChannel.Keys.Order().ToArray());
        Assert.All(comparison.ByChannel.Values, value => Assert.Equal(2, value.Cover.SampleCount));
    }

    private static PixelImage Image(int width, int height, byte[] red)
    {
        var bytes = new byte[width * height * 4];
        for (var pixel = 0; pixel < red.Length; pixel++)
        {
            bytes[pixel * 4] = red[pixel];
            bytes[(pixel * 4) + 3] = 255;
        }
        return new(new(width, height), bytes);
    }
}
