using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.SpectralArt;
using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.SpectralArt;
using ImageLabPlugin.Infrastructure.Persistence;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>覆盖 Spectral Art 窄用例、Session 所有权、强度零、取消和 PNG 发布前回读。</summary>
public sealed class SpectralArtApplicationTests
{
    [Fact]
    public async Task 准备用例只解码一次并建立只读频谱事实()
    {
        var codec = new MemoryCodec(Carrier());
        using var session = await Prepare(codec).ExecuteAsync(new("carrier.png"), default);
        Assert.Equal(1, codec.PathDecodeCount); Assert.Equal(new ImageSize(64, 64), session.SourceImage.Size);
        Assert.Equal(64, session.Spectrum.PaddedWidth); Assert.False(string.IsNullOrWhiteSpace(session.SourceFingerprint));
        Assert.False(session.SourceImage.Rgba.Span.SequenceEqual(session.SourceSpectrumPreview.Rgba.Span));
    }

    [Fact]
    public async Task 文字Pattern只经过文字端口且不调用图片解码()
    {
        var codec = new MemoryCodec(Carrier()); var rasterizer = new RecordingTextRasterizer();
        var useCase = new CreateSpectralPatternUseCase(codec, rasterizer,
            new SpectralPatternNormalizer(new ImageAreaResampler()));
        var options = new SpectralPatternNormalizationOptions(SpectralPatternSourceKind.Text,
            SpectralPatternSamplingMode.BinaryNearest, 8, 8, 0.5d, false, SpectralPatternBackground.Black);
        var pattern = await useCase.ExecuteAsync(new(SpectralPatternSourceKind.Text, "TEST", string.Empty,
            "Fake Font", 32d, 700, 4, options), default);
        Assert.Equal(1, rasterizer.CallCount); Assert.Equal(0, codec.PathDecodeCount);
        Assert.Equal(SpectralPatternSourceKind.Text, pattern.SourceKind); Assert.Equal((8, 8), (pattern.Width, pattern.Height));
    }

    [Fact]
    public async Task 完整渲染只提交匹配Session的结果并保持Alpha()
    {
        using var session = await Prepare(new MemoryCodec(Carrier())).ExecuteAsync(new("carrier.png"), default);
        var recipe = Recipe(1.5d); var result = await Render().ExecuteAsync(session, recipe, default);
        Assert.Equal(session.SessionFingerprint, result.SessionFingerprint); Assert.Equal(recipe.Fingerprint(), result.RecipeFingerprint);
        Assert.Equal(session.SourceImage.Size, result.Output.Size); Assert.True(result.Frequency.TotalWrittenBins > 0);
        for (var i = 3; i < result.Output.Rgba.Length; i += 4) Assert.Equal(session.SourceImage.Rgba.Span[i], result.Output.Rgba.Span[i]);
        Assert.InRange(result.Raw.MaximumImaginaryResidual, 0d, FrequencyInverseTransformer.MaximumAllowedImaginaryResidual);
    }

    [Fact]
    public async Task 强度零输出逐字节等于源图且质量为无损()
    {
        using var session = await Prepare(new MemoryCodec(Carrier())).ExecuteAsync(new("carrier.png"), default);
        var result = await Render().ExecuteAsync(session, Recipe(0d), default);
        Assert.Equal(session.SourceImage.Rgba.ToArray(), result.Output.Rgba.ToArray());
        Assert.True(double.IsPositiveInfinity(result.Quality.PsnrRgbDb)); Assert.Equal(0, result.Frequency.TotalWrittenBins);
    }

    [Fact]
    public async Task 预取消不会返回部分Session或结果()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Prepare(new MemoryCodec(Carrier())).ExecuteAsync(new("carrier.png"), cancellation.Token));
        using var session = await Prepare(new MemoryCodec(Carrier())).ExecuteAsync(new("carrier.png"), default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Render().ExecuteAsync(session, Recipe(2d), cancellation.Token));
    }

    [Fact]
    public async Task Dispose后Session拒绝渲染()
    {
        var session = await Prepare(new MemoryCodec(Carrier())).ExecuteAsync(new("carrier.png"), default); session.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Render().ExecuteAsync(session, Recipe(2d), default));
    }

    [Fact]
    public async Task Png回读一致后才交给原子写入器()
    {
        var codec = new MemoryCodec(Carrier()); using var session = await Prepare(codec).ExecuteAsync(new("carrier.png"), default);
        var recipe = Recipe(0d); var result = await Render().ExecuteAsync(session, recipe, default); var writer = new RecordingWriter();
        await new ExportSpectralArtImageUseCase(codec, writer, ExportVerifier()).ExecuteAsync(session, result, recipe, "result.png", default);
        Assert.Equal("result.png", writer.Path); Assert.NotEmpty(writer.Content); Assert.Equal(1, codec.MemoryDecodeCount);
    }

    [Fact]
    public async Task Png回读不一致与源文件覆盖都不会发布()
    {
        var codec = new MemoryCodec(Carrier()); using var session = await Prepare(codec).ExecuteAsync(new("carrier.png"), default);
        var recipe = Recipe(0d); var result = await Render().ExecuteAsync(session, recipe, default); var writer = new RecordingWriter();
        var stale = Recipe(1d);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ExportSpectralArtImageUseCase(codec, writer, ExportVerifier()).ExecuteAsync(session, result, stale, "result.png", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ExportSpectralArtImageUseCase(codec, writer, ExportVerifier()).ExecuteAsync(session, result, recipe, "carrier.png", default));
        Assert.Null(writer.Path);
    }

    [Fact]
    public async Task 载体超过2048平方预算时在FFT分配前阻断()
    {
        var tooLarge = new PixelImage(new ImageSize(2049, 1025), new byte[2049 * 1025 * 4]); var codec = new MemoryCodec(tooLarge);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Prepare(codec).ExecuteAsync(new("large.png"), default));
        Assert.Equal(1, codec.PathDecodeCount);
    }

    [Fact]
    public async Task 脱敏报告可诚实序列化无损PSNR无穷值()
    {
        using var session = await Prepare(new MemoryCodec(Carrier())).ExecuteAsync(new("private-carrier.png"), default);
        var recipe = Recipe(0d); var result = await Render().ExecuteAsync(session, recipe, default);
        var report = new SpectralArtReport(SpectralArtProtocol.ReportProtocol, 1, result.SourceFingerprint,
            64, 64, 64, 64, recipe.Pattern.SourceKind, recipe.Pattern.Width, recipe.Pattern.Height,
            recipe.Pattern.Fingerprint, recipe.Region, recipe.Strength, result.Frequency, result.Raw,
            result.Quality, result.Timings, "不是识别率或安全证明");
        var serializer = new SpectralArtReportSerializer();
        var json = System.Text.Encoding.UTF8.GetString(serializer.SerializeJson(report));
        var csv = System.Text.Encoding.UTF8.GetString(serializer.SerializeCsv(report));
        Assert.Contains("Infinity", json, StringComparison.Ordinal); Assert.Contains("Infinity", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("private-carrier.png", json, StringComparison.Ordinal); Assert.DoesNotContain("private-carrier.png", csv, StringComparison.Ordinal);
    }

    private static PrepareSpectralArtCarrierUseCase Prepare(IImageCodec codec)
    {
        var fft = new Fft2DTransform(new Fft1DTransform());
        return new(codec, new ImageChannelConverter(), new FrequencySpectrumBuilder(fft), new SpectrumProjector());
    }

    private static RenderSpectralArtUseCase Render()
    {
        var radial = new RadialLogPowerBaseline(); var converter = new ImageChannelConverter();
        var inverse = new FrequencyInverseTransformer(new Fft2DTransform(new Fft1DTransform()));
        return new(new SpectralPatternMapper(), new SpectralAmplitudeWriter(radial),
            new SpectralArtReconstructor(inverse, converter),
            new SpectralArtDiagnostics(new FullReferenceQualityAnalyzer(new ImagePairValidator()), new FrequencyDifferenceProjector(), radial),
            new SpectrumProjector(), new SpectralPatternPreviewProjector());
    }

    private static SpectralExportFactVerifier ExportVerifier()
    {
        var converter = new ImageChannelConverter();
        return new SpectralExportFactVerifier(converter,
            new FrequencySpectrumBuilder(new Fft2DTransform(new Fft1DTransform())),
            new SpectralPatternMapper());
    }

    private static SpectralArtRecipe Recipe(double strength)
    {
        var values = Enumerable.Range(0, 64).Select(i => (i % 3) == 0 ? 1d : 0d).ToArray();
        return new(new SpectralPattern(8, 8, values, SpectralPatternSamplingMode.BinaryNearest, SpectralPatternSourceKind.Text),
            SpectralArtRegion.Default, SpectralPatternFitMode.Contain, strength);
    }

    private static PixelImage Carrier()
    {
        var rgba = new byte[64 * 64 * 4];
        for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
        { var offset = ((y * 64) + x) * 4; rgba[offset] = (byte)(32 + ((x * 3) % 190)); rgba[offset + 1] = (byte)(48 + ((y * 2) % 180)); rgba[offset + 2] = (byte)(64 + ((x + y) % 160)); rgba[offset + 3] = (byte)(128 + ((x + y) % 128)); }
        return new PixelImage(new ImageSize(64, 64), rgba);
    }

    private sealed class MemoryCodec(PixelImage image) : IImageCodec
    {
        private PixelImage? _lastEncoded;
        public int PathDecodeCount { get; private set; }
        public int MemoryDecodeCount { get; private set; }
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); PathDecodeCount++; return Task.FromResult(image.Clone()); }
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); MemoryDecodeCount++; return Task.FromResult((_lastEncoded ?? throw new InvalidOperationException()).Clone()); }
        public Task<byte[]> EncodeAsync(PixelImage value, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); _lastEncoded = value.Clone(); return Task.FromResult(new byte[] { 137, 80, 78, 71 }); }
    }

    private sealed class RecordingWriter : IAtomicFileWriter
    {
        public string? Path { get; private set; }
        public byte[] Content { get; private set; } = [];
        public Task WriteAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) { Path = targetPath; Content = content.ToArray(); return Task.CompletedTask; }
    }

    private sealed class RecordingTextRasterizer : ISpectralTextRasterizer
    {
        public int CallCount { get; private set; }
        public Task<PixelImage> RasterizeAsync(SpectralTextRasterRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new PixelImage(new ImageSize(2, 2),
                [0,0,0,255, 255,255,255,255, 255,255,255,255, 0,0,0,255]));
        }
    }
}
