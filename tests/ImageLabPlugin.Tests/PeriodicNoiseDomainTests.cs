using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.PeriodicNoiseRemoval;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>周期峰模型、坐标、检测、Notch 公式、共轭遮罩与 IFFT Golden。</summary>
public sealed class PeriodicNoiseDomainTests
{
    [Fact]
    public void 频率坐标规范化共轭与环面距离保持一致()
    {
        var point = PeriodicFrequency.FromInternal(5, 3, 64, 32);
        var conjugate = point.Conjugate();
        Assert.Equal((-5d / 64d, -3d / 32d), (conjugate.Fx, conjugate.Fy));
        Assert.Equal(0d, PeriodicFrequency.ToroidalDistance(point, point));
        Assert.Equal(0.02d, PeriodicFrequency.ToroidalDistance(new(-0.49, 0), new(0.49, 0)), 12);
        Assert.Equal(PeriodicFrequency.Canonical(point), PeriodicFrequency.Canonical(conjugate));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PeriodicFrequency(0.5, 0));
    }

    [Fact]
    public void 配方复制去重限制数量并区分完整与数学指纹()
    {
        var external = new List<PeriodicNotch> { new(new(-0.1, 0), PeriodicNotchOrigin.Manual) };
        var manual = new PeriodicNoiseRecipe(ImageChannel.Luma, PeriodicNotchTransition.Gaussian,
            0.01, 0.8, 9, external);
        external.Clear();
        var automatic = new PeriodicNoiseRecipe(ImageChannel.Luma, PeriodicNotchTransition.Gaussian,
            0.01, 0.8, 2, [new(new(-0.1, 0), PeriodicNotchOrigin.Automatic)]);
        Assert.Single(manual.Notches);
        Assert.NotEqual(manual.Fingerprint(), automatic.Fingerprint());
        Assert.Equal(manual.MathematicalFingerprint(), automatic.MathematicalFingerprint());
        Assert.Equal(1, manual.ButterworthOrder);
        Assert.Throws<ArgumentException>(() => new PeriodicNoiseRecipe(ImageChannel.Red,
            PeriodicNotchTransition.Ideal, 0.01, 1, 1,
            Enumerable.Range(0, 33).Select(i => new PeriodicNotch(new(-0.49 + (i * 0.01), 0.1),
                PeriodicNotchOrigin.Manual))));
    }

    [Theory]
    [InlineData(PeriodicNotchTransition.Ideal, 0, 0)]
    [InlineData(PeriodicNotchTransition.Ideal, 1, 0)]
    [InlineData(PeriodicNotchTransition.Ideal, 1.0001, 1)]
    [InlineData(PeriodicNotchTransition.Butterworth, 0, 0)]
    [InlineData(PeriodicNotchTransition.Butterworth, 1, 0.5)]
    [InlineData(PeriodicNotchTransition.Gaussian, 0, 0)]
    [InlineData(PeriodicNotchTransition.Gaussian, 1, 0.5)]
    internal void 三种响应固定点符合振幅协议(PeriodicNotchTransition transition, double radiusRatio,
        double expectedGain)
    {
        var actual = new NotchResponse().Gain(radiusRatio * 0.02, transition, 0.02, 1, 2);
        Assert.Equal(expectedGain, actual, 10);
    }

    [Fact]
    public void 零强度全通且非法数值与阶数被拒绝()
    {
        var response = new NotchResponse();
        Assert.Equal(1d, response.Gain(0, PeriodicNotchTransition.Gaussian, 0.01, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => response.Gain(double.NaN,
            PeriodicNotchTransition.Ideal, 0.01, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => response.Gain(0,
            PeriodicNotchTransition.Butterworth, 0.01, 1, 13));
    }

    [Fact]
    public void 多中心遮罩取最小值且严格共轭并与输入顺序无关()
    {
        var spectrum = Spectrum(32, 32, (x, y) => 10d + x + y);
        var first = Recipe([new(new(-5d / 32d, 0), PeriodicNotchOrigin.Manual),
            new(new(0, -7d / 32d), PeriodicNotchOrigin.Automatic)]);
        var second = Recipe(first.Notches.Reverse());
        var factory = new NotchMaskFactory(new NotchResponse());
        var a = factory.Create(spectrum, first);
        var b = factory.Create(spectrum, second);
        Assert.Equal(a.GainMask.Gains.ToArray(), b.GainMask.Gains.ToArray());
        Assert.Equal(0d, a.GainMask[27, 0], 12);
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
        {
            var pair = FrequencyCoordinates.ConjugateIndex(x, y, 32, 32);
            Assert.Equal(a.GainMask[x, y], a.GainMask[pair.X, pair.Y], 12);
        }
        Assert.True(a.Statistics.ModifiedBinCount >= 4);
    }

    [Fact]
    public void 常量频谱不产生候选且取消不返回部分结果()
    {
        var detector = Detector();
        var spectrum = Spectrum(32, 32, (_, _) => 42d);
        Assert.Empty(detector.Detect(spectrum, new()).Candidates);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => detector.Detect(spectrum, new(), cancellation.Token));
    }

    [Fact]
    public void 密集规则纹理事实被标为高风险且不能成为保守建议()
    {
        var settings = new PeriodicNoiseDetectionSettings();
        var assessed = new PeriodicPeakRiskAssessor().Assess(new PeriodicFrequency(-0.2, 0.1),
            prominence: 2, compactness: 0.9, denseNeighborCount: 5, settings);
        var candidate = new PeriodicFrequencyCandidate(new(-0.2, 0.1), new(0.2, -0.1), 12, 2, 0.9,
            assessed.Level, assessed.Reasons, 10);
        Assert.Equal(PeriodicPeakRiskLevel.High, candidate.RiskLevel);
        Assert.True(candidate.RiskReasons.HasFlag(PeriodicPeakRiskReason.DenseNeighborhood));
        Assert.False(candidate.IsSafeSuggestion);
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(0, 7)]
    [InlineData(5, 9)]
    internal void 合成水平垂直斜向正弦的首候选命中真实频率(int kx, int ky)
    {
        var spectrum = Spectrum(64, 64, (x, y) => 128d +
            (35d * Math.Cos(2d * Math.PI * (((kx * x) / 64d) + ((ky * y) / 64d)))));
        var settings = new PeriodicNoiseDetectionSettings(0.01, 3, 0.1, 0.01);
        var first = Detector().Detect(spectrum, settings);
        var second = Detector().Detect(spectrum, settings);
        Assert.NotEmpty(first.Candidates);
        var candidate = first.Candidates[0].CanonicalFrequency;
        var expected = PeriodicFrequency.Canonical(new(kx / 64d, ky / 64d));
        Assert.InRange(PeriodicFrequency.ToroidalDistance(candidate, expected), 0, 1d / 64d);
        Assert.Equal(first.Candidates, second.Candidates);
    }

    [Fact]
    public void 精确全强度陷波移除正弦并保持DC与实值残差()
    {
        var spectrum = Spectrum(64, 64, (x, _) => 128d + 30d * Math.Cos(2d * Math.PI * 5d * x / 64d));
        var recipe = Recipe([new(new(-5d / 64d, 0), PeriodicNotchOrigin.Manual)]);
        var mask = new NotchMaskFactory(new NotchResponse()).Create(spectrum, recipe);
        var result = new FrequencyMaskApplier(Fft()).Apply(spectrum, mask.GainMask);
        Assert.All(result.Values.ToArray(), value => Assert.InRange(value, 128d - 1e-8, 128d + 1e-8));
        Assert.InRange(result.MaximumImaginaryResidual, 0, 1e-8);
        Assert.NotEqual(0d, mask.GainMask[0, 0]);
    }

    private static PeriodicNoiseRecipe Recipe(IEnumerable<PeriodicNotch> notches) => new(ImageChannel.Luma,
        PeriodicNotchTransition.Ideal, 0.001, 1, 1, notches);

    private static PeriodicPeakDetector Detector() => new(new RadialSpectrumBaseline(),
        new PeriodicPeakRiskAssessor());

    internal static Fft2DTransform Fft() => new(new Fft1DTransform());

    internal static FrequencySpectrum Spectrum(int width, int height, Func<int, int, double> sample)
    {
        var values = new double[width * height];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            values[(y * width) + x] = sample(x, y);
        return new FrequencySpectrumBuilder(Fft()).Build(new ImageChannelPlane(new ImageSize(width, height),
            ImageChannel.Luma, values));
    }
}
