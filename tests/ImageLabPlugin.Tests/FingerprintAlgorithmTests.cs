using ImageLabPlugin.Domain.Fingerprinting;
using ImageLabPlugin.Domain.Frequency;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class FingerprintAlgorithmTests
{
    [Fact]
    public void 规范摘要与八乘八位序往返()
    {
        var value = new ImageFingerprint(FingerprintAlgorithmId.AverageHash, 0x8000000000000001UL);
        Assert.Equal("8000000000000001", value.ToCanonicalHex());
        Assert.True(value.GetBit(0, 0)); Assert.True(value.GetBit(7, 7)); Assert.False(value.GetBit(1, 0));
        Assert.Equal(value, ImageFingerprint.Parse(FingerprintAlgorithmId.AverageHash, "8000000000000001"));
        Assert.Throws<FormatException>(() => ImageFingerprint.Parse(FingerprintAlgorithmId.AverageHash, "ABC"));
    }

    [Fact]
    public void aHash均匀图全一并冻结大于等于分支()
    {
        var image = FingerprintNormalizationTests.GrayImage(1, 1, [42]);
        var result = new AverageHashAlgorithm(new()).Compute(image);
        Assert.Equal(ulong.MaxValue, result.Bits);
        Assert.Equal("FFFFFFFFFFFFFFFF", result.ToCanonicalHex());
    }

    [Fact]
    public void dHash水平递增全零递减全一且垂直变化不混入()
    {
        var increasing = FingerprintNormalizationTests.GrayImage(9, 8, Enumerable.Range(0, 72).Select(i => (byte)((i % 9) * 20)).ToArray());
        var decreasing = FingerprintNormalizationTests.GrayImage(9, 8, Enumerable.Range(0, 72).Select(i => (byte)((8 - (i % 9)) * 20)).ToArray());
        var vertical = FingerprintNormalizationTests.GrayImage(9, 8, Enumerable.Range(0, 72).Select(i => (byte)((i / 9) * 20)).ToArray());
        var algorithm = new DifferenceHashAlgorithm(new());
        Assert.Equal(0UL, algorithm.Compute(increasing).Bits);
        Assert.Equal(ulong.MaxValue, algorithm.Compute(decreasing).Bits);
        Assert.Equal(0UL, algorithm.Compute(vertical).Bits);
    }

    [Fact]
    public void 低频DCT常量输入只有DC显著()
    {
        var transform = new LowFrequencyDctTransform();
        var coefficients = transform.Transform(Enumerable.Repeat(10d, 32 * 32).ToArray());
        Assert.Equal(320d, coefficients[0], 8);
        Assert.All(coefficients.Skip(1), value => Assert.InRange(Math.Abs(value), 0d, 1e-10));
    }

    [Fact]
    public void pHash与独立二维参考循环一致()
    {
        var values = Enumerable.Range(0, 32 * 32).Select(i => (byte)((i * 37 + (i / 32) * 11) % 256)).ToArray();
        var image = FingerprintNormalizationTests.GrayImage(32, 32, values);
        var actual = new PerceptualHashAlgorithm(new(), new()).Compute(image);
        var coefficients = IndependentDct(values);
        var ac = coefficients.Skip(1).Order().ToArray(); var median = ac[31]; ulong expected = 0;
        for (var i = 0; i < 64; i++) if (coefficients[i] >= median) expected |= 1UL << (63 - i);
        Assert.Equal(expected, actual.Bits);
    }

    [Theory]
    [InlineData(0UL, 0UL, 0)]
    [InlineData(0UL, 1UL, 1)]
    [InlineData(0UL, 0xFFFFFFFFUL, 32)]
    [InlineData(0UL, ulong.MaxValue, 64)]
    public void 汉明距离覆盖端点(ulong left, ulong right, int expected)
    {
        var distance = new FingerprintDistanceCalculator().Calculate(new(FingerprintAlgorithmId.AverageHash, left), new(FingerprintAlgorithmId.AverageHash, right));
        Assert.Equal(expected, distance.Distance);
        Assert.Equal(100d * (64 - expected) / 64d, distance.BitSimilarityPercent, 10);
    }

    [Fact]
    public void 不同算法结构化拒绝且策略总览覆盖分歧()
    {
        Assert.Throws<ArgumentException>(() => new FingerprintDistanceCalculator().Calculate(new(FingerprintAlgorithmId.AverageHash, 0), new(FingerprintAlgorithmId.DifferenceHash, 0)));
        var policy = new FingerprintDecisionPolicy();
        Assert.Equal(FingerprintDecision.ExactFingerprintMatch, policy.Decide(FingerprintAlgorithmId.AverageHash, new(0)));
        Assert.Equal(FingerprintDecision.NearUnderReferencePolicy, policy.Decide(FingerprintAlgorithmId.AverageHash, new(8)));
        Assert.Equal(FingerprintDecision.NotNearUnderReferencePolicy, policy.Decide(FingerprintAlgorithmId.AverageHash, new(9)));
        Assert.Equal(FingerprintOverview.Divergent, policy.Summarize([FingerprintDecision.ExactFingerprintMatch, FingerprintDecision.NotNearUnderReferencePolicy]));
        Assert.Equal(FingerprintOverview.Incomplete, policy.Summarize([FingerprintDecision.NotComparable]));
    }

    private static double[] IndependentDct(IReadOnlyList<byte> source)
    {
        var result = new double[64];
        for (var v = 0; v < 8; v++) for (var u = 0; u < 8; u++)
        {
            double sum = 0;
            for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
                sum += source[(y * 32) + x] * Math.Cos(((2 * x + 1) * u * Math.PI) / 64d) * Math.Cos(((2 * y + 1) * v * Math.PI) / 64d);
            var au = u == 0 ? 1d / Math.Sqrt(32d) : Math.Sqrt(2d / 32d);
            var av = v == 0 ? 1d / Math.Sqrt(32d) : Math.Sqrt(2d / 32d);
            result[(v * 8) + u] = au * av * sum;
        }
        return result;
    }
}
