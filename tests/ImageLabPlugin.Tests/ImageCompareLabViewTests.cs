using Avalonia;
using Avalonia.Media.Imaging;
using ImageLabPlugin.Features.ImageCompareLab;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class ImageCompareLabViewTests
{
    [Fact]
    public void 视口映射排除黑边并覆盖首末像素()
    {
        var bounds = new Rect(0, 0, 200, 100);
        var size = new PixelSize(100, 100);
        Assert.False(ComparisonViewportMapper.TryMap(bounds, size, new Point(1, 50), 0, 0.5, 0.5, ComparisonDisplayMode.Split, out _));
        Assert.True(ComparisonViewportMapper.TryMap(bounds, size, new Point(50, 0), 0, 0.5, 0.5, ComparisonDisplayMode.Split, out var first));
        Assert.True(ComparisonViewportMapper.TryMap(bounds, size, new Point(149.999, 99.999), 0, 0.5, 0.5, ComparisonDisplayMode.Split, out var last));
        Assert.Equal((0, 0), (first.X, first.Y));
        Assert.Equal((99, 99), (last.X, last.Y));
    }

    [Fact]
    public void 并排两面板映射到相同原图坐标()
    {
        var bounds = new Rect(0, 0, 200, 100); var size = new PixelSize(100, 100);
        Assert.True(ComparisonViewportMapper.TryMap(bounds, size, new Point(25, 50), 0, 0.5, 0.5, ComparisonDisplayMode.SideBySide, out var left));
        Assert.True(ComparisonViewportMapper.TryMap(bounds, size, new Point(125, 50), 0, 0.5, 0.5, ComparisonDisplayMode.SideBySide, out var right));
        Assert.Equal(left, right);
    }
}
