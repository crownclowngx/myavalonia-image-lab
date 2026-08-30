using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Wavelets;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class WaveletDenoiseAndWatermarkTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void 零阈值保持全部系数且LL永不修改(int modeValue)
    {
        var mode = (WaveletThresholdMode)modeValue;
        var plane = new ImageChannelPlane(new(8, 8), ImageChannel.Luma,
            Enumerable.Range(0, 64).Select(value => Math.Sin(value) * 30d).ToArray());
        var baseline = new HaarWaveletTransform().Forward(plane, 2);
        var recipe = new WaveletDenoiseRecipe(WaveletTransformId.Haar, ImageChannel.Luma, 2, mode,
            WaveletThresholdSource.Manual, 0d, [1, 2],
            [WaveletSubband.HorizontalDetail, WaveletSubband.VerticalDetail, WaveletSubband.DiagonalDetail]);
        var processed = new WaveletThresholdProcessor().Apply(baseline, recipe);
        Assert.Equal(baseline.Coefficients.ToArray(), processed.Pyramid.Coefficients.ToArray());
    }

    [Fact]
    public void Hard与Soft边界和目标子带精确受控()
    {
        var plane = new ImageChannelPlane(new(4, 4), ImageChannel.Luma,
            new double[] { 1, 2, 3, 7, 5, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47 });
        var baseline = new HaarWaveletTransform().Forward(plane, 1);
        var region = baseline.GetLevel(1).HorizontalDetail;
        var threshold = baseline.Coefficients.Span[(region.Y * 4) + region.X];
        threshold = Math.Abs(threshold);
        var hard = new WaveletDenoiseRecipe(WaveletTransformId.Haar, ImageChannel.Luma, 1, WaveletThresholdMode.Hard,
            WaveletThresholdSource.Manual, threshold, [1], [WaveletSubband.HorizontalDetail]);
        var soft = new WaveletDenoiseRecipe(WaveletTransformId.Haar, ImageChannel.Luma, 1, WaveletThresholdMode.Soft,
            WaveletThresholdSource.Manual, threshold, [1], [WaveletSubband.HorizontalDetail]);
        var processor = new WaveletThresholdProcessor();
        var hardResult = processor.Apply(baseline, hard).Pyramid.Coefficients.Span;
        var softResult = processor.Apply(baseline, soft).Pyramid.Coefficients.Span;
        var index = (region.Y * 4) + region.X;
        Assert.Equal(baseline.Coefficients.Span[index], hardResult[index]);
        Assert.InRange(Math.Abs(softResult[index]), 0d, 1e-12);
        Assert.Equal(baseline.Coefficients.Span[0], softResult[0]);
        Assert.Equal(baseline.Coefficients.Span[3], softResult[3]);
    }

    [Fact]
    public void Mad估计返回有限Universal建议且小样本明确不可用()
    {
        var transform = new HaarWaveletTransform();
        var normal = transform.Forward(new(new(8, 8), ImageChannel.Luma,
            Enumerable.Range(0, 64).Select(value => (double)(value % 7)).ToArray()), 1);
        var estimate = new WaveletNoiseEstimator().Estimate(normal);
        Assert.True(estimate.IsAvailable); Assert.True(double.IsFinite(estimate.Sigma)); Assert.True(estimate.UniversalThreshold >= 0d);
        var tiny = transform.Forward(new(new(2, 2), ImageChannel.Luma, new double[] { 1, 2, 3, 4 }), 1);
        Assert.False(new WaveletNoiseEstimator().Estimate(tiny).IsAvailable);
    }

    [Fact]
    public void Dwt载体固定种子可回读并保持尺寸Alpha与输入不变()
    {
        var image = CreateNoiseImage(256, 256);
        var original = image.Rgba.ToArray();
        var carrier = new DwtWatermarkCarrier(new HaarWaveletTransform(), new ImageChannelConverter());
        var payload = "wavelet-watermark"u8.ToArray();
        var capacity = carrier.Estimate(image, 2, payload.Length);
        Assert.True(capacity.Fits); Assert.True(capacity.MaximumPayloadBytes > payload.Length);
        var embedded = carrier.Embed(image, payload, 2, 64d, 42);
        var read = carrier.Read(embedded.Image, 2, 64d, 42);
        Assert.True(read.Detected); Assert.True(read.IntegrityValid); Assert.Equal(payload, read.Payload);
        Assert.Equal(image.Size, embedded.Image.Size); Assert.Equal(original, image.Rgba.ToArray());
        for (var i = 3; i < original.Length; i += 4) Assert.Equal(original[i], embedded.Image.Rgba.Span[i]);
    }

    [Fact]
    public void Dwt容量边界与错误种子明确失败()
    {
        var image = CreateNoiseImage(128, 128);
        var carrier = new DwtWatermarkCarrier(new HaarWaveletTransform(), new ImageChannelConverter());
        var capacity = carrier.Estimate(image, 1, 0);
        Assert.Throws<InvalidOperationException>(() => carrier.Embed(image, new byte[capacity.MaximumPayloadBytes + 1], 1, 64d, 1));
        var embedded = carrier.Embed(image, new byte[] { 1, 2, 3 }, 1, 64d, 1);
        Assert.False(carrier.Read(embedded.Image, 1, 64d, 2).IntegrityValid);
    }

    [Fact]
    public void 小波通道回写固定使用AwayFromZero且保留Alpha()
    {
        var source = new PixelImage(new(1, 1), new byte[] { 0, 20, 30, 77 });
        var plane = new ImageChannelPlane(new(1, 1), ImageChannel.Red, new double[] { 0.5d });
        var result = new ImageChannelConverter().Apply(source, plane, MidpointRounding.AwayFromZero);
        Assert.Equal((byte)1, result.Image.GetPixel(0, 0).R);
        Assert.Equal((byte)77, result.Image.GetPixel(0, 0).A);
    }

    private static PixelImage CreateNoiseImage(int width, int height)
    {
        var random = new Random(1729); var rgba = new byte[width * height * 4];
        random.NextBytes(rgba); for (var i = 3; i < rgba.Length; i += 4) rgba[i] = (byte)(128 + (i % 128));
        return new(new(width, height), rgba);
    }
}
