using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Wavelets;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>覆盖手算 Golden、轴向、奇数扩展、多层重建、能量和策略隔离。</summary>
public sealed class WaveletTransformTests
{
    [Fact]
    public void Haar二维手算Golden与冻结四象限一致()
    {
        var plane = new ImageChannelPlane(new(2, 2), ImageChannel.Luma, new double[] { 1, 2, 3, 4 });
        var pyramid = new HaarWaveletTransform().Forward(plane, 1);

        AssertClose(new double[] { 5, -1, -2, 0 }, pyramid.Coefficients.Span, 1e-12);
        Assert.Equal(new WaveletRegion(0, 0, 1, 1), pyramid.GetLevel(1).Approximation);
        Assert.Equal(new WaveletRegion(0, 1, 1, 1), pyramid.GetLevel(1).HorizontalDetail);
        Assert.Equal(new WaveletRegion(1, 0, 1, 1), pyramid.GetLevel(1).VerticalDetail);
        Assert.Equal(new WaveletRegion(1, 1, 1, 1), pyramid.GetLevel(1).DiagonalDetail);
    }

    [Theory]
    [InlineData(4, 4, 1)]
    [InlineData(5, 3, 2)]
    [InlineData(17, 9, 3)]
    [InlineData(1, 7, 2)]
    [InlineData(7, 1, 2)]
    [InlineData(3, 5, 6)]
    public void 两种策略对奇数与退化尺寸均可逆(int width, int height, int levels)
    {
        var random = new Random(20260831 + width * 17 + height);
        var values = Enumerable.Range(0, width * height).Select(_ => random.NextDouble() * 510d - 255d).ToArray();
        var plane = new ImageChannelPlane(new(width, height), ImageChannel.ChromaBlue, values);
        IWaveletTransform[] transforms = [new HaarWaveletTransform(), new Cdf53WaveletTransform()];

        foreach (var transform in transforms)
        {
            var pyramid = transform.Forward(plane, levels);
            var restored = transform.Inverse(pyramid);
            Assert.Equal(plane.Size, restored.Size);
            AssertClose(values, restored.Values.Span, 2e-10);
        }
    }

    [Fact]
    public void Haar在未扩展平面保持Parseval能量()
    {
        var values = Enumerable.Range(1, 64).Select(value => (double)value - 32.5d).ToArray();
        var plane = new ImageChannelPlane(new(8, 8), ImageChannel.Luma, values);
        var coefficients = new HaarWaveletTransform().Forward(plane, 3).Coefficients.Span;
        Assert.InRange(Math.Abs(values.Sum(value => value * value) - coefficients.ToArray().Sum(value => value * value)), 0d, 1e-9);
    }

    [Fact]
    public void 水平与垂直条纹分别进入LH与HL()
    {
        var horizontal = new double[16]; var vertical = new double[16];
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
        { horizontal[y * 4 + x] = (y & 1) == 0 ? 1d : -1d; vertical[y * 4 + x] = (x & 1) == 0 ? 1d : -1d; }
        var transform = new HaarWaveletTransform();
        var hp = transform.Forward(new(new(4, 4), ImageChannel.Luma, horizontal), 1);
        var vp = transform.Forward(new(new(4, 4), ImageChannel.Luma, vertical), 1);
        Assert.True(Energy(hp, WaveletSubband.HorizontalDetail) > 15.9d);
        Assert.InRange(Energy(hp, WaveletSubband.VerticalDetail), 0d, 1e-20);
        Assert.True(Energy(vp, WaveletSubband.VerticalDetail) > 15.9d);
        Assert.InRange(Energy(vp, WaveletSubband.HorizontalDetail), 0d, 1e-20);
    }

    [Fact]
    public void 错误策略不能逆变换另一策略金字塔()
    {
        var plane = new ImageChannelPlane(new(4, 4), ImageChannel.Red, Enumerable.Range(0, 16).Select(x => (double)x).ToArray());
        var pyramid = new HaarWaveletTransform().Forward(plane, 1);
        Assert.Throws<ArgumentException>(() => new Cdf53WaveletTransform().Inverse(pyramid));
    }

    [Fact]
    public void 可从最深层逐级重建到指定层且第一层等于完整逆变换()
    {
        var values = Enumerable.Range(0, 64).Select(value => (double)value).ToArray();
        var plane = new ImageChannelPlane(new(8, 8), ImageChannel.Luma, values);
        var transform = new HaarWaveletTransform(); var pyramid = transform.Forward(plane, 3);
        var deepestStage = transform.InverseToLevel(pyramid, 3);
        var middleStage = transform.InverseToLevel(pyramid, 2);
        var full = transform.InverseToLevel(pyramid, 1);
        Assert.Equal(new ImageSize(2, 2), deepestStage.Size);
        Assert.Equal(new ImageSize(4, 4), middleStage.Size);
        AssertClose(values, full.Values.Span, 1e-10);
        Assert.Throws<ArgumentOutOfRangeException>(() => transform.InverseToLevel(pyramid, 4));
    }

    private static double Energy(WaveletPyramid pyramid, WaveletSubband subband)
    {
        var region = pyramid.GetLevel(1).GetRegion(subband); var values = pyramid.Coefficients.Span; double result = 0d;
        for (var y = region.Y; y < region.Bottom; y++) for (var x = region.X; x < region.Right; x++)
        { var value = values[y * pyramid.PaddedSize.Width + x]; result += value * value; }
        return result;
    }

    private static void AssertClose(ReadOnlySpan<double> expected, ReadOnlySpan<double> actual, double tolerance)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++) Assert.InRange(Math.Abs(expected[i] - actual[i]), 0d, tolerance);
    }
}
