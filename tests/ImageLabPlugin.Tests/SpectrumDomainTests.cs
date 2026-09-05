using System.Numerics;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>频域分析器的纯数值门禁；这些测试不依赖 Avalonia 或文件系统。</summary>
public sealed class SpectrumDomainTests
{
    [Fact]
    public void 六通道抽取使用冻结公式且透明像素仍分析Rgb()
    {
        var image = new PixelImage(new ImageSize(1, 1), [10, 20, 30, 0]);
        var converter = new ImageChannelConverter();

        Assert.Equal(10d, converter.Extract(image, ImageChannel.Red)[0, 0]);
        Assert.Equal(20d, converter.Extract(image, ImageChannel.Green)[0, 0]);
        Assert.Equal(30d, converter.Extract(image, ImageChannel.Blue)[0, 0]);
        Assert.Equal(18.15d, converter.Extract(image, ImageChannel.Luma)[0, 0], 10);
        Assert.Equal(134.68736d, converter.Extract(image, ImageChannel.ChromaBlue)[0, 0], 8);
        Assert.Equal(122.18688d, converter.Extract(image, ImageChannel.ChromaRed)[0, 0], 8);
    }

    [Fact]
    public void Rgb重建只改变选中通道并保持Alpha和源对象()
    {
        var source = new PixelImage(new ImageSize(1, 1), [10, 20, 30, 40]);
        var result = new ImageChannelConverter().Apply(
            source,
            new ImageChannelPlane(source.Size, ImageChannel.Green, [300d]));

        Assert.Equal((10, 20, 30, 40), source.GetPixel(0, 0));
        Assert.Equal((10, 255, 30, 40), result.Image.GetPixel(0, 0));
        Assert.Equal(1, result.ClippedPixelCount);
    }

    [Fact]
    public void 分析代理不放大小图并按面积平均缩小()
    {
        var rgba = new byte[1024 * 512 * 4];
        for (var i = 0; i < rgba.Length; i += 4) { rgba[i] = (byte)(((i / 4) % 1024) < 512 ? 0 : 200); rgba[i + 3] = 255; }
        var source = new PixelImage(new ImageSize(1024, 512), rgba);
        var projector = new ImageAnalysisProxyProjector();

        var proxy = projector.Create(source, 512);

        Assert.Equal(new ImageSize(512, 256), proxy.Size);
        Assert.Equal((0, 0, 0, 255), proxy.GetPixel(0, 0));
        Assert.Equal((200, 0, 0, 255), proxy.GetPixel(511, 0));
        var small = projector.Create(new PixelImage(new ImageSize(1, 1), [1, 2, 3, 4]), 512);
        Assert.Equal((1, 2, 3, 4), small.GetPixel(0, 0));
    }

    [Fact]
    public void 一维Fft往返与Parseval误差满足一乘十负八()
    {
        var original = Enumerable.Range(0, 64).Select(i => new Complex(Math.Sin(i * 0.31), Math.Cos(i * 0.17))).ToArray();
        var values = original.ToArray();
        var fft = new Fft1DTransform();

        fft.Forward(values);
        var spatialEnergy = original.Sum(value => value.Magnitude * value.Magnitude);
        var frequencyEnergy = values.Sum(value => value.Magnitude * value.Magnitude) / values.Length;
        Assert.True(Math.Abs(spatialEnergy - frequencyEnergy) / spatialEnergy < 1e-8);
        fft.Inverse(values);
        Assert.True(values.Zip(original).Max(pair => (pair.First - pair.Second).Magnitude) < 1e-8);
    }

    [Fact]
    public void 二维常量只有Dc且冲激幅值恒定()
    {
        var fft = new Fft2DTransform(new Fft1DTransform());
        var constant = Enumerable.Repeat(new Complex(7d, 0d), 64).ToArray();
        fft.Forward(constant, 8, 8);
        Assert.Equal(448d, constant[0].Real, 8);
        Assert.True(constant.Skip(1).Max(value => value.Magnitude) < 1e-8);

        var impulse = new Complex[64]; impulse[19] = Complex.One;
        fft.Forward(impulse, 8, 8);
        Assert.All(impulse, value => Assert.InRange(value.Magnitude, 1d - 1e-10, 1d + 1e-10));
    }

    [Fact]
    public void 二维Fft拒绝非法尺寸并在行边界取消()
    {
        var fft = new Fft2DTransform(new Fft1DTransform());
        Assert.Throws<ArgumentException>(() => fft.Forward(new Complex[12], 3, 4));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => fft.Forward(new Complex[64], 8, 8, cancellation.Token));
    }

    [Fact]
    public void 二维Fft往返Parseval与实值共轭满足数值门禁()
    {
        const int width = 16;
        const int height = 8;
        var original = Enumerable.Range(0, width * height)
            .Select(i => new Complex(Math.Sin(i * 0.19) + Math.Cos(i * 0.07), 0d)).ToArray();
        var values = original.ToArray();
        var fft = new Fft2DTransform(new Fft1DTransform());
        fft.Forward(values, width, height);
        var spatialEnergy = original.Sum(value => value.Magnitude * value.Magnitude);
        var frequencyEnergy = values.Sum(value => value.Magnitude * value.Magnitude) / (width * height);
        Assert.True(Math.Abs(spatialEnergy - frequencyEnergy) / spatialEnergy < 1e-8);
        double conjugateError = 0d;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var conjugate = FrequencyCoordinates.ConjugateIndex(x, y, width, height);
                conjugateError = Math.Max(conjugateError, (values[(y * width) + x] - Complex.Conjugate(values[(conjugate.Y * width) + conjugate.X])).Magnitude);
            }
        Assert.True(conjugateError < 1e-8);
        fft.Inverse(values, width, height);
        Assert.True(values.Zip(original).Max(pair => (pair.First - pair.Second).Magnitude) < 1e-8);
    }

    [Fact]
    public void 整数周期正弦出现共轭主峰且棋盘格能量位于Nyquist点()
    {
        const int size = 16;
        var fft = new Fft2DTransform(new Fft1DTransform());
        var sine = new Complex[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++) sine[(y * size) + x] = new Complex(Math.Sin(2d * Math.PI * 3d * x / size), 0d);
        fft.Forward(sine, size, size);
        var peaks = sine.Select((value, index) => (value.Magnitude, Index: index)).OrderByDescending(item => item.Magnitude).Take(2).Select(item => item.Index).Order().ToArray();
        Assert.Equal(new[] { 3, 13 }, peaks);

        var checkerboard = new Complex[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++) checkerboard[(y * size) + x] = new Complex(((x + y) & 1) == 0 ? 1d : -1d, 0d);
        fft.Forward(checkerboard, size, size);
        var maximum = checkerboard.Select((value, index) => (value.Magnitude, Index: index)).MaxBy(item => item.Magnitude);
        Assert.Equal((size / 2 * size) + (size / 2), maximum.Index);
    }

    [Fact]
    public void 中心化坐标和共轭索引共享同一事实源()
    {
        var center = FrequencyCoordinates.FromDisplay(4, 4, 8, 8);
        Assert.Equal((0, 0, 0, 0), (center.InternalX, center.InternalY, center.Kx, center.Ky));
        var corner = FrequencyCoordinates.FromDisplay(0, 0, 8, 8);
        Assert.Equal((-4, -4), (corner.Kx, corner.Ky));
        Assert.Equal(1d, corner.Radius, 12);
        Assert.Equal((7, 6), FrequencyCoordinates.ConjugateIndex(1, 2, 8, 8));
    }

    [Fact]
    public void 频带遮罩对每个频点及其共轭点权重一致()
    {
        var spectrum = new FrequencySpectrum(new ImageSize(8, 8), 8, 8, new Complex[64]);
        var mask = new FrequencyBandMaskFactory().Create(
            spectrum,
            new FrequencyBandDefinition(FrequencyBandKind.Custom, FrequencyBandBoundaries.Default, 0.2, 0.7));
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            {
                var conjugate = FrequencyCoordinates.ConjugateIndex(x, y, 8, 8);
                Assert.Equal(mask[(y * 8) + x], mask[(conjugate.Y * 8) + conjugate.X]);
            }
    }

    [Fact]
    public void 径向能量固定256Bin且四区占比守恒()
    {
        var values = new Complex[64]; values[0] = new Complex(8, 0); values[1] = new Complex(2, 0); values[7] = new Complex(2, 0);
        var report = new RadialEnergyAnalyzer().Analyze(
            new FrequencySpectrum(new ImageSize(8, 8), 8, 8, values), FrequencyBandBoundaries.Default);

        Assert.Equal(256, report.Bins.Count);
        Assert.InRange(report.DcShare + report.LowShare + report.MediumShare + report.HighShare, 1d - 1e-12, 1d + 1e-12);
        Assert.InRange(report.Bins.Sum(), 1d - 1e-12, 1d + 1e-12);
    }

    [Fact]
    public void Dct常量块只有Dc非零且Idct往返()
    {
        var transform = new Dct8x8Transform();
        var spatial = Enumerable.Repeat(200d, 64).ToArray();
        var frequency = new double[64]; var reconstructed = new double[64];
        transform.Forward(spatial, frequency); transform.Inverse(frequency, reconstructed);

        Assert.True(Math.Abs(frequency[0]) > 1d);
        Assert.True(frequency.Skip(1).Max(Math.Abs) < 1e-8);
        Assert.True(reconstructed.Zip(spatial).Max(pair => Math.Abs(pair.First - pair.Second)) < 1e-8);
        Assert.Equal(FrequencyRegion.Medium, DctBlockAnalyzer.ClassifyCoefficient(2, 2));
    }

    [Fact]
    public void 频谱投影对零输入不产生NaN语义并输出黑图()
    {
        var spectrum = new FrequencySpectrum(new ImageSize(8, 8), 8, 8, new Complex[64]);
        var projector = new SpectrumProjector();
        foreach (var mode in Enum.GetValues<SpectrumMagnitudeMode>())
            Assert.All(projector.CreateMagnitude(spectrum, mode).Rgba.ToArray(), value => Assert.True(value == 0 || value == 255));
        var info = projector.Inspect(spectrum, 4, 4, FrequencyBandBoundaries.Default);
        Assert.Null(info.PhaseRadians); Assert.Equal(0d, info.NormalizedEnergy);
    }

    [Fact]
    public void 结构化缓冲上限固定为2048平方()
    {
        Assert.Equal(4_194_304, FrequencySpectrum.MaximumComplexValues);
        Assert.Equal(2048, FrequencySpectrum.NextPowerOfTwo(2048));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrequencySpectrum.NextPowerOfTwo(2049));
    }
}
