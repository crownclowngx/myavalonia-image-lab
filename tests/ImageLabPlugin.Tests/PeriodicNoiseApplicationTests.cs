using System.Text;
using ImageLabPlugin.Application.PeriodicNoiseRemoval;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.PeriodicNoiseRemoval;
using ImageLabPlugin.Infrastructure.Persistence;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>周期噪声 Session、重建、草案导出门禁、严格配方和原子端口测试。</summary>
public sealed class PeriodicNoiseApplicationTests
{
    [Fact]
    public async Task 准备只解码一次且检测不修改频谱()
    {
        var codec = new MemoryCodec(Pattern(32, 32));
        using var session = await Prepare(codec).ExecuteAsync(new("source.png", ImageChannel.Luma, 512), default);
        var before = session.Spectrum.Values.ToArray();
        var result = await new DetectPeriodicNoiseCandidatesUseCase(new PeriodicPeakDetector(
            new RadialSpectrumBaseline(), new PeriodicPeakRiskAssessor())).ExecuteAsync(session, new(), default);
        Assert.Equal(1, codec.DecodeCount);
        Assert.Equal(before, session.Spectrum.Values.ToArray());
        Assert.InRange(result.Candidates.Count, 0, 64);
    }

    [Fact]
    public async Task 草案预览禁止导出采用结果才可通过指纹门禁()
    {
        var codec = new MemoryCodec(Pattern(16, 16));
        var writer = new MemoryWriter();
        using var session = await Prepare(codec).ExecuteAsync(new("source.png", ImageChannel.Luma, 512), default);
        var recipe = Recipe();
        var draft = await Renderer().ExecuteAsync(session, recipe, [], isDraft: true, default);
        var export = new ExportPeriodicNoiseArtifactUseCase(codec, writer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => export.ExecuteAsync(new(draft,
            session.SessionFingerprint, recipe.Fingerprint(), PeriodicNoiseExportArtifact.Reconstruction,
            "draft.png"), default));

        var accepted = await Renderer().ExecuteAsync(session, recipe, [], isDraft: false, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => export.ExecuteAsync(new(accepted,
            "stale", recipe.Fingerprint(), PeriodicNoiseExportArtifact.Reconstruction, "bad.png"), default));
        var saved = await export.ExecuteAsync(new(accepted, session.SessionFingerprint, recipe.Fingerprint(),
            PeriodicNoiseExportArtifact.MaskPreview, "mask.png"), default);
        Assert.Equal(PeriodicNoiseExportArtifact.MaskPreview, saved.Artifact);
        Assert.Equal(1, writer.WriteCount);
    }

    [Theory]
    [InlineData(ImageChannel.Red)]
    [InlineData(ImageChannel.Green)]
    [InlineData(ImageChannel.Blue)]
    [InlineData(ImageChannel.Luma)]
    [InlineData(ImageChannel.ChromaBlue)]
    [InlineData(ImageChannel.ChromaRed)]
    internal async Task 六通道重建保持Alpha并报告有限诊断(ImageChannel channel)
    {
        var codec = new MemoryCodec(Solid(16, 16, 60, 90, 130, 77));
        using var session = await Prepare(codec).ExecuteAsync(new("source.png", channel, 512), default);
        var recipe = new PeriodicNoiseRecipe(channel, PeriodicNotchTransition.Gaussian, 0.01, 0.7, 1,
            [new(new(-0.125, 0), PeriodicNotchOrigin.Manual)]);
        var result = await Renderer().ExecuteAsync(session, recipe, [], false, default);
        for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++)
            Assert.Equal(77, result.Reconstruction.GetAlpha(x, y));
        Assert.InRange(result.Diagnostics.MaximumImaginaryResidual, 0, 1e-8);
        Assert.InRange(result.Diagnostics.RemovedSpectrumEnergyRatio, 0, 1);
    }

    [Fact]
    public async Task 原尺寸结果明确标识且释放Session后用例拒绝执行()
    {
        var codec = new MemoryCodec(Pattern(16, 16));
        var session = await Prepare(codec).ExecuteAsync(new("source.png", ImageChannel.Luma, 512), default);
        var full = new RenderFullPeriodicNoiseResultUseCase(new ImageChannelConverter(), Builder(),
            (RenderPeriodicNoisePreviewUseCase)Renderer());
        var result = await full.ExecuteAsync(session, Recipe(), [], default);
        Assert.True(result.IsFullSize);
        Assert.False(result.IsDraft);
        session.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Renderer().ExecuteAsync(session, Recipe(), [],
            false, default));
    }

    [Fact]
    public void 频谱单击映射使用统一显示坐标并自动canonical()
    {
        using var session = SessionForMapping();
        var mapper = new MapPeriodicSpectrumSelectionUseCase();
        Assert.Equal(new PeriodicFrequency(0, 0), mapper.Execute(session, 0.5, 0.5));
        var point = mapper.Execute(session, 0.75, 0.5);
        Assert.Equal(PeriodicFrequency.Canonical(new(0.25, 0)), point);
        Assert.Throws<ArgumentOutOfRangeException>(() => mapper.Execute(session, -0.1, 0.5));
    }

    [Fact]
    public async Task 配方严格往返拒绝未知重复字段和错误指纹()
    {
        var serializer = new PeriodicNoiseRecipeSerializer();
        var recipe = Recipe();
        var json = serializer.Serialize(recipe);
        Assert.Equal(recipe.Fingerprint(), serializer.Deserialize(json).Fingerprint());
        var text = Encoding.UTF8.GetString(json);
        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(Encoding.UTF8.GetBytes(
            text.Replace("\"productId\"", "\"unknown\":1,\"productId\"", StringComparison.Ordinal))));
        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(Encoding.UTF8.GetBytes(
            text.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1,\"schemaVersion\": 1", StringComparison.Ordinal))));
        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(Encoding.UTF8.GetBytes(
            text.Replace(recipe.Fingerprint(), "0000000000000000", StringComparison.Ordinal))));

        var reader = new MemoryReader(json);
        var imported = await new ImportPeriodicNoiseRecipeUseCase(reader, serializer)
            .ExecuteAsync("recipe.json", default);
        Assert.Equal(recipe.Fingerprint(), imported.Fingerprint());
        Assert.Equal(1024 * 1024, reader.MaximumBytes);
    }

    [Fact]
    public async Task 配方和候选摘要通过各自原子端口且摘要不含源路径()
    {
        var writer = new MemoryWriter();
        var recipeSerializer = new PeriodicNoiseRecipeSerializer();
        await new ExportPeriodicNoiseRecipeUseCase(recipeSerializer, writer).ExecuteAsync(Recipe(),
            "recipe.json", default);
        Assert.Contains("periodic-noise-removal", Encoding.UTF8.GetString(writer.Content));

        using var session = SessionForMapping();
        var candidate = new PeriodicFrequencyCandidate(new(-0.125, 0), new(0.125, 0), 10, 2, 0.9,
            PeriodicPeakRiskLevel.Low, PeriodicPeakRiskReason.None, 1);
        var detection = new PeriodicNoiseDetectionResult([candidate],
            [new(candidate.CanonicalFrequency, PeriodicNotchOrigin.Automatic)]);
        await new ExportPeriodicNoiseCandidateSummaryUseCase(new PeriodicNoiseCandidateSummarySerializer(), writer)
            .ExecuteAsync(session, detection, "candidates.json", default);
        var summary = Encoding.UTF8.GetString(writer.Content);
        Assert.Contains("robustScore", summary);
        Assert.DoesNotContain(session.SourcePath, summary);
    }

    private static PeriodicNoiseRecipe Recipe() => new(ImageChannel.Luma, PeriodicNotchTransition.Gaussian,
        0.01, 0.8, 1, [new(new(-0.125, 0), PeriodicNotchOrigin.Manual)]);

    private static PreparePeriodicNoiseSessionUseCase Prepare(IImageCodec codec) => new(codec,
        new ImageAnalysisProxyProjector(), new ImageChannelConverter(), Builder(), new SpectrumProjector());

    private static IRenderPeriodicNoisePreviewUseCase Renderer()
    {
        var converter = new ImageChannelConverter();
        return new RenderPeriodicNoisePreviewUseCase(new NotchMaskFactory(new NotchResponse()),
            new FrequencyMaskApplier(PeriodicNoiseDomainTests.Fft()),
            new FrequencyGainSpectrumProjector(new SpectrumProjector()), converter,
            new FrequencyDifferenceProjector(), new FullReferenceQualityAnalyzer(new ImagePairValidator()),
            new PeriodicNoiseLossAnalyzer());
    }

    private static FrequencySpectrumBuilder Builder() => new(PeriodicNoiseDomainTests.Fft());

    private static PeriodicNoiseSession SessionForMapping()
    {
        var image = Solid(8, 8, 10, 20, 30, 255);
        var converter = new ImageChannelConverter();
        var plane = converter.Extract(image, ImageChannel.Luma);
        var spectrum = Builder().Build(plane);
        return new PeriodicNoiseSession("secret-source.png", image, image, plane, spectrum, image, 512);
    }

    private static PixelImage Solid(int width, int height, byte r, byte g, byte b, byte a)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        { rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = a; }
        return new PixelImage(new ImageSize(width, height), rgba);
    }

    private static PixelImage Pattern(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var i = (y * width) + x;
            rgba[i * 4] = (byte)(100 + (30 * Math.Cos(2 * Math.PI * 3 * x / width)));
            rgba[(i * 4) + 1] = (byte)((x * 7 + y * 3) % 256);
            rgba[(i * 4) + 2] = (byte)((x * 5 + y * 11) % 256);
            rgba[(i * 4) + 3] = (byte)(80 + (i % 170));
        }
        return new PixelImage(new ImageSize(width, height), rgba);
    }

    private sealed class MemoryCodec(PixelImage image) : IImageCodec
    {
        public int DecodeCount { get; private set; }
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken)
        { DecodeCount++; return Task.FromResult(image.Clone()); }
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) =>
            Task.FromResult(image.Clone());
        public Task<byte[]> EncodeAsync(PixelImage value, ImageOutputFormat format, int jpegQuality,
            CancellationToken cancellationToken) => Task.FromResult(value.Rgba.ToArray());
    }

    private sealed class MemoryWriter : IAtomicFileWriter
    {
        public int WriteCount { get; private set; }
        public byte[] Content { get; private set; } = [];
        public Task WriteAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        { WriteCount++; Content = content.ToArray(); return Task.CompletedTask; }
    }

    private sealed class MemoryReader(byte[] content) : ITextFileReader
    {
        public int MaximumBytes { get; private set; }
        public Task<byte[]> ReadAsync(string path, int maximumBytes, CancellationToken cancellationToken)
        { MaximumBytes = maximumBytes; return Task.FromResult(content); }
    }
}
