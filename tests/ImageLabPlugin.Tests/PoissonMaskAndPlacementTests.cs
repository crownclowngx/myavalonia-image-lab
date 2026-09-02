using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.PoissonBlending;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class PoissonMaskAndPlacementTests
{
    [Fact]
    public void 闭开矩形精确覆盖且空矩形不产生未知量()
    {
        var rasterizer = new PoissonMaskRasterizer(); var size = new ImageSize(6, 5);
        var mask = rasterizer.Rasterize(size, new(new PoissonRectangle(1, 2, 3, 2), []));
        Assert.Equal(6, mask.Values.Span.ToArray().Sum(value => value));
        Assert.True(mask.Contains(1, 2)); Assert.True(mask.Contains(3, 3)); Assert.False(mask.Contains(4, 3));
        var empty = rasterizer.Rasterize(size, new(new PoissonRectangle(1, 1, 0, 2), []));
        Assert.DoesNotContain((byte)1, empty.Values.Span.ToArray());
    }

    [Theory]
    [InlineData(0.5, 0.5, 2, 2)]
    [InlineData(0.125, 0.125, 0, 0)]
    [InlineData(0.375, 0.375, 2, 2)]
    public void 归一化坐标按ToEven冻结(double x, double y, int expectedX, int expectedY)
    { Assert.Equal((expectedX, expectedY), PoissonMaskRasterizer.ToPixel(new(x, y), new ImageSize(5, 5))); }

    [Fact]
    public void 后写擦除覆盖矩形和先写添加()
    {
        var strokes = new[] { new PoissonMaskStroke(PoissonMaskTool.Erase, 0.01, [new(0.5, 0.5)], 1) };
        var mask = new PoissonMaskRasterizer().Rasterize(new ImageSize(9, 9), new(new PoissonRectangle(1, 1, 7, 7), strokes));
        Assert.False(mask.Contains(4, 4)); Assert.True(mask.Contains(1, 1));
    }

    [Fact]
    public void 拓扑统计识别多分量和孔洞()
    {
        var values = new byte[49];
        for (var y = 1; y <= 5; y++) for (var x = 1; x <= 5; x++) values[(y * 7) + x] = 1;
        values[(3 * 7) + 3] = 0; values[(1 * 7) + 1] = 0; values[0] = 1;
        var result = new PoissonMaskTopologyAnalyzer().Analyze(new(new ImageSize(7, 7), values));
        Assert.Equal(2, result.ComponentCount); Assert.Equal(1, result.HoleCount); Assert.Equal(24, result.UnknownCount);
    }

    [Fact]
    public void 空遮罩返回结构化阻断且不抛出索引异常()
    {
        var image = PoissonTestFactory.Solid(5, 5, 10, 20, 30); var mask = new PoissonBinaryMask(image.Size, new byte[25]);
        var result = new PoissonPlacementValidator().Validate(image, image, mask, default);
        Assert.False(result.IsValid); Assert.Contains(result.Issues, item => item.Code == "empty-mask");
    }

    [Fact]
    public void 遮罩触碰源边缘会在分配方程前阻断()
    {
        var source = PoissonTestFactory.Solid(5, 5, 1, 2, 3); var target = source.Clone();
        var mask = PoissonTestFactory.RectangleMask(5, 5, new(0, 2, 1, 1));
        var result = new PoissonPlacementValidator().Validate(source, target, mask, default);
        Assert.Contains(result.Issues, item => item.Code == "source-halo-out-of-bounds");
    }

    [Fact]
    public void 正负偏移按source加offset映射并校验目标halo()
    {
        var source = PoissonTestFactory.Solid(7, 7, 1, 2, 3); var target = PoissonTestFactory.Solid(9, 9, 4, 5, 6);
        var mask = PoissonTestFactory.RectangleMask(7, 7, new(2, 2, 2, 2)); var validator = new PoissonPlacementValidator();
        Assert.True(validator.Validate(source, target, mask, new(2, 2)).IsValid);
        Assert.False(validator.Validate(source, target, mask, new(-2, -2)).IsValid);
    }

    [Theory]
    [InlineData(true, 254, "source-alpha-not-opaque")]
    [InlineData(false, 254, "target-alpha-not-opaque")]
    [InlineData(true, 255, null)]
    public void Alpha254阻断而255通过(bool alterSource, byte alpha, string? expectedCode)
    {
        var source = PoissonTestFactory.Solid(5, 5, 1, 2, 3); var target = source.Clone();
        var changed = PoissonTestFactory.Solid(5, 5, 1, 2, 3, alpha); if (alterSource) source = changed; else target = changed;
        var result = new PoissonPlacementValidator().Validate(source, target, PoissonTestFactory.RectangleMask(5, 5, new(2, 2, 1, 1)), default);
        if (expectedCode is null) Assert.True(result.IsValid); else Assert.Contains(result.Issues, item => item.Code == expectedCode);
    }
}
