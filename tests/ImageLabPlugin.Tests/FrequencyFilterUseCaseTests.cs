using ImageLabPlugin.Application.FrequencyFiltering;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Convolution;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>Session、缓存、原尺寸预算与 stale 导出的应用层门禁。</summary>
public sealed class FrequencyFilterUseCaseTests
{
    [Fact]
    public async Task 准备用例只解码一次并建立代理频谱()
    {
        var codec = new MemoryCodec(FrequencyFilterDomainTests.Solid(8, 4, 10, 20, 30, 255));
        using var session = await Prepare(codec).ExecuteAsync(new("source.png", ImageChannel.Luma, 512), default);
        Assert.Equal(1, codec.DecodeCount); Assert.Equal(new ImageSize(8, 4), session.AnalysisProxy.Size);
        Assert.Equal(8, session.Spectrum.PaddedWidth); Assert.Equal(4, session.Spectrum.PaddedHeight);
        Assert.NotNull(session.MagnitudePreview);
    }

    [Fact]
    public async Task 输出模式变化复用raw而数学变化重做IFFT()
    {
        var codec = new MemoryCodec(FrequencyFilterDomainTests.Solid(8, 8, 50, 30, 20, 255));
        using var session = await Prepare(codec).ExecuteAsync(new("source.png", ImageChannel.Red, 512), default);
        var apply = Apply();
        var direct = Recipe(FrequencyProjectionMode.Direct, 1, 0.3);
        var first = await apply.ExecuteAsync(session, direct, default);
        var centered = Recipe(FrequencyProjectionMode.Centered, 2, 0.3);
        var second = await apply.ExecuteAsync(session, centered, default);
        var changed = await apply.ExecuteAsync(session, Recipe(FrequencyProjectionMode.Direct, 1, 0.4), default);
        Assert.False(first.Timings.UsedCachedRaw); Assert.True(second.Timings.UsedCachedRaw); Assert.False(changed.Timings.UsedCachedRaw);
        Assert.Equal(first.Raw.Values.ToArray(), second.Raw.Values.ToArray());
    }

    [Fact]
    public async Task Session释放后所有用例拒绝继续使用()
    {
        var codec = new MemoryCodec(FrequencyFilterDomainTests.Solid(4, 4, 50, 30, 20, 255));
        var session = await Prepare(codec).ExecuteAsync(new("source.png", ImageChannel.Red, 512), default); session.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Apply().ExecuteAsync(session, Recipe(), default));
    }

    [Fact]
    public async Task 原尺寸预算内执行并明确标识完整结果()
    {
        var codec = new MemoryCodec(FrequencyFilterDomainTests.Solid(8, 8, 50, 30, 20, 200));
        using var session = await Prepare(codec).ExecuteAsync(new("source.png", ImageChannel.Red, 512), default);
        var useCase = new RenderFullFrequencyFilterUseCase(new ImageChannelConverter(), Builder(), Apply());
        var result = await useCase.ExecuteAsync(session, Recipe(), default);
        Assert.True(result.IsFullSize); Assert.Equal(session.SourceImage.Size, result.Projection.Image.Size);
        Assert.Equal(200, result.Projection.Image.GetAlpha(0, 0));
    }

    [Fact]
    public async Task 导出同时校验Session和配方指纹并走原子端口()
    {
        var codec = new MemoryCodec(FrequencyFilterDomainTests.Solid(4, 4, 50, 30, 20, 255)); var writer = new MemoryWriter();
        using var session = await Prepare(codec).ExecuteAsync(new("source.png", ImageChannel.Red, 512), default);
        var recipe = Recipe(); var result = await Apply().ExecuteAsync(session, recipe, default);
        var export = new ExportFrequencyFilterImageUseCase(codec, writer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => export.ExecuteAsync(new(result, "wrong", recipe.Fingerprint(), "out.png"), default));
        var saved = await export.ExecuteAsync(new(result, session.SessionFingerprint, recipe.Fingerprint(), "out.png"), default);
        Assert.Equal("out.png", saved.OutputPath); Assert.Equal(1, writer.WriteCount); Assert.NotEmpty(writer.Content);
    }

    [Fact]
    public async Task 空间比较用例不重新解码且返回有限核近似()
    {
        var codec = new MemoryCodec(FrequencyFilterDomainTests.Solid(8, 8, 50, 30, 20, 255));
        using var session = await Prepare(codec).ExecuteAsync(new("source.png", ImageChannel.Red, 512), default);
        var fft = new Fft2DTransform(new Fft1DTransform());
        var useCase = new CompareFrequencySpatialUseCase(new FrequencyFilterMaskFactory(new RadialFilterResponse()), Builder(),
            new FrequencySpatialComparator(new FrequencyFilterEngine(fft), new SpatialConvolver(), new FrequencyImpulseResponseFactory(fft)));
        var result = await useCase.ExecuteAsync(session, Recipe(), 7, default);
        Assert.Equal(1, codec.DecodeCount); Assert.Equal(7, result.ImpulseKernel.Kernel.Size); Assert.True(result.MeanAbsoluteError >= 0);
    }

    private static FrequencyFilterRecipe Recipe(FrequencyProjectionMode mode = FrequencyProjectionMode.Direct,
        double gain = 1, double cutoff = 0.3) => new(FrequencyFilterKind.LowPass, FrequencyFilterFamily.Gaussian,
        cutoff, 0.8, 1, mode, gain, ImageChannel.Red);

    private static FrequencySpectrumBuilder Builder() => new(new Fft2DTransform(new Fft1DTransform()));
    private static PrepareFrequencyFilterSessionUseCase Prepare(IImageCodec codec) => new(codec,
        new ImageAnalysisProxyProjector(), new ImageChannelConverter(), Builder(), new SpectrumProjector());
    private static ApplyFrequencyFilterUseCase Apply()
    {
        var converter = new ImageChannelConverter();
        return new ApplyFrequencyFilterUseCase(new FrequencyFilterMaskFactory(new RadialFilterResponse()),
            new FrequencyFilterEngine(new Fft2DTransform(new Fft1DTransform())), new FrequencySignalProjector(converter),
            new FrequencyDifferenceProjector(), new FrequencySideEffectAnalyzer(),
            new FullReferenceQualityAnalyzer(new ImagePairValidator()));
    }

    private sealed class MemoryCodec(PixelImage image) : IImageCodec
    {
        public int DecodeCount { get; private set; }
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken) { DecodeCount++; return Task.FromResult(image.Clone()); }
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) => Task.FromResult(image.Clone());
        public Task<byte[]> EncodeAsync(PixelImage value, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken) => Task.FromResult(value.Rgba.ToArray());
    }
    private sealed class MemoryWriter : IAtomicFileWriter
    {
        public int WriteCount { get; private set; } public byte[] Content { get; private set; } = [];
        public Task WriteAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        { WriteCount++; Content = content.ToArray(); return Task.CompletedTask; }
    }
}
