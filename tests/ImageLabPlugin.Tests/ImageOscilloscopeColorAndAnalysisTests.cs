using ImageLabPlugin.Domain.ImageOscilloscope;
using ImageLabPlugin.Domain.Shared.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class ImageOscilloscopeColorAndAnalysisTests
{
    private readonly OscilloscopeColorConverter _converter = new();

    [Fact]
    public void 白底合成与ToEven使用同一可见Rgb事实()
    {
        var transparentBlack = _converter.Convert(0, 0, 0, 0);
        var transparentHidden = _converter.Convert(17, 99, 201, 0);
        Assert.Equal((byte)255, transparentBlack.Red);
        Assert.Equal(transparentBlack, transparentHidden with { Alpha = 0 });
        Assert.Equal((byte)128, OscilloscopeColorConverter.CompositeOnWhite(0, 127));
        Assert.Equal((byte)127, OscilloscopeColorConverter.CompositeOnWhite(0, 128));
    }

    [Fact]
    public void 纯色与灰阶Bt601和HsvGolden成立()
    {
        var red = _converter.Convert(255, 0, 0, 255);
        var green = _converter.Convert(0, 255, 0, 255);
        var blue = _converter.Convert(0, 0, 255, 255);
        var gray = _converter.Convert(128, 128, 128, 255);
        Assert.Equal((byte)76, red.Luma); Assert.Equal((byte)150, green.Luma); Assert.Equal((byte)29, blue.Luma);
        Assert.Equal(0d, red.Hue); Assert.Equal(120d, green.Hue); Assert.Equal(240d, blue.Hue);
        Assert.Equal(1d, red.Saturation); Assert.Null(gray.Hue); Assert.Equal(0d, gray.Cb, 12); Assert.Equal(0d, gray.Cr, 12);
        Assert.Equal(.5d, red.Cr, 12); Assert.Equal(.5d, blue.Cb, 12);
    }

    [Fact]
    public void 三像素分析对所有Scope直方图和分布保持守恒()
    {
        var image = Image(3, 1, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255]);
        var original = image.Rgba.ToArray();
        var result = new ImageOscilloscopeAnalyzer(_converter).Analyze(image, default);
        Assert.Equal(3L, result.PixelCount);
        Assert.Equal(3UL, Sum(result.Waveform.Counts));
        Assert.Equal(3UL, Sum(result.RedParade.Counts)); Assert.Equal(3UL, Sum(result.GreenParade.Counts)); Assert.Equal(3UL, Sum(result.BlueParade.Counts));
        Assert.Equal(3UL, Sum(result.Vectorscope.Counts));
        Assert.Equal(3UL, result.RedHistogram.Aggregate(0UL, (sum, value) => sum + value));
        Assert.Equal(3UL, result.GreenHistogram.Aggregate(0UL, (sum, value) => sum + value));
        Assert.Equal(3UL, result.BlueHistogram.Aggregate(0UL, (sum, value) => sum + value));
        Assert.Equal(3UL, result.LumaHistogram.Aggregate(0UL, (sum, value) => sum + value));
        Assert.Equal(3UL, result.SaturationHistogram.Aggregate(0UL, (sum, value) => sum + value));
        Assert.Equal(3UL, result.ChromaHistogram.Aggregate(0UL, (sum, value) => sum + value));
        Assert.Equal(3UL, result.HueDefinedCount);
        Assert.Equal(original, image.Rgba.ToArray());
    }

    [Fact]
    public void Waveform保持上下方向与最后源列映射()
    {
        var black = new ImageOscilloscopeAnalyzer(_converter).Analyze(Image(1, 1, [0, 0, 0, 255]), default);
        var white = new ImageOscilloscopeAnalyzer(_converter).Analyze(Image(1, 1, [255, 255, 255, 255]), default);
        Assert.Equal(1u, black.Waveform[0, 255]);
        Assert.Equal(1u, white.Waveform[0, 0]);
        Assert.Equal(1023, ImageOscilloscopeAnalyzer.MapHorizontal(1024, 1025, 1024));
    }

    [Fact]
    public void 灰阶不伪造Hue零度且平均色度回到中心()
    {
        var result = new ImageOscilloscopeAnalyzer(_converter).Analyze(
            Image(2, 1, [0, 0, 0, 255, 255, 255, 255, 255]), default);
        Assert.Equal(0UL, result.HueDefinedCount);
        Assert.All(result.HueWeights, value => Assert.Equal(0d, value));
        Assert.Equal(0d, result.MeanCb, 12); Assert.Equal(0d, result.MeanCr, 12);
    }

    [Fact]
    public void 预取消不会返回半成品且固定栅格预算受限()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new ImageOscilloscopeAnalyzer(_converter)
            .Analyze(Image(4, 4, Enumerable.Repeat(new byte[] { 1, 2, 3, 255 }, 16).SelectMany(x => x).ToArray()), cancellation.Token));
        var result = new ImageOscilloscopeAnalyzer(_converter).Analyze(Image(1025, 1,
            Enumerable.Repeat(new byte[] { 0, 0, 0, 255 }, 1025).SelectMany(x => x).ToArray()), default);
        Assert.Equal(1024, result.Waveform.Width);
        Assert.Equal(1024 * 256, result.Waveform.Counts.Count);
        Assert.Equal(512 * 512, result.Vectorscope.Counts.Count);
    }

    private static PixelImage Image(int width, int height, byte[] rgba) => new(new ImageSize(width, height), rgba);
    private static ulong Sum(IEnumerable<uint> values) => values.Aggregate(0UL, (sum, value) => sum + value);
}
