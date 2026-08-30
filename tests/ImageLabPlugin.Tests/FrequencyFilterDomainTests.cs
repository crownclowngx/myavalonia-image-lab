using ImageLabPlugin.Domain.Convolution;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>频域滤波公式、所有权、IFFT、投影、诊断与空间近似的数值门禁。</summary>
public sealed class FrequencyFilterDomainTests
{
    [Theory]
    [InlineData(FrequencyFilterFamily.Ideal, 0.2, 1, 0.1, 1.0)]
    [InlineData(FrequencyFilterFamily.Ideal, 0.2, 1, 0.2, 1.0)]
    [InlineData(FrequencyFilterFamily.Ideal, 0.2, 1, 0.200001, 0.0)]
    [InlineData(FrequencyFilterFamily.Butterworth, 0.2, 1, 0.0, 1.0)]
    [InlineData(FrequencyFilterFamily.Butterworth, 0.2, 12, 0.2, 0.5)]
    [InlineData(FrequencyFilterFamily.Gaussian, 0.2, 1, 0.0, 1.0)]
    [InlineData(FrequencyFilterFamily.Gaussian, 0.2, 1, 0.2, 0.5)]
    internal void 低通原型固定点符合协议(FrequencyFilterFamily family, double cutoff, int order, double radius, double expected)
    {
        var recipe = Recipe(FrequencyFilterKind.LowPass, family, cutoff, 0.7, order);
        Assert.Equal(expected, new RadialFilterResponse().Evaluate(recipe, radius), 12);
    }

    [Theory]
    [InlineData(FrequencyFilterFamily.Ideal, 1)]
    [InlineData(FrequencyFilterFamily.Butterworth, 1)]
    [InlineData(FrequencyFilterFamily.Butterworth, 12)]
    [InlineData(FrequencyFilterFamily.Gaussian, 1)]
    internal void 低高通与带通带阻逐点互补(FrequencyFilterFamily family, int order)
    {
        var response = new RadialFilterResponse();
        for (var i = 0; i <= 100; i++)
        {
            var radius = i / 100d;
            var low = response.Evaluate(Recipe(FrequencyFilterKind.LowPass, family, 0.3, 0.7, order), radius);
            var high = response.Evaluate(Recipe(FrequencyFilterKind.HighPass, family, 0.3, 0.7, order), radius);
            var pass = response.Evaluate(Recipe(FrequencyFilterKind.BandPass, family, 0.3, 0.7, order), radius);
            var stop = response.Evaluate(Recipe(FrequencyFilterKind.BandStop, family, 0.3, 0.7, order), radius);
            Assert.InRange(low, 0d, 1d); Assert.InRange(high, 0d, 1d);
            Assert.Equal(1d, low + high, 12); Assert.Equal(1d, pass + stop, 12);
        }
    }

    [Fact]
    public void 配方拒绝非法边界并规范化无关参数()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Recipe(FrequencyFilterKind.LowPass, FrequencyFilterFamily.Ideal, 0, 0.8, 1));
        Assert.Throws<ArgumentException>(() => Recipe(FrequencyFilterKind.BandPass, FrequencyFilterFamily.Ideal, 0.8, 0.2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Recipe(FrequencyFilterKind.LowPass, FrequencyFilterFamily.Butterworth, 0.2, 0.8, 13));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrequencyFilterRecipe(FrequencyFilterKind.LowPass,
            FrequencyFilterFamily.Gaussian, 0.2, 0.8, 1, FrequencyProjectionMode.Centered, 4.1, ImageChannel.Luma));
        var one = Recipe(FrequencyFilterKind.LowPass, FrequencyFilterFamily.Gaussian, 0.2, 0.3, 1);
        var two = Recipe(FrequencyFilterKind.LowPass, FrequencyFilterFamily.Gaussian, 0.2, 0.9, 12);
        Assert.Equal(one.Fingerprint(), two.Fingerprint());
    }

    [Fact]
    public void 过渡区公式与Ideal零宽度固定()
    {
        var response = new RadialFilterResponse();
        var ideal = response.Transition(FrequencyFilterFamily.Ideal, 0.25, 1);
        Assert.Equal(0d, ideal.Width);
        var butterworth = response.Transition(FrequencyFilterFamily.Butterworth, 0.25, 2);
        Assert.True(butterworth.RadiusAt90Percent < 0.25); Assert.True(butterworth.RadiusAt10Percent > 0.25);
        var gaussian = response.Transition(FrequencyFilterFamily.Gaussian, 0.25, 1);
        Assert.True(gaussian.RadiusAt90Percent < 0.25); Assert.True(gaussian.RadiusAt10Percent > 0.25);
    }

    [Fact]
    public void 遮罩共轭对称且对外数组不可修改()
    {
        var spectrum = Spectrum(8, 8, Enumerable.Range(0, 64).Select(i => (double)i).ToArray());
        var mask = new FrequencyFilterMaskFactory(new RadialFilterResponse()).Create(spectrum,
            Recipe(FrequencyFilterKind.BandPass, FrequencyFilterFamily.Butterworth, 0.2, 0.7, 4));
        for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++)
        {
            var conjugate = FrequencyCoordinates.ConjugateIndex(x, y, 8, 8);
            Assert.Equal(mask[x, y], mask[conjugate.X, conjugate.Y], 12);
        }
        var copy = mask.Gains.ToArray(); copy[0] = 123;
        Assert.NotEqual(123, mask[0, 0]);
    }

    [Fact]
    public void 常量图低通保持且高通清除DC并不修改缓存频谱()
    {
        var spectrum = Spectrum(8, 8, Enumerable.Repeat(42d, 64).ToArray());
        var before = spectrum.Values.ToArray();
        var factory = new FrequencyFilterMaskFactory(new RadialFilterResponse());
        var engine = new FrequencyFilterEngine(new Fft2DTransform(new Fft1DTransform()));
        var low = Recipe(FrequencyFilterKind.LowPass, FrequencyFilterFamily.Gaussian, 0.3, 0.8, 1);
        var lowResult = engine.Apply(spectrum, factory.Create(spectrum, low));
        Assert.All(lowResult.Values.ToArray(), value => Assert.InRange(value, 42d - 1e-9, 42d + 1e-9));
        var high = Recipe(FrequencyFilterKind.HighPass, FrequencyFilterFamily.Ideal, 0.2, 0.8, 1);
        var highResult = engine.Apply(spectrum, factory.Create(spectrum, high));
        Assert.All(highResult.Values.ToArray(), value => Assert.InRange(Math.Abs(value), 0d, 1e-9));
        Assert.Equal(before, spectrum.Values.ToArray());
        Assert.InRange(highResult.MaximumImaginaryResidual, 0d, 1e-8);
    }

    [Fact]
    public void 三种输出投影只应用一次偏置或叠加且保留Alpha()
    {
        var image = Solid(2, 1, 10, 20, 30, 77);
        var converter = new ImageChannelConverter(); var source = converter.Extract(image, ImageChannel.Red);
        foreach (var item in new[]
        {
            (FrequencyProjectionMode.Direct, 1d, 5d),
            (FrequencyProjectionMode.Centered, 2d, 138d),
            (FrequencyProjectionMode.Additive, 2d, 20d)
        })
        {
            var recipe = new FrequencyFilterRecipe(FrequencyFilterKind.HighPass, FrequencyFilterFamily.Ideal,
                0.2, 1, 1, item.Item1, item.Item2, ImageChannel.Red);
            var raw = new FrequencyFilterPlaneResult(image.Size, [5d, 5d], 0, recipe.MathematicalFingerprint());
            var result = new FrequencySignalProjector(converter).Project(image, source, raw, recipe);
            Assert.Equal((byte)Math.Round(item.Item3), result.Image.GetPixel(0, 0).R);
            Assert.Equal(77, result.Image.GetAlpha(0, 0));
        }
    }

    [Fact]
    public void 副作用诊断限制越界摘要并给出剖面与梯度()
    {
        var image = Solid(8, 8, 100, 0, 0, 255); var converter = new ImageChannelConverter(); var plane = converter.Extract(image, ImageChannel.Red);
        var recipe = Recipe(FrequencyFilterKind.HighPass, FrequencyFilterFamily.Ideal, 0.2, 1, 1);
        var values = Enumerable.Range(0, 64).Select(i => (i % 2 == 0 ? -10d : 300d) + (i / 8)).ToArray();
        var raw = new FrequencyFilterPlaneResult(image.Size, values, 0, recipe.MathematicalFingerprint());
        var projected = new ImageChannelPlane(image.Size, ImageChannel.Red, values);
        var result = new FrequencySideEffectAnalyzer().Analyze(plane, raw, projected);
        Assert.Equal(32, result.Outliers.Count); Assert.Equal(32, result.FilteredBelowZero); Assert.Equal(32, result.FilteredAbove255);
        Assert.Equal(8, result.SourceHorizontalProfile.Count); Assert.Equal(8, result.ResultVerticalProfile.Count);
        Assert.True(result.ResultGradientEnergy > result.SourceGradientEnergy);
    }

    [Theory]
    [InlineData(FrequencyFilterKind.LowPass, 1d)]
    [InlineData(FrequencyFilterKind.BandStop, 1d)]
    [InlineData(FrequencyFilterKind.HighPass, 0d)]
    [InlineData(FrequencyFilterKind.BandPass, 0d)]
    internal void 截断冲激核按滤波方向修正DC且能量比例有界(FrequencyFilterKind kind, double expectedSum)
    {
        var spectrum = Spectrum(32, 32, new double[1024]);
        var mask = new FrequencyFilterMaskFactory(new RadialFilterResponse()).Create(spectrum,
            Recipe(kind, FrequencyFilterFamily.Gaussian, 0.2, 0.65, 1));
        var kernel = new FrequencyImpulseResponseFactory(new Fft2DTransform(new Fft1DTransform())).Create(mask, kind, 7);
        Assert.Equal(expectedSum, kernel.SumAfterCorrection, 10);
        Assert.InRange(kernel.RetainedL1Ratio, 0, 1); Assert.InRange(kernel.RetainedL2Ratio, 0, 1);
        Assert.InRange(kernel.MaximumImaginaryResidual, 0, 1e-8);
    }

    [Fact]
    public void 空间近似使用raw比较并报告非负中位耗时()
    {
        var values = Enumerable.Range(0, 64).Select(i => (double)(i % 13)).ToArray(); var spectrum = Spectrum(8, 8, values);
        var recipe = Recipe(FrequencyFilterKind.LowPass, FrequencyFilterFamily.Gaussian, 0.4, 0.8, 1);
        var factory = new FrequencyFilterMaskFactory(new RadialFilterResponse()); var fft = new Fft2DTransform(new Fft1DTransform());
        var comparison = new FrequencySpatialComparator(new FrequencyFilterEngine(fft), new SpatialConvolver(),
            new FrequencyImpulseResponseFactory(fft)).Compare(values, spectrum, factory.Create(spectrum, recipe), recipe.Kind, 7);
        Assert.True(comparison.MeanAbsoluteError >= 0); Assert.True(comparison.MaximumAbsoluteError >= 0);
        Assert.True(comparison.FrequencyElapsed >= TimeSpan.Zero); Assert.True(comparison.SpatialElapsed >= TimeSpan.Zero);
        Assert.Equal(3, comparison.MeasurementCount);
    }

    private static FrequencyFilterRecipe Recipe(FrequencyFilterKind kind, FrequencyFilterFamily family,
        double inner, double outer, int order) => new(kind, family, inner, outer, order,
        FrequencyProjectionMode.Direct, 1, ImageChannel.Red);

    private static FrequencySpectrum Spectrum(int width, int height, double[] values)
    {
        var plane = new ImageChannelPlane(new ImageSize(width, height), ImageChannel.Red, values);
        return new FrequencySpectrumBuilder(new Fft2DTransform(new Fft1DTransform())).Build(plane);
    }

    internal static PixelImage Solid(int width, int height, byte r, byte g, byte b, byte a)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++) { rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = a; }
        return new PixelImage(new ImageSize(width, height), rgba);
    }
}
