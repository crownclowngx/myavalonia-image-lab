using ImageLabPlugin.Domain.Fingerprinting;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Frequency;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class FingerprintNormalizationTests
{
    [Fact]
    public void 一乘一放大保持视觉亮度且不修改源图()
    {
        var source = new PixelImage(new ImageSize(1, 1), [100, 150, 200, 255]);
        var before = source.Rgba.ToArray();
        var values = new FingerprintLumaNormalizer().Normalize(source, 32, 32);
        var expected = (0.299d * 100) + (0.587d * 150) + (0.114d * 200);
        Assert.Equal(1024, values.Length);
        Assert.All(values, value => Assert.Equal(expected, value, 10));
        Assert.Equal(before, source.Rgba.ToArray());
    }

    [Fact]
    public void 完全透明隐藏RGB不改变归一化和三种指纹()
    {
        var first = new PixelImage(new ImageSize(2, 1), [255, 0, 0, 0, 0, 0, 0, 255]);
        var second = new PixelImage(new ImageSize(2, 1), [0, 255, 200, 0, 0, 0, 0, 255]);
        var normalizer = new FingerprintLumaNormalizer();
        Assert.Equal(normalizer.Normalize(first, 8, 8), normalizer.Normalize(second, 8, 8));
        foreach (var algorithm in CreateAlgorithms(normalizer)) Assert.Equal(algorithm.Compute(first), algorithm.Compute(second));
    }

    [Fact]
    public void 二乘二面积缩小为覆盖区域平均()
    {
        var source = GrayImage(2, 2, [0, 100, 200, 255]);
        var value = Assert.Single(new FingerprintLumaNormalizer().Normalize(source, 1, 1));
        Assert.Equal(138.75d, value, 10);
    }

    [Fact]
    public void 已取消令牌在目标行边界抛出()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        Assert.Throws<OperationCanceledException>(() => new FingerprintLumaNormalizer().Normalize(GrayImage(2, 2, [0, 1, 2, 3]), 8, 8, source.Token));
    }

    internal static PixelImage GrayImage(int width, int height, IReadOnlyList<byte> values)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < values.Count; i++) { rgba[i * 4] = rgba[(i * 4) + 1] = rgba[(i * 4) + 2] = values[i]; rgba[(i * 4) + 3] = 255; }
        return new PixelImage(new ImageSize(width, height), rgba);
    }

    internal static IImageFingerprintAlgorithm[] CreateAlgorithms(FingerprintLumaNormalizer? normalizer = null)
    {
        normalizer ??= new FingerprintLumaNormalizer();
        return [new AverageHashAlgorithm(normalizer), new DifferenceHashAlgorithm(normalizer), new PerceptualHashAlgorithm(normalizer, new())];
    }
}
