using Avalonia;
using ImageLabPlugin.Features.Common;
using ImageLabPlugin.Features.SpectralArt;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>固定 Spectral Art 与 Frequency Mask 共享的 Uniform/letterbox 坐标契约。</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SpectralArtViewTests
{
    [Fact]
    public void 共享坐标映射排除Letterbox并覆盖图片边缘()
    {
        var bounds = new Size(240, 120); var pixels = new PixelSize(120, 120);
        Assert.False(UniformImageCoordinateMapper.TryMap(bounds, pixels, new Point(20, 60), out _, out _));
        Assert.True(UniformImageCoordinateMapper.TryMap(bounds, pixels, new Point(60, 0), out var firstX, out var firstY));
        Assert.True(UniformImageCoordinateMapper.TryMap(bounds, pixels, new Point(180, 120), out var lastX, out var lastY));
        Assert.Equal((0d, 0d), (firstX, firstY)); Assert.Equal((1d, 1d), (lastX, lastY));
    }

    [Fact]
    public void 区域控件默认值位于合法规范半平面()
    {
        var canvas = new SpectralArtRegionCanvas();
        Assert.True(canvas.LeftFrequency > 0d); Assert.True(canvas.TopFrequency < canvas.BottomFrequency);
        Assert.True(canvas.LeftFrequency < canvas.RightFrequency); Assert.True(canvas.BottomFrequency < 0d);
        Assert.True(canvas.Focusable);
    }
}
