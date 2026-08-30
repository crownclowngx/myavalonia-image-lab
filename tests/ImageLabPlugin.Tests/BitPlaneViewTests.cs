using Avalonia;
using ImageLabPlugin.Features.BitPlaneViewer;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class BitPlaneViewTests
{
    [Fact]
    public void Uniform映射排除横向留白并覆盖首末像素()
    {
        var bounds = new Size(200, 100);
        var pixels = new PixelSize(100, 100);
        Assert.False(BitPlanePreviewControl.TryMap(bounds, pixels, new Point(1, 50), out _, out _));
        Assert.True(BitPlanePreviewControl.TryMap(bounds, pixels, new Point(50, 0), out var firstX, out var firstY));
        Assert.True(BitPlanePreviewControl.TryMap(bounds, pixels, new Point(149.999, 99.999), out var lastX, out var lastY));
        Assert.Equal((0d, 0d), (firstX, firstY));
        Assert.InRange(lastX, 0.999, 1d); Assert.InRange(lastY, 0.999, 1d);
    }
}
