using System.Text;
using System.Text.Json;
using ImageLabPlugin.Application.ImageComparison;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Infrastructure.Persistence;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>覆盖顺序解码、结构化不匹配、Session 释放和 JSON 隐私协议。</summary>
public sealed class ImageComparisonUseCaseTests
{
    [Fact]
    public async Task 准备用例按参考后候选顺序解码并建立完整Session()
    {
        var codec = new RecordingCodec(new Dictionary<string, PixelImage>
        {
            ["reference"] = Image(1, 1, [1, 2, 3, 4]),
            ["candidate"] = Image(1, 1, [2, 4, 6, 8])
        });
        var useCase = CreatePrepare(codec);

        var result = await useCase.ExecuteAsync(new ImageComparisonRequest("reference", "candidate"), CancellationToken.None);

        Assert.Equal(new[] { "reference", "candidate" }, codec.DecodedPaths);
        Assert.True(result.IsComparable);
        Assert.NotNull(result.Session!.Summary.Metrics);
        Assert.Equal(1, result.Session.Summary.Histograms!.Reference.GetBins(ImageChannel.Red).Sum());
        result.Session.Dispose();
    }

    [Fact]
    public async Task 尺寸不匹配保留双预览与原因但不建立Session或伪指标()
    {
        var codec = new RecordingCodec(new Dictionary<string, PixelImage>
        {
            ["reference"] = Image(2, 1, [0, 0, 0, 255, 0, 0, 0, 255]),
            ["candidate"] = Image(1, 1, [0, 0, 0, 255])
        });
        var result = await CreatePrepare(codec).ExecuteAsync(new ImageComparisonRequest("reference", "candidate"), CancellationToken.None);

        Assert.False(result.IsComparable);
        Assert.Null(result.Session);
        Assert.NotNull(result.Mismatch);
        Assert.Null(result.Summary.Metrics);
        Assert.Null(result.Summary.Histograms);
    }

    [Fact]
    public void Session释放后拒绝像素检查和投影访问()
    {
        var image = Image(1, 1, [0, 0, 0, 255]);
        var summary = ComparableSummary(image.Size, new FullReferenceQualityAnalyzer(new ImagePairValidator()).Analyze(image, image.Clone()),
            new ImageHistogramAnalyzer(new ImagePairValidator()).Analyze(image, image.Clone()));
        var session = new ImageComparisonSession(image, image.Clone(), image.Clone(), image.Clone(),
            new ImageDifferenceProxy(new ImageSize(1, 1), [0], [0], [0], [0], [0]), summary);
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => new InspectImagePairUseCase(new ImagePairPixelInspector(new ImagePairValidator())).Execute(session, new ImagePoint(0, 0)));
        Assert.Throws<ObjectDisposedException>(() => new ProjectImageDifferenceUseCase(new ImageDifferenceProxyProjector(), new DifferenceHeatmapProjector())
            .Execute(session, new DifferenceProjectionOptions(DifferenceProjectionKind.Rgb, 4), CancellationToken.None));
    }

    [Fact]
    public void 完全一致报告生成合法Json且不泄露绝对路径像素和堆栈()
    {
        var image = Image(1, 1, [10, 20, 30, 255]);
        var metrics = new FullReferenceQualityAnalyzer(new ImagePairValidator()).Analyze(image, image.Clone());
        var histograms = new ImageHistogramAnalyzer(new ImagePairValidator()).Analyze(image, image.Clone());
        var report = new ImageComparisonReport(1, "C:/secret/reference.png", "D:/private/candidate.png", DateTimeOffset.Parse("2026-08-30T00:00:00Z"), ComparableSummary(image.Size, metrics, histograms));
        var json = new ImageComparisonSummarySerializer().Serialize(report);
        using var parsed = JsonDocument.Parse(json);

        Assert.Equal(1, parsed.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(parsed.RootElement.GetProperty("metrics").GetProperty("psnrRgbDb").GetProperty("isInfinite").GetBoolean());
        Assert.Equal(JsonValueKind.Null, parsed.RootElement.GetProperty("metrics").GetProperty("psnrRgbDb").GetProperty("value").ValueKind);
        Assert.DoesNotContain("C:/secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("D:/private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("rgbaBytes", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pixels", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 摘要导出委托原子写入端口并使用Utf8()
    {
        var image = Image(1, 1, [0, 0, 0, 255]);
        var metrics = new FullReferenceQualityAnalyzer(new ImagePairValidator()).Analyze(image, image.Clone());
        var histograms = new ImageHistogramAnalyzer(new ImagePairValidator()).Analyze(image, image.Clone());
        var report = new ImageComparisonReport(1, "参考.png", "候选.png", DateTimeOffset.UnixEpoch, ComparableSummary(image.Size, metrics, histograms));
        var writer = new RecordingWriter();
        var useCase = new ExportComparisonSummaryUseCase(new ImageComparisonSummarySerializer(), writer);

        await useCase.ExecuteAsync(report, "target.json", CancellationToken.None);

        Assert.Equal("target.json", writer.Path);
        Assert.Contains("参考.png", Encoding.UTF8.GetString(writer.Content!), StringComparison.Ordinal);
    }

    private static PrepareImageComparisonUseCase CreatePrepare(IImageCodec codec)
    {
        var validator = new ImagePairValidator();
        return new PrepareImageComparisonUseCase(codec, validator, new ImageAnalysisProxyProjector(),
            new FullReferenceQualityAnalyzer(validator), new ImageHistogramAnalyzer(validator), new ImageDifferenceProxyAnalyzer(validator));
    }

    private static ImageComparisonSummary ComparableSummary(ImageSize size, FullReferenceQualityMetrics metrics, ImagePairHistograms histograms) =>
        new(ImageComparisonSummary.CurrentAlgorithmId, size, size, true, null,
            ImageComparisonSummary.CurrentColorFormulaId, ImageComparisonSummary.CurrentAlphaRule, metrics, histograms);

    private static PixelImage Image(int width, int height, byte[] rgba) => new(new ImageSize(width, height), rgba);

    private sealed class RecordingCodec(IReadOnlyDictionary<string, PixelImage> images) : IImageCodec
    {
        public List<string> DecodedPaths { get; } = [];
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); DecodedPaths.Add(path); return Task.FromResult(images[path].Clone()); }
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class RecordingWriter : IAtomicFileWriter
    {
        public string? Path { get; private set; }
        public byte[]? Content { get; private set; }
        public Task WriteAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        { Path = targetPath; Content = content.ToArray(); return Task.CompletedTask; }
    }
}
