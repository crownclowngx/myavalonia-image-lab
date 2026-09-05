using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class ImageDifferenceAndHistogramTests
{
    private readonly ImagePairValidator _validator = new();

    [Fact]
    public void 缩小基础差异执行先差异后聚合而不会抵消()
    {
        var reference = Image(2, 1, [0, 0, 0, 255, 255, 0, 0, 255]);
        var candidate = Image(2, 1, [255, 0, 0, 255, 0, 0, 0, 255]);
        var proxy = new ImageDifferenceProxyAnalyzer(_validator).Analyze(reference, candidate, 1);

        Assert.Equal(new ImageSize(1, 1), proxy.Size);
        Assert.Equal(255, proxy.Red.Span[0]);
        Assert.Equal(255, proxy.MaximumRgb.Span[0]);
        Assert.Equal(new byte[] { 0, 0, 0, 255, 255, 0, 0, 255 }, reference.Rgba.ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void Rgb差异六档倍率裁切且Alpha固定(int amplification)
    {
        var proxy = new ImageDifferenceProxy(new ImageSize(1, 1), [10], [20], [30], [30], [18]);
        var result = new ImageDifferenceProxyProjector().Project(proxy, amplification);
        var pixel = result.Image.GetPixel(0, 0);
        Assert.Equal(Math.Min(255, 10 * amplification), pixel.R);
        Assert.Equal(Math.Min(255, 20 * amplification), pixel.G);
        Assert.Equal(Math.Min(255, 30 * amplification), pixel.B);
        Assert.Equal(255, pixel.A);
    }

    [Fact]
    public void 热力图固定色表有256项且不按输入自动归一化()
    {
        var projector = new DifferenceHeatmapProjector();
        var first = new ImageDifferenceProxy(new ImageSize(1, 1), [1], [1], [1], [1], [1]);
        var second = new ImageDifferenceProxy(new ImageSize(1, 1), [1], [1], [1], [1], [200]);
        var colorA = projector.Project(first, HeatmapScalarSource.MaximumRgb, 1).Image.GetPixel(0, 0);
        var colorB = projector.Project(second, HeatmapScalarSource.MaximumRgb, 1).Image.GetPixel(0, 0);

        Assert.Equal(256, DifferenceHeatmapProjector.ColorTable.Count);
        Assert.Equal(colorA, colorB);
        Assert.NotEqual(DifferenceHeatmapProjector.ColorTable[0], DifferenceHeatmapProjector.ColorTable[255]);
    }

    [Fact]
    public void 六通道直方图总数守恒并使用冻结的Y舍入()
    {
        var reference = Image(2, 1, [255, 0, 0, 255, 0, 255, 0, 255]);
        var candidate = reference.Clone();
        var histograms = new ImageHistogramAnalyzer(_validator).Analyze(reference, candidate);

        foreach (var channel in Enum.GetValues<ImageChannel>())
        {
            Assert.Equal(2, histograms.Reference.GetBins(channel).Sum());
            Assert.Equal(2, histograms.Candidate.GetBins(channel).Sum());
        }
        Assert.Equal(1, histograms.Reference.GetBins(ImageChannel.Luma)[76]);
        Assert.Equal(1, histograms.Reference.GetBins(ImageChannel.Luma)[150]);
    }

    [Fact]
    public void 非法倍率被领域投影拒绝()
    {
        var proxy = new ImageDifferenceProxy(new ImageSize(1, 1), [1], [1], [1], [1], [1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageDifferenceProxyProjector().Project(proxy, 3));
    }

    private static PixelImage Image(int width, int height, byte[] rgba) => new(new ImageSize(width, height), rgba);
}
