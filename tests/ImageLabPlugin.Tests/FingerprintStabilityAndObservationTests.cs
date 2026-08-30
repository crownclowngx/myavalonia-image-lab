using ImageLabPlugin.Application.Fingerprinting;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.Robustness;
using ImageLabPlugin.Domain.Fingerprinting;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Robustness.Operators;
using ImageLabPlugin.Infrastructure.Fingerprinting;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class FingerprintStabilityAndObservationTests
{
    [Fact]
    public void 鲁棒性同图观测三算法距离均为零且不产生综合结论()
    {
        var algorithms = FingerprintNormalizationTests.CreateAlgorithms();
        var probe = new FingerprintObservationProbe(algorithms, new());
        var image = FingerprintNormalizationTests.GrayImage(9, 8, Enumerable.Range(0, 72).Select(value => (byte)(value * 3)).ToArray());
        var result = probe.Observe(image, image.Clone(), algorithms.Select(value => value.Id).ToArray(), default);
        Assert.Equal(3, result.Count);
        Assert.All(result, value => Assert.Equal(0, value.Distance.Distance));
    }

    [Fact]
    public async Task 固定信道复用缩放亮度与中心裁剪且不修改源图()
    {
        var channel = new FingerprintStabilityChannel(new NeverCodec(), [new ScaleOperator(), new BrightnessOperator(), new CropOperator()]);
        var source = FingerprintNormalizationTests.GrayImage(10, 8, Enumerable.Range(0, 80).Select(value => (byte)value).ToArray());
        var before = source.Rgba.ToArray();
        var scaled = await channel.ApplyAsync(source, FingerprintStabilityKind.Scale, 0.5m, default);
        var bright = await channel.ApplyAsync(source, FingerprintStabilityKind.Brightness, 10m, default);
        var cropped = await channel.ApplyAsync(source, FingerprintStabilityKind.CenterCrop, 10m, default);
        Assert.Equal(new ImageSize(5, 4), scaled.Image.Size);
        Assert.Equal(source.Size, bright.Image.Size);
        Assert.Equal(new ImageSize(8, 6), cropped.Image.Size);
        Assert.Equal(before, source.Rgba.ToArray());
    }

    [Fact]
    public async Task JPEG透明输入可见阻断且不会调用编码器()
    {
        var codec = new CountingCodec();
        var channel = new FingerprintStabilityChannel(codec, [new ScaleOperator(), new BrightnessOperator(), new CropOperator()]);
        var transparent = new PixelImage(new ImageSize(1, 1), [1, 2, 3, 254]);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await channel.ApplyAsync(transparent, FingerprintStabilityKind.Jpeg, 80, default));
        Assert.Contains("透明", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, codec.EncodeCalls);
    }

    [Fact]
    public async Task 稳定性用例串行保留点事实与单张当前预览()
    {
        var algorithms = FingerprintNormalizationTests.CreateAlgorithms();
        var image = FingerprintNormalizationTests.GrayImage(8, 8, Enumerable.Range(0, 64).Select(value => (byte)(value * 4)).ToArray());
        var comparisonResults = algorithms.Select(algorithm =>
        {
            var fingerprint = algorithm.Compute(image);
            return new FingerprintAlgorithmResult(algorithm.Id, fingerprint, fingerprint, new(0), new FingerprintDecisionPolicy().GetThreshold(algorithm.Id), FingerprintDecision.ExactFingerprintMatch, TimeSpan.Zero, string.Empty);
        }).ToArray();
        var facts = new FingerprintImageFacts("a.png", image.Size, false);
        using var session = new FingerprintComparisonSession(image, image.Clone(), image.Clone(), image.Clone(), new(
            FingerprintLumaNormalizer.NormalizationId, FingerprintDecisionPolicy.PolicyId, facts, facts, comparisonResults,
            FingerprintOverview.ConsistentlyNear, DateTimeOffset.UnixEpoch, "限制"));
        var channel = new CloneChannel();
        var useCase = new RunFingerprintStabilityUseCase(channel, algorithms, new(), new(), new());
        var result = await useCase.ExecuteAsync(session, new(FingerprintStabilityKind.Brightness, [-10m, 0m, 10m]), null, default);
        Assert.True(result.IsComplete);
        Assert.Equal(3, result.Points.Count);
        Assert.Equal(3, channel.Calls);
        Assert.NotNull(result.CurrentSamplePreview);
        Assert.All(result.Points, point => Assert.All(point.Algorithms, value => Assert.Equal(0, value.Distance.Distance)));
    }

    private sealed class CloneChannel : IFingerprintStabilityChannel
    { public int Calls { get; private set; } public ValueTask<FingerprintStabilitySample> ApplyAsync(PixelImage source, FingerprintStabilityKind kind, decimal value, CancellationToken cancellationToken) { Calls++; return ValueTask.FromResult(new FingerprintStabilitySample(source.Clone(), null)); } }
    private sealed class NeverCodec : IImageCodec
    {
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class CountingCodec : IImageCodec
    {
        public int EncodeCalls { get; private set; }
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken) { EncodeCalls++; return Task.FromResult(Array.Empty<byte>()); }
    }
}
