using ImageLabPlugin.Domain.Fingerprinting;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Robustness.Operators;
using ImageLabPlugin.Domain.Watermarking;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>冻结 V1 参考阈值的离线、可重复校准清单；它只证明参考样本，不把阈值包装成普适概率。</summary>
public sealed class FingerprintReferencePolicyCalibrationTests
{
    [Fact]
    public async Task 参考阈值容纳轻度缩放亮度并拒绝反相结构()
    {
        var pixels = Enumerable.Range(0, 32 * 32).Select(index => (byte)((index * 29 + (index / 32) * 17 + ((index % 32) * (index / 32))) % 216 + 20)).ToArray();
        var baseline = FingerprintNormalizationTests.GrayImage(32, 32, pixels);
        var key = new RobustnessCaseKey(EmbeddingProfileId.Balanced, 0, 0, 0);
        var scaled = await new ScaleOperator().ApplyAsync(baseline, new ScaleParameters(0.75m, 0.75m), new(0, key, "scale", PerturbationKind.Scale), default);
        var bright = await new BrightnessOperator().ApplyAsync(baseline, new BrightnessParameters(10), new(0, key, "brightness", PerturbationKind.Brightness), default);
        var inverted = FingerprintNormalizationTests.GrayImage(32, 32, pixels.Select(value => (byte)(255 - value)).ToArray());
        var policy = new FingerprintDecisionPolicy(); var calculator = new FingerprintDistanceCalculator();

        foreach (var algorithm in FingerprintNormalizationTests.CreateAlgorithms())
        {
            var original = algorithm.Compute(baseline);
            var scaleDistance = calculator.Calculate(original, algorithm.Compute(scaled));
            var brightnessDistance = calculator.Calculate(original, algorithm.Compute(bright));
            var negativeDistance = calculator.Calculate(original, algorithm.Compute(inverted));
            Assert.True(scaleDistance.Distance <= policy.GetThreshold(algorithm.Id), $"{algorithm.Id} 缩放距离 {scaleDistance.Distance}");
            Assert.True(brightnessDistance.Distance <= policy.GetThreshold(algorithm.Id), $"{algorithm.Id} 亮度距离 {brightnessDistance.Distance}");
            Assert.True(negativeDistance.Distance > policy.GetThreshold(algorithm.Id), $"{algorithm.Id} 反相距离 {negativeDistance.Distance}");
        }
    }
}
