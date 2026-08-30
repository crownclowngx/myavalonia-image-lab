using Avalonia;
using ImageLabPlugin.Features.FrequencyMaskEditor;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class FrequencyMaskEditorViewTests
{
    [Fact]
    public void 画布Uniform映射排除letterbox并覆盖边缘()
    {
        var bounds = new Size(200, 100);
        var pixels = new PixelSize(100, 100);
        Assert.False(FrequencyCanvasCoordinateMapper.TryMap(bounds, pixels, new Point(1, 50), out _, out _));
        Assert.True(FrequencyCanvasCoordinateMapper.TryMap(bounds, pixels, new Point(50, 0), out var firstX, out var firstY));
        Assert.True(FrequencyCanvasCoordinateMapper.TryMap(bounds, pixels, new Point(150, 100), out var lastX, out var lastY));
        Assert.Equal((0d, 0d), (firstX, firstY));
        Assert.Equal((1d, 1d), (lastX, lastY));
    }

    [Fact]
    public void 非法尺寸不产生伪坐标()
    {
        Assert.False(FrequencyCanvasCoordinateMapper.TryMap(new Size(0, 100), new PixelSize(10, 10),
            new Point(0, 0), out _, out _));
    }
}
