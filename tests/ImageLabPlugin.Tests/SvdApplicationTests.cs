using System.Text;
using System.Text.Json;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.SvdDecomposition;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.SvdDecomposition;
using ImageLabPlugin.Infrastructure.Persistence;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class SvdApplicationTests
{
    [Fact]
    public async Task Prepare只解码一次并建立128代理且小图不放大()
    {
        var image = SolidImage(300, 150, 10, 20, 30, 40);
        var codec = new RecordingCodec(image);
        var useCase = new PrepareSvdSessionUseCase(codec, new ImageAreaResampler());

        using var session = await useCase.ExecuteAsync("source.png", 128, default);

        Assert.Equal(1, codec.DecodeCount);
        Assert.Equal(new ImageSize(128, 64), session.AnalysisProxy.Size);
        Assert.Equal(64, session.ProxyFingerprint.Length);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => useCase.ExecuteAsync("source.png", 512, default));
    }

    [Fact]
    public async Task 分解缓存键不含Rank且相同策略命中同一结果()
    {
        using var session = SessionFor(SampleImage());
        var useCase = CreateDecompose();
        var first = await useCase.ExecuteAsync(session, SvdColorStrategy.SingleChannel, ImageChannel.Luma, default);
        var second = await useCase.ExecuteAsync(session, SvdColorStrategy.SingleChannel, ImageChannel.Luma, default);

        Assert.Same(first, second);
        Assert.Equal(1, session.CachedDecompositionCount);
        var reconstruct = CreateReconstruct();
        var k0 = await reconstruct.ExecuteAsync(session, first, 0, default);
        var k2 = await reconstruct.ExecuteAsync(session, first, 2, default);
        Assert.Equal(1, session.CachedDecompositionCount);
        Assert.NotEqual(k0.RecipeFingerprint, k2.RecipeFingerprint);
    }

    [Fact]
    public async Task 三策略比较固定顺序且不产生最佳字段()
    {
        using var session = SessionFor(SampleImage());
        var comparison = await new CompareSvdStrategiesUseCase(CreateDecompose(), CreateReconstruct())
            .ExecuteAsync(session, 1, default);

        Assert.Equal(SvdComparisonCompletionStatus.Complete, comparison.CompletionStatus);
        Assert.Equal(new[] { SvdColorStrategy.SingleChannel, SvdColorStrategy.IndependentRgb,
            SvdColorStrategy.IndependentYCbCr }, comparison.Cases.Select(item => item.Strategy));
        Assert.Equal(new[] { 1, 3, 3 }, comparison.Cases.Select(item => item.MatrixCount));
        Assert.DoesNotContain(comparison.GetType().GetProperties(), property =>
            property.Name.Contains("Best", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task 报告将无穷PSNR结构化为null且CSV使用稳定记录顺序()
    {
        using var session = SessionFor(SampleImage());
        var decomposition = await CreateDecompose().ExecuteAsync(session, SvdColorStrategy.IndependentRgb, ImageChannel.Luma, default);
        var result = await CreateReconstruct().ExecuteAsync(session, decomposition, 2, default);
        var component = await new ProjectSvdComponentUseCase(new SvdComponentProjector())
            .ExecuteAsync(decomposition, 0, 0, default);
        var report = new SvdExperimentReport("image-lab.svd-report/1", SvdRecipeFingerprint.NumericProtocol,
            "source.png", session.SourceImage.Size, session.AnalysisProxy.Size, 128, decomposition, result, component,
            null, ["分析代理", "不是图片文件压缩器"], DateTimeOffset.UnixEpoch);
        var serializer = new SvdReportSerializer();

        var json = serializer.SerializeJson(report);
        using var document = JsonDocument.Parse(json);
        var text = Encoding.UTF8.GetString(json);
        Assert.DoesNotContain("Infinity", text, StringComparison.Ordinal);
        Assert.True(document.RootElement.GetProperty("imageQuality").GetProperty("psnrRgb").GetProperty("isExact").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("imageQuality").GetProperty("psnrRgb").GetProperty("psnrDb").ValueKind);
        var csv = Encoding.UTF8.GetString(serializer.SerializeCsv(report));
        Assert.True(csv.IndexOf("singular-value", StringComparison.Ordinal) < csv.IndexOf("rank-result", StringComparison.Ordinal));
        Assert.True(csv.IndexOf("rank-result", StringComparison.Ordinal) < csv.IndexOf("diagnostics", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PNG导出拒绝过期指纹和覆盖源路径并走原子写入()
    {
        var sourcePath = Path.GetFullPath("source.png");
        using var session = new SvdSession(sourcePath, SampleImage(), SampleImage(), 128, "proxy");
        var decomposition = await CreateDecompose().ExecuteAsync(session, SvdColorStrategy.SingleChannel, ImageChannel.Luma, default);
        var result = await CreateReconstruct().ExecuteAsync(session, decomposition, 1, default);
        var writer = new RecordingWriter();
        var useCase = new ExportSvdImageUseCase(new RecordingCodec(SampleImage()), writer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(session, result, "stale", "out.png", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(session, result, result.RecipeFingerprint, sourcePath, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(session, result,
            result.RecipeFingerprint, "out.jpg", default));
        await useCase.ExecuteAsync(session, result, result.RecipeFingerprint, "out.png", default);
        Assert.Equal("out.png", writer.Path);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, writer.Content);
    }

    [Fact]
    public void Session释放后清空缓存并阻断使用()
    {
        var session = SessionFor(SampleImage());
        session.Dispose();
        Assert.True(session.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => session.ThrowIfDisposed());
    }

    private static DecomposeSvdUseCase CreateDecompose() => new(
        new SvdColorStrategyExecutor(new ImageChannelConverter(), new JacobiSvdDecomposer()));

    private static ReconstructSvdRankUseCase CreateReconstruct() => new(
        new LowRankReconstructor(), new SvdImageReconstructor(new ImageChannelConverter()),
        new SvdReconstructionAnalyzer(new SingularValueEnergyAnalyzer(),
            new FullReferenceQualityAnalyzer(new ImagePairValidator())));

    private static SvdSession SessionFor(PixelImage image) => new("source.png", image, image.Clone(), 128, "proxy");

    private static PixelImage SampleImage() => new(new ImageSize(2, 2), new byte[]
    {
        10, 20, 30, 11, 40, 50, 60, 22,
        70, 80, 90, 33, 100, 110, 120, 44
    });

    private static PixelImage SolidImage(int width, int height, byte r, byte g, byte b, byte a)
    {
        var bytes = new byte[checked(width * height * 4)];
        for (var index = 0; index < width * height; index++)
        { var offset = index * 4; bytes[offset] = r; bytes[offset + 1] = g; bytes[offset + 2] = b; bytes[offset + 3] = a; }
        return new(new ImageSize(width, height), bytes);
    }

    private sealed class RecordingCodec(PixelImage image) : IImageCodec
    {
        public int DecodeCount { get; private set; }
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken)
        { DecodeCount++; return Task.FromResult(image.Clone()); }
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) =>
            Task.FromResult(image.Clone());
        public Task<byte[]> EncodeAsync(PixelImage value, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken) =>
            Task.FromResult(new byte[] { 137, 80, 78, 71 });
    }

    private sealed class RecordingWriter : IAtomicFileWriter
    {
        public string? Path { get; private set; }
        public byte[]? Content { get; private set; }
        public Task WriteAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        { Path = targetPath; Content = content.ToArray(); return Task.CompletedTask; }
    }
}
