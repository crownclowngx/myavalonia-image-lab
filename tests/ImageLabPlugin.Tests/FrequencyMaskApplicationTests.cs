using ImageLabPlugin.Application.FrequencyMaskEditing;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.FrequencyMaskEditing;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Infrastructure.Persistence;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>共享 IFFT、Session、六通道回写、诊断、探针和 stale 导出的应用门禁。</summary>
public sealed class FrequencyMaskApplicationTests
{
    [Fact]
    public void 共享增益遮罩拒绝非有限越界和非共轭数据()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrequencyGainMask(2, 1, [double.NaN, double.NaN]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrequencyGainMask(2, 1, [-0.1, -0.1]));
        Assert.Throws<ArgumentException>(() => new FrequencyGainMask(4, 1, [1, 0, 1, 1]));
    }

    [Fact]
    public void FrequencyFilter与编辑器共享核心后全通不修改缓存频谱()
    {
        var values = Enumerable.Range(0, 64).Select(i => (double)i).ToArray();
        var plane = new ImageChannelPlane(new ImageSize(8, 8), ImageChannel.Red, values);
        var spectrum = Builder().Build(plane);
        var before = spectrum.Values.ToArray();
        var mask = new FrequencyGainMask(8, 8, Enumerable.Repeat(1d, 64).ToArray());
        var result = new FrequencyMaskApplier(Fft()).Apply(spectrum, mask);
        Assert.Equal(before, spectrum.Values.ToArray());
        Assert.InRange(result.MaximumImaginaryResidual, 0d, 1e-8);
        Assert.Equal(values, result.Values.ToArray(), new ApproximateDoubleComparer(1e-9));
    }

    [Fact]
    public async Task 准备用例只解码一次并建立独占代理Session()
    {
        var codec = new MemoryCodec(Solid(8, 4, 10, 20, 30, 200));
        using var session = await Prepare(codec).ExecuteAsync(new("source.png", ImageChannel.Luma, 512), default);
        Assert.Equal(1, codec.DecodeCount);
        Assert.Equal(new ImageSize(8, 4), session.AnalysisProxy.Size);
        Assert.NotNull(session.MagnitudePreview);
        Assert.True(session.CanRenderFullSize);
    }

    [Fact]
    public async Task 全通重建逐字节保持且质量指标为无差异()
    {
        var image = Pattern(8, 8);
        var codec = new MemoryCodec(image);
        using var session = await Prepare(codec).ExecuteAsync(new("source.png", ImageChannel.Luma, 512), default);
        var result = await Render().ExecuteAsync(session, new FrequencyMaskRecipe(1), default);
        Assert.Equal(image.Rgba.ToArray(), result.Reconstruction.Rgba.ToArray());
        Assert.Equal(0, result.Quality.ChangedPixelCountRgb);
        Assert.True(double.IsPositiveInfinity(result.Quality.PsnrLumaDb));
        Assert.Equal(0d, result.MaskStatistics.MaximumConjugateError);
    }

    [Theory]
    [InlineData(ImageChannel.Red)]
    [InlineData(ImageChannel.Green)]
    [InlineData(ImageChannel.Blue)]
    [InlineData(ImageChannel.Luma)]
    [InlineData(ImageChannel.ChromaBlue)]
    [InlineData(ImageChannel.ChromaRed)]
    internal async Task 六通道重建逐字节保持Alpha(ImageChannel channel)
    {
        var codec = new MemoryCodec(Solid(8, 8, 50, 80, 120, 77));
        using var session = await Prepare(codec).ExecuteAsync(new("source.png", channel, 512), default);
        var dc = new NormalizedFrequencyPoint(4d / 7d, 4d / 7d);
        var recipe = new FrequencyMaskRecipe(1, [FrequencyMaskOperation.Brush([dc], 0.001, 0, 1)]);
        var result = await Render().ExecuteAsync(session, recipe, default);
        for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++) Assert.Equal(77, result.Reconstruction.GetAlpha(x, y));
        Assert.InRange(result.Raw.MaximumImaginaryResidual, 0d, 1e-8);
    }

    [Fact]
    public async Task Session释放后渲染与探针均拒绝继续使用()
    {
        var codec = new MemoryCodec(Pattern(4, 4));
        var session = await Prepare(codec).ExecuteAsync(new("source.png", ImageChannel.Red, 512), default);
        var result = await Render().ExecuteAsync(session, new FrequencyMaskRecipe(1), default);
        session.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Render().ExecuteAsync(session, new FrequencyMaskRecipe(1), default));
        Assert.Throws<ObjectDisposedException>(() => new InspectFrequencyMaskPointUseCase().Execute(session, result, 0.5, 0.5));
    }

    [Fact]
    public async Task 探针返回显示自然共轭频率和两种增益()
    {
        var codec = new MemoryCodec(Pattern(8, 8));
        using var session = await Prepare(codec).ExecuteAsync(new("source.png", ImageChannel.Red, 512), default);
        var recipe = new FrequencyMaskRecipe(0.5, [FrequencyMaskOperation.Brush([new(4d / 7d, 4d / 7d)], 0.001, 0, 1)]);
        var result = await Render().ExecuteAsync(session, recipe, default);
        var point = new InspectFrequencyMaskPointUseCase().Execute(session, result, 4d / 7d, 4d / 7d);
        Assert.Equal(0, point.InternalX);
        Assert.Equal(0, point.ConjugateInternalX);
        Assert.Equal(0d, point.EditGain);
        Assert.Equal(0.5d, point.EffectiveGain);
    }

    [Fact]
    public async Task 完整尺寸结果明确标识且导出拒绝stale指纹()
    {
        var codec = new MemoryCodec(Pattern(8, 8));
        using var session = await Prepare(codec).ExecuteAsync(new("source.png", ImageChannel.Red, 512), default);
        var recipe = new FrequencyMaskRecipe(1);
        var full = await new RenderFullFrequencyMaskUseCase(new ImageChannelConverter(), Builder(), Render())
            .ExecuteAsync(session, recipe, default);
        Assert.True(full.IsFullSize);
        var writer = new MemoryWriter();
        var export = new ExportFrequencyMaskImageUseCase(codec, writer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => export.ExecuteAsync(
            new(full, "wrong", recipe.Fingerprint(), FrequencyMaskExportArtifact.Reconstruction, "out.png"), default));
        var saved = await export.ExecuteAsync(new(full, session.SessionFingerprint, recipe.Fingerprint(),
            FrequencyMaskExportArtifact.MaskPreview, "mask.png"), default);
        Assert.Equal(FrequencyMaskExportArtifact.MaskPreview, saved.Artifact);
        Assert.Equal(1, writer.WriteCount);
    }

    [Fact]
    public async Task 配方文件用例执行读写预算和原子端口()
    {
        var serializer = new FrequencyMaskRecipeSerializer();
        var recipe = new FrequencyMaskRecipe(0.4, [FrequencyMaskOperation.Invert()]);
        var reader = new MemoryReader(serializer.Serialize(recipe));
        var imported = await new ImportFrequencyMaskRecipeUseCase(reader, serializer).ExecuteAsync("recipe.json", default);
        Assert.Equal(recipe.Fingerprint(), imported.Fingerprint());
        var writer = new MemoryWriter();
        await new ExportFrequencyMaskRecipeUseCase(serializer, writer).ExecuteAsync(recipe, "recipe.json", default);
        Assert.NotEmpty(writer.Content);
        Assert.Equal(1024 * 1024, reader.ObservedMaximumBytes);
    }

    private static PrepareFrequencyMaskEditorSessionUseCase Prepare(IImageCodec codec) => new(codec,
        new ImageAnalysisProxyProjector(), new ImageChannelConverter(), Builder(), new SpectrumProjector());

    private static RenderFrequencyMaskUseCase Render() => new(new FrequencyMaskRasterizer(new ConjugateMaskWriter()),
        new FrequencyMaskApplier(Fft()), new ImageChannelConverter(), new FrequencyMaskDiagnostics(),
        new ChannelDifferenceProjector(), new FullReferenceQualityAnalyzer(new ImagePairValidator()));

    private static Fft2DTransform Fft() => new(new Fft1DTransform());
    private static FrequencySpectrumBuilder Builder() => new(Fft());

    private static PixelImage Solid(int width, int height, byte r, byte g, byte b, byte a)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++) { rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = a; }
        return new PixelImage(new ImageSize(width, height), rgba);
    }

    private static PixelImage Pattern(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        { rgba[i * 4] = (byte)(i * 3); rgba[i * 4 + 1] = (byte)(255 - i); rgba[i * 4 + 2] = (byte)(i * 7); rgba[i * 4 + 3] = (byte)(100 + (i % 100)); }
        return new PixelImage(new ImageSize(width, height), rgba);
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
        public int WriteCount { get; private set; }
        public byte[] Content { get; private set; } = [];
        public Task WriteAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        { WriteCount++; Content = content.ToArray(); return Task.CompletedTask; }
    }

    private sealed class MemoryReader(byte[] content) : ITextFileReader
    {
        public int ObservedMaximumBytes { get; private set; }
        public Task<byte[]> ReadAsync(string path, int maximumBytes, CancellationToken cancellationToken)
        { ObservedMaximumBytes = maximumBytes; return Task.FromResult(content); }
    }

    private sealed class ApproximateDoubleComparer(double tolerance) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) <= tolerance;
        public int GetHashCode(double obj) => 0;
    }
}
