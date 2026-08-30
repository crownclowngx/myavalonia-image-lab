using System.Text;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.Wavelets;
using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Wavelets;
using ImageLabPlugin.Infrastructure.Persistence;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Robustness.Operators;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class WaveletUseCaseAndReportTests
{
    [Fact]
    public async Task 代理分解去噪绑定同一指纹且释放会话后拒绝执行()
    {
        using var session = new WaveletSession("source.png", CreateImage(16, 16), CreateImage(16, 16), 512);
        var services = CreateUseCases();
        var recipe = CreateRecipe(2, 4d);
        var analysis = await services.Decompose.ExecuteAsync(session, recipe, false, 1,
            WaveletSubband.DiagonalDetail, WaveletProjectionMode.Symmetric, CancellationToken.None);
        var result = await services.Denoise.ExecuteAsync(session, analysis, recipe, CancellationToken.None);
        Assert.Equal(recipe.Fingerprint(), result.RecipeFingerprint); Assert.False(result.IsFullSize);
        Assert.Equal(new ImageSize(8, 8), analysis.Projection.Image.Size);
        session.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => services.Decompose.ExecuteAsync(session, recipe, false, 1,
            WaveletSubband.DiagonalDetail, WaveletProjectionMode.Symmetric, CancellationToken.None));
    }

    [Fact]
    public async Task 扫描上限固定顺序且取消保留部分结果()
    {
        using var session = new WaveletSession("source.png", CreateImage(32, 32), CreateImage(32, 32), 512);
        var services = CreateUseCases();
        var scan = new RunWaveletQualityScanUseCase(services.Decompose, services.Denoise);
        await Assert.ThrowsAsync<ArgumentException>(() => scan.ExecuteAsync(session, CreateRecipe(1, 1d),
            Enumerable.Range(0, 22).Select(value => (double)value).ToArray(), [1], CancellationToken.None));
        var result = await scan.ExecuteAsync(session, CreateRecipe(2, 1d), [0d, 2d], [1, 2], CancellationToken.None);
        Assert.Equal(new[] { (1, 0d), (1, 2d), (2, 0d), (2, 2d) }, result.Cases.Select(value => (value.Levels, value.Threshold)));
        Assert.False(result.Canceled); Assert.Contains("无干净参考图", result.MetricBoundary, StringComparison.Ordinal);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var canceled = await scan.ExecuteAsync(session, CreateRecipe(1, 1d), [1d], [1], cancellation.Token);
        Assert.True(canceled.Canceled); Assert.Empty(canceled.Cases);
    }

    [Fact]
    public async Task 有参考代理时扫描返回质量指标而不是伪空值()
    {
        var source = CreateImage(16, 16); var reference = CreateImage(16, 16);
        using var session = new WaveletSession("source.png", source, source.Clone(), 512, "clean.png", reference, reference.Clone());
        var services = CreateUseCases(); var scan = new RunWaveletQualityScanUseCase(services.Decompose, services.Denoise);
        var result = await scan.ExecuteAsync(session, CreateRecipe(1, 0d), [0d], [1], CancellationToken.None);
        Assert.Single(result.Cases); Assert.NotNull(result.Cases[0].PsnrLuma); Assert.NotNull(result.Cases[0].SsimLuma);
        Assert.Contains("参考代理", result.MetricBoundary, StringComparison.Ordinal);
    }

    [Fact]
    public void 报告Json可回读且Csv正确转义并保留限制字段()
    {
        var scanCase = new WaveletScanCase(0, 1, 2.5d, new(10, 5, 5), 0.1d, null, null, TimeSpan.FromMilliseconds(1));
        var report = new WaveletExperimentReport("wavelet-experiment-v1", "a,b.png", "abc", "Haar", "Luma", 1, 2.5d,
            [scanCase], null, ["无参考图不排序"], DateTimeOffset.UnixEpoch);
        var serializer = new WaveletExperimentReportSerializer();
        var json = Encoding.UTF8.GetString(serializer.SerializeJson(report));
        var csv = Encoding.UTF8.GetString(serializer.SerializeCsv(report));
        Assert.Contains("wavelet-experiment-v1", json, StringComparison.Ordinal);
        Assert.Contains("无参考图不排序", json, StringComparison.Ordinal);
        Assert.Contains("scan,0", csv, StringComparison.Ordinal); Assert.DoesNotContain("NaN", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 导出拒绝代理与过期指纹并固定Png()
    {
        var image = CreateImage(4, 4); var plane = new ImageChannelConverter().Extract(image, ImageChannel.Luma);
        var pyramid = new HaarWaveletTransform().Forward(plane, 1);
        var reconstruction = new WaveletReconstructionResult(plane, image, 0, 0, 0);
        var result = new WaveletDenoiseResult(pyramid, reconstruction, new(1, 1, 0), null, "fingerprint", false, TimeSpan.Zero);
        var codec = new RecordingCodec(); var writer = new RecordingWriter(); var useCase = new ExportWaveletImageUseCase(codec, writer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(result, "fingerprint", "out.png", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(result with { IsFullSize = true }, "stale", "out.png", CancellationToken.None));
        await useCase.ExecuteAsync(result with { IsFullSize = true }, "fingerprint", "out.png", CancellationToken.None);
        Assert.Equal(ImageOutputFormat.Png, codec.Format); Assert.Equal("out.png", writer.Path);
    }

    [Fact]
    public async Task 公平比较对两个载体执行完全相同的有限案例集合()
    {
        var image = CreateImage(2, 2); var calls = new List<PerturbationKind>();
        IImagePerturbationOperator[] operators =
        [
            new PassThroughOperator(PerturbationKind.JpegReencode, calls), new PassThroughOperator(PerturbationKind.Scale, calls),
            new PassThroughOperator(PerturbationKind.GaussianNoise, calls), new PassThroughOperator(PerturbationKind.GaussianBlur, calls),
            new PassThroughOperator(PerturbationKind.Brightness, calls), new PassThroughOperator(PerturbationKind.Contrast, calls)
        ];
        var useCase = new RunWatermarkCarrierBenchmarkUseCase(
            [new FakeCarrier("dct-frequency-qim-v1", 100), new FakeCarrier("dwt-pair-qim-v1", 100)], operators,
            new FullReferenceQualityAnalyzer(new ImagePairValidator()));
        var report = await useCase.ExecuteAsync(image, new byte[] { 1, 2 }, CancellationToken.None);
        var groups = report.Cases.GroupBy(item => item.CarrierId).Select(group => group.Select(item => item.CaseId).ToArray()).ToArray();
        Assert.Equal(2, groups.Length); Assert.Equal(groups[0], groups[1]); Assert.Equal(18, report.Cases.Count);
        Assert.Equal(18, calls.Count); Assert.Equal(6, calls.Count(value => value == PerturbationKind.JpegReencode));
    }

    [Fact]
    public async Task 任一载体容量不足时公平比较在扰动前阻断()
    {
        var useCase = new RunWatermarkCarrierBenchmarkUseCase(
            [new FakeCarrier("dct-frequency-qim-v1", 1), new FakeCarrier("dwt-pair-qim-v1", 100)], [],
            new FullReferenceQualityAnalyzer(new ImagePairValidator()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(CreateImage(2, 2),
            new byte[] { 1, 2 }, CancellationToken.None));
    }

    private static (IDecomposeWaveletUseCase Decompose, IDenoiseWaveletUseCase Denoise) CreateUseCases()
    {
        var haar = new HaarWaveletTransform(); var cdf = new Cdf53WaveletTransform();
        var catalog = new WaveletTransformCatalog([haar, cdf]); var channels = new ImageChannelConverter();
        var decompose = new DecomposeWaveletUseCase(channels, catalog, new WaveletNoiseEstimator(), new WaveletSubbandProjector());
        var denoise = new DenoiseWaveletUseCase(new WaveletThresholdProcessor(), new WaveletImageReconstructor(catalog, channels),
            new FullReferenceQualityAnalyzer(new ImagePairValidator()));
        return (decompose, denoise);
    }

    private static WaveletDenoiseRecipe CreateRecipe(int levels, double threshold) => new(WaveletTransformId.Haar,
        ImageChannel.Luma, levels, WaveletThresholdMode.Soft, WaveletThresholdSource.Manual, threshold,
        Enumerable.Range(1, levels), [WaveletSubband.HorizontalDetail, WaveletSubband.VerticalDetail, WaveletSubband.DiagonalDetail]);

    private static PixelImage CreateImage(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < rgba.Length; i += 4) { rgba[i] = (byte)(i % 251); rgba[i + 1] = (byte)((i * 3) % 251); rgba[i + 2] = (byte)((i * 7) % 251); rgba[i + 3] = 255; }
        return new(new(width, height), rgba);
    }

    private sealed class RecordingCodec : IImageCodec
    {
        public ImageOutputFormat Format { get; private set; }
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken)
        { Format = format; return Task.FromResult(new byte[] { 1, 2, 3 }); }
    }
    private sealed class RecordingWriter : IAtomicFileWriter
    {
        public string? Path { get; private set; }
        public Task WriteAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        { Path = targetPath; return Task.CompletedTask; }
    }

    private sealed record FakeContext : IWatermarkBenchmarkReadContext;
    private sealed class FakeCarrier(string id, int capacity) : IWatermarkBenchmarkCarrier
    {
        public string CarrierId => id;
        public WatermarkBenchmarkCapacity Estimate(PixelImage source, int payloadLength) => new(id, capacity);
        public Task<WatermarkBenchmarkEmbedding> EmbedAndReadAsync(PixelImage source, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
            Task.FromResult(new WatermarkBenchmarkEmbedding(source.Clone(), true, payload.ToArray(), 1d, 0d, new FakeContext()));
        public Task<WatermarkBenchmarkRead> ReadAsync(PixelImage image, WatermarkBenchmarkEmbedding baseline,
            ReadOnlyMemory<byte> expectedPayload, CancellationToken cancellationToken) => Task.FromResult(new WatermarkBenchmarkRead(true, 1d, 0d));
    }
    private sealed class PassThroughOperator(PerturbationKind kind, List<PerturbationKind> calls) : IImagePerturbationOperator
    {
        public PerturbationKind Kind => kind;
        public ValueTask<PixelImage> ApplyAsync(PixelImage source, PerturbationParameters parameters,
            DeterministicTrialContext trial, CancellationToken cancellationToken)
        { calls.Add(kind); return ValueTask.FromResult(source.Clone()); }
    }
}
