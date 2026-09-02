using ImageLabPlugin.Domain.ImageOscilloscope;
using ImageLabPlugin.Domain.Shared.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class ImageOscilloscopeClippingAndProbeTests
{
    private readonly OscilloscopeColorConverter _converter = new();

    [Fact]
    public void 阈值值对象拒绝倒置越界并接受两个极限组合()
    {
        Assert.Equal((byte)0, new ClippingThresholds(0, 1).Shadow);
        Assert.Equal((byte)255, new ClippingThresholds(254, 255).Highlight);
        Assert.Throws<ArgumentException>(() => new ClippingThresholds(5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClippingThresholds(-1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClippingThresholds(5, 256));
    }

    [Fact]
    public void 裁切包含边界并区分亮度与Rgb任一通道()
    {
        var image = Image(5, 1, [
            0, 0, 0, 255, 5, 5, 5, 255, 250, 250, 250, 255, 255, 255, 255, 255,
            255, 128, 128, 255]);
        var result = new ClippingAnalyzer(_converter).Analyze(image, new ClippingThresholds(5, 250), default);
        Assert.Equal(2UL, result.Counts.LumaShadow); Assert.Equal(2UL, result.Counts.LumaHighlight);
        Assert.Equal(2UL, result.Counts.RgbShadow); Assert.Equal(3UL, result.Counts.RgbHighlight);
        Assert.Equal(3UL, result.Counts.RedHighlight); Assert.Equal(2UL, result.Counts.GreenHighlight);
    }

    [Fact]
    public void 缩小覆盖层以任一命中聚合保留孤立像素()
    {
        var bytes = Enumerable.Repeat(new byte[] { 128, 128, 128, 255 }, 1025).SelectMany(x => x).ToArray();
        bytes[^4] = bytes[^3] = bytes[^2] = 255;
        var result = new ClippingAnalyzer(_converter).Analyze(Image(1025, 1, bytes), ClippingThresholds.Default, default);
        Assert.Equal(1024, result.Width);
        Assert.NotEqual(0, result.Mask[^1] & ClippingAnalyzer.LumaHighlightBit);
    }

    [Fact]
    public void 密度投影使用NearestRank并保持Parade共享上限()
    {
        var counts = Enumerable.Range(1, 200).Select(value => (uint)value).ToArray();
        Assert.Equal(199u, ScopeDensityProjector.PercentileUpper(counts));
        var projector = new ScopeDensityProjector();
        var red = new ScopeCountGrid(2, 1, new uint[] { 1, 100 });
        var green = new ScopeCountGrid(2, 1, new uint[] { 2, 80 });
        var blue = new ScopeCountGrid(2, 1, new uint[] { 0, 60 });
        var parade = projector.ProjectParade(red, green, blue, ScopeDensityMode.Logarithmic);
        Assert.Equal(parade.Red.UpperCount, parade.Green.UpperCount); Assert.Equal(parade.Red.UpperCount, parade.Blue.UpperCount);
        Assert.Equal(0f, parade.Blue.Tones[0]); Assert.Equal(1f, parade.Red.Tones[1]);
        var linear = projector.Project(red, ScopeDensityMode.Linear);
        Assert.Equal(.01f, linear.Tones[0], 5); Assert.Equal(1f, linear.Tones[1]);
        Assert.Equal(new uint[] { 1, 100 }, red.Counts);
    }

    [Fact]
    public void 密度排序栅格着色与覆盖层投影响应取消()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var grid = new ScopeCountGrid(2, 1, new uint[] { 1, 2 });
        var projector = new ScopeDensityProjector();
        Assert.ThrowsAny<OperationCanceledException>(() => projector.Project(grid, ScopeDensityMode.Logarithmic, cancellation.Token));
        var projection = projector.Project(grid, ScopeDensityMode.Logarithmic);
        Assert.ThrowsAny<OperationCanceledException>(() => new ImageOscilloscopeRasterizer()
            .Rasterize(projection, 1, 2, 3, cancellation.Token));
        var clipping = new ClippingAnalyzer(_converter).Analyze(Image(1, 1, [255, 255, 255, 255]), ClippingThresholds.Default, default);
        Assert.ThrowsAny<OperationCanceledException>(() => new ClippingAnalyzer(_converter)
            .CreateOverlay(clipping, ScopeClippingMode.Luma, cancellation.Token));
    }

    [Fact]
    public void Letterbox边界与最后像素使用半开区间()
    {
        var mapper = new ImageProbeCoordinateMapper();
        Assert.False(mapper.Map(49.999, 50, 200, 100, 100, 100).IsInside);
        var first = mapper.Map(50, 0, 200, 100, 100, 100);
        var last = mapper.Map(149.999, 99.999, 200, 100, 100, 100);
        Assert.True(first.IsInside); Assert.Equal((0, 0), (first.SourceX, first.SourceY));
        Assert.True(last.IsInside); Assert.Equal((99, 99), (last.SourceX, last.SourceY));
        Assert.False(mapper.Map(150, 50, 200, 100, 100, 100).IsInside);
    }

    [Fact]
    public void 源像素精确映射到全部Scope与分布Bin()
    {
        var source = Image(2, 1, [0, 0, 0, 255, 255, 0, 0, 255]);
        var probe = new ScopeProbeMapper(_converter).Map(source, 1, 0, 2);
        Assert.Equal(new ScopePoint(1, 179), probe.Waveform);
        Assert.Equal(new ScopePoint(1, 0), probe.RedParade);
        Assert.Equal(new ScopePoint(1, 255), probe.GreenParade);
        Assert.Equal(255, probe.RedHistogramBin); Assert.Equal(76, probe.LumaHistogramBin);
        Assert.Equal(255, probe.SaturationBin); Assert.Equal(0, probe.HueBin);
        Assert.Equal(ImageOscilloscopeAnalyzer.MapVectorscope(probe.Pixel.Cb, probe.Pixel.Cr), probe.Vectorscope);
    }

    [Fact]
    public void 六个纯色参考目标复用相同颜色与坐标公式()
    {
        var targets = new VectorscopeReferenceTargetProvider(_converter).Create();
        Assert.Equal(new[] { "R", "Mg", "B", "Cy", "G", "Yl" }, targets.Select(target => target.Label));
        var red = _converter.Convert(255, 0, 0, 255);
        Assert.Equal(ImageOscilloscopeAnalyzer.MapVectorscope(red.Cb, red.Cr), targets[0].Point);
        Assert.Equal(6, targets.Select(target => target.Point).Distinct().Count());
    }

    private static PixelImage Image(int width, int height, byte[] rgba) => new(new ImageSize(width, height), rgba);
}
