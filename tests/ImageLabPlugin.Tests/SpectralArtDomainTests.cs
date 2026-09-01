using System.Numerics;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.SpectralArt;
using ImageLabPlugin.Infrastructure.Persistence;
using ImageLabPlugin.Application.SpectralArt;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>覆盖 Pattern 不可变性、规范化、区域门禁、共轭幅度写入、IFFT 与严格配方。</summary>
public sealed class SpectralArtDomainTests
{
    [Fact]
    public void Pattern防御性复制且指纹稳定()
    {
        double[] values = [0d, 1d, 0.25d, 0.75d];
        var pattern = new SpectralPattern(2, 2, values, SpectralPatternSamplingMode.GrayscaleArea, SpectralPatternSourceKind.LogoImage);
        values[1] = 0d; var exposed = pattern.Weights.ToArray(); exposed[1] = 0d;
        var again = new SpectralPattern(2, 2, [0d, 1d, 0.25d, 0.75d], SpectralPatternSamplingMode.GrayscaleArea, SpectralPatternSourceKind.LogoImage);
        Assert.Equal(1d, pattern[1, 0]); Assert.Equal(pattern.Fingerprint, again.Fingerprint);
    }

    [Theory]
    [InlineData(double.NaN)] [InlineData(-0.1d)] [InlineData(1.1d)]
    public void Pattern拒绝非法权重(double value) => Assert.ThrowsAny<ArgumentException>(() =>
        new SpectralPattern(1, 1, [value], SpectralPatternSamplingMode.BinaryNearest, SpectralPatternSourceKind.Text));

    [Fact]
    public void Pattern拒绝全零与超过512尺寸()
    {
        Assert.Throws<ArgumentException>(() => new SpectralPattern(2, 2, [0d, 0d, 0d, 0d], SpectralPatternSamplingMode.BinaryNearest, SpectralPatternSourceKind.Text));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpectralPattern(513, 1, new double[513], SpectralPatternSamplingMode.BinaryNearest, SpectralPatternSourceKind.Text));
    }

    [Fact]
    public void 二值最近邻保持硬边且透明像素不成为前景()
    {
        var image = new PixelImage(new ImageSize(2, 1), [0, 0, 0, 255, 0, 0, 0, 0]);
        var options = new SpectralPatternNormalizationOptions(SpectralPatternSourceKind.QrImage,
            SpectralPatternSamplingMode.BinaryNearest, 4, 2, 0.5d, false, SpectralPatternBackground.White);
        var pattern = new SpectralPatternNormalizer(new ImageAreaResampler()).Normalize(image, options);
        Assert.All(pattern.Weights.ToArray(), value => Assert.True(value is 0d or 1d));
        Assert.Equal(1d, pattern[0, 0]); Assert.Equal(0d, pattern[3, 1]);
    }

    [Fact]
    public void 灰度面积缩小产生面积平均且反相生效()
    {
        var image = new PixelImage(new ImageSize(2, 2), [0,0,0,255, 255,255,255,255, 255,255,255,255, 255,255,255,255]);
        var normalizer = new SpectralPatternNormalizer(new ImageAreaResampler());
        var normal = normalizer.Normalize(image, new(SpectralPatternSourceKind.LogoImage, SpectralPatternSamplingMode.GrayscaleArea, 1, 1, .5, false, SpectralPatternBackground.Black));
        var inverted = normalizer.Normalize(image, new(SpectralPatternSourceKind.LogoImage, SpectralPatternSamplingMode.GrayscaleArea, 1, 1, .5, true, SpectralPatternBackground.Black));
        Assert.Equal(0.25d, normal[0, 0], 2); Assert.Equal(0.75d, inverted[0, 0], 2);
    }

    [Fact]
    public void 默认区域在64频谱中合法且映射指纹确定()
    {
        var pattern = CheckerPattern(); var mapper = new SpectralPatternMapper();
        var first = mapper.Map(pattern, SpectralArtRegion.Default, SpectralPatternFitMode.Contain, 64, 64);
        var second = mapper.Map(pattern, SpectralArtRegion.Default, SpectralPatternFitMode.Contain, 64, 64);
        Assert.True(first.Width >= 8 && first.Height >= 8); Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.InRange(first.MainBinCount / 4096d, 0d, SpectralPatternMapper.MaximumOccupiedRatio);
    }

    [Theory]
    [InlineData(-0.04, -0.04, 0.04, 0.04)]
    [InlineData(-0.4, -0.4, 0.4, -0.1)]
    [InlineData(0.48, -0.4, 0.5, -0.38)]
    public void 非法区域被领域门禁拒绝(double left, double top, double right, double bottom)
    {
        var region = new SpectralArtRegion(left, top, right, bottom);
        Assert.Throws<InvalidOperationException>(() => new SpectralPatternMapper().Map(CheckerPattern(), region, SpectralPatternFitMode.Stretch, 64, 64));
    }

    [Fact]
    public void 写入器强度零不改工作副本也不报告变化()
    {
        var spectrum = Spectrum(64, 64); var working = spectrum.CreateWorkingCopy(); var before = working.ToArray();
        var mapping = new SpectralPatternMapper().Map(CheckerPattern(), SpectralArtRegion.Default, SpectralPatternFitMode.Stretch, 64, 64);
        var result = new SpectralAmplitudeWriter(new RadialLogPowerBaseline()).ApplyInPlace(spectrum, working, mapping, 0d);
        Assert.Equal(before, working); Assert.Equal(0, result.ChangedTotalBins); Assert.Equal(result.SourceEnergy, result.ResultEnergy);
    }

    [Fact]
    public void 写入器保持相位并逐点产生精确共轭副本()
    {
        var spectrum = Spectrum(64, 64); var working = spectrum.CreateWorkingCopy();
        var mapping = new SpectralPatternMapper().Map(CheckerPattern(), SpectralArtRegion.Default, SpectralPatternFitMode.Stretch, 64, 64);
        var result = new SpectralAmplitudeWriter(new RadialLogPowerBaseline()).ApplyInPlace(spectrum, working, mapping, 2d);
        Assert.True(result.ChangedIndependentBins > 0); Assert.Equal(result.ChangedIndependentBins * 2, result.ChangedTotalBins);
        Assert.InRange(result.MaximumPhaseDeviation, 0d, 1e-10); Assert.Equal(0d, result.MaximumConjugateResidual);
        for (var y = 0; y < mapping.Height; y++) for (var x = 0; x < mapping.Width; x++)
        {
            if (mapping[x, y] <= 0d) continue;
            var point = FrequencyCoordinates.FromDisplay(mapping.Left + x, mapping.Top + y, 64, 64);
            var pair = FrequencyCoordinates.ConjugateIndex(point.InternalX, point.InternalY, 64, 64);
            Assert.Equal(Complex.Conjugate(working[(point.InternalY * 64) + point.InternalX]), working[(pair.Y * 64) + pair.X]);
        }
    }

    [Fact]
    public void 写入后的频谱IFFT满足虚部门禁并裁回原尺寸()
    {
        var spectrum = Spectrum(64, 64); var working = spectrum.CreateWorkingCopy();
        var mapping = new SpectralPatternMapper().Map(CheckerPattern(), SpectralArtRegion.Default, SpectralPatternFitMode.Contain, 64, 64);
        new SpectralAmplitudeWriter(new RadialLogPowerBaseline()).ApplyInPlace(spectrum, working, mapping, 1d);
        var inverse = new FrequencyInverseTransformer(new Fft2DTransform(new Fft1DTransform()));
        var plane = inverse.InverseOwned(working, 64, 64); var cropped = inverse.Crop(plane, new ImageSize(64, 64));
        Assert.Equal(4096, cropped.Length); Assert.InRange(plane.MaximumImaginaryResidual, 0d, FrequencyInverseTransformer.MaximumAllowedImaginaryResidual);
    }

    [Fact]
    public void 固定频谱量程可让两次投影共享同一上限()
    {
        var spectrum = Spectrum(64, 64); var changed = spectrum.CreateWorkingCopy(); changed[0] *= 4d;
        var projector = new SpectrumProjector(); var scale = projector.CreateSharedScale(spectrum, changed, SpectrumMagnitudeMode.Logarithmic);
        var a = projector.CreateMagnitude(spectrum, scale); var b = projector.CreateMagnitude(spectrum, changed, scale);
        Assert.Equal(a.Size, b.Size); Assert.True(scale.MagnitudeLimit > 0d); Assert.NotEqual(a.Rgba.ToArray(), b.Rgba.ToArray());
    }

    [Fact]
    public void 配方严格往返保持Pattern与Recipe指纹()
    {
        var recipe = new SpectralArtRecipe(CheckerPattern(), SpectralArtRegion.Default, SpectralPatternFitMode.Contain, 2d);
        var serializer = new SpectralArtRecipeSerializer(); var restored = serializer.Deserialize(serializer.Serialize(recipe));
        Assert.Equal(recipe.Pattern.Fingerprint, restored.Pattern.Fingerprint); Assert.Equal(recipe.Fingerprint(), restored.Fingerprint());
    }

    [Theory]
    [InlineData("{\"schema\":1,\"schema\":1}")]
    [InlineData("{\"schema\":1,\"unknown\":1}")]
    [InlineData("{\"schema\":1} trailing")]
    public void 严格配方拒绝重复未知与尾随数据(string json) =>
        Assert.Throws<InvalidDataException>(() => new SpectralArtRecipeSerializer().Deserialize(System.Text.Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void 配方序列化不泄露载体路径或原文字()
    {
        var recipe = new SpectralArtRecipe(CheckerPattern(), SpectralArtRegion.Default, SpectralPatternFitMode.Contain, 2d);
        var json = System.Text.Encoding.UTF8.GetString(new SpectralArtRecipeSerializer().Serialize(recipe));
        Assert.DoesNotContain("C:\\\\private", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("secret text", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(SpectralArtProtocol.RecipeProtocol, json, StringComparison.Ordinal);
    }

    private static SpectralPattern CheckerPattern() => new(8, 8,
        Enumerable.Range(0, 64).Select(i => (i + (i / 8)) % 2 == 0 ? 1d : 0d).ToArray(),
        SpectralPatternSamplingMode.BinaryNearest, SpectralPatternSourceKind.Text);

    private static FrequencySpectrum Spectrum(int width, int height)
    {
        var values = new double[width * height];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) values[(y * width) + x] = 96d + (20d * Math.Cos(2d * Math.PI * ((5d * x / width) + (7d * y / height))));
        return new FrequencySpectrumBuilder(new Fft2DTransform(new Fft1DTransform())).Build(new ImageChannelPlane(new ImageSize(width, height), ImageChannel.Luma, values));
    }
}
