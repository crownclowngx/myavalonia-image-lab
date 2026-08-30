using ImageLabPlugin.Application.BitPlanes;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.BitPlanes;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Features.BitPlaneViewer;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>验证窄用例的所有权边界、PNG 原子导出和 Document 轻量快照。</summary>
public sealed class BitPlaneUseCaseAndDocumentTests
{
    [Fact]
    public async Task 准备用例只解码一次且切换通道复用同一会话()
    {
        var codec = new RecordingCodec(CreateImage());
        using var session = await new PrepareBitPlaneSessionUseCase(codec).ExecuteAsync("source.png", CancellationToken.None);
        var analyze = CreateAnalyzeUseCase();
        var red = await analyze.ExecuteAsync(session, BitPlaneChannel.Red, CancellationToken.None);
        var alpha = await analyze.ExecuteAsync(session, BitPlaneChannel.Alpha, CancellationToken.None);

        Assert.Equal(1, codec.DecodePathCalls);
        Assert.Equal(new byte[] { 0xAA, 0x10 }, red.Plane.Values.ToArray());
        Assert.Equal(new byte[] { 4, 8 }, alpha.Plane.Values.ToArray());
        Assert.Equal(8, red.Statistics.Count);
    }

    [Fact]
    public async Task 投影用例复用分析并以常数时间生成探针()
    {
        using var session = new BitPlaneSession("source.png", CreateImage());
        var analysis = await CreateAnalyzeUseCase().ExecuteAsync(session, BitPlaneChannel.Red, CancellationToken.None);
        var useCase = new ProjectBitPlaneViewUseCase(new BitPlaneProjector(), new BitPlanePixelInspector());
        var projection = await useCase.ExecuteAsync(session, analysis, new BitMask8(0x0F), 7, CancellationToken.None);
        var report = useCase.Inspect(session, analysis, new BitMask8(0x0F), 1, 0);

        Assert.Equal(new ImageSize(2, 1), projection.Source.Size);
        Assert.Equal(0, report.KeptValue);
        Assert.Equal(0x10, report.ChannelValue);
    }

    [Fact]
    public async Task 导出用例固定PNG并把完整结果交给原子写入端口()
    {
        var codec = new RecordingCodec(CreateImage());
        var writer = new RecordingWriter();
        using var session = new BitPlaneSession("source.png", CreateImage());
        var analysis = await CreateAnalyzeUseCase().ExecuteAsync(session, BitPlaneChannel.Alpha, CancellationToken.None);
        var useCase = new ExportBitPlaneImageUseCase(new BitPlaneReconstructor(), codec, writer);

        var result = await useCase.ExecuteAsync(session, analysis, new BitMask8(0x0F), "result.png", CancellationToken.None);

        Assert.Equal(ImageOutputFormat.Png, codec.LastFormat);
        Assert.Equal("result.png", writer.Path);
        Assert.Equal(codec.EncodedBytes, writer.Content);
        Assert.Equal(new ImageSize(2, 1), result.Size);
        Assert.Equal(new byte[] { 0xAA, 2, 3, 4, 0x10, 6, 7, 8 }, codec.LastEncodedImage!.Rgba.ToArray());
    }

    [Fact]
    public async Task 已释放会话拒绝分析投影和导出()
    {
        var session = new BitPlaneSession("source.png", CreateImage());
        var analysis = await CreateAnalyzeUseCase().ExecuteAsync(session, BitPlaneChannel.Red, CancellationToken.None);
        session.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => CreateAnalyzeUseCase().ExecuteAsync(session, BitPlaneChannel.Red, CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => new ProjectBitPlaneViewUseCase(new BitPlaneProjector(), new BitPlanePixelInspector())
            .ExecuteAsync(session, analysis, new BitMask8(1), 0, CancellationToken.None));
    }

    [Fact]
    public async Task 快照只保存路径和轻量参数且恢复不自动解码()
    {
        var codec = new RecordingCodec(CreateImage());
        using var source = CreateDocument(codec);
        await source.InitializeAsync(new NewDocumentActivation("位平面"), CancellationToken.None);
        source.SourcePath = "D:/missing/example.png";
        source.SelectedChannel = "Alpha";
        source.FocusedBit = 2;
        source.MaskValue = 0x55;
        source.ShowCheckerboard = false;
        var snapshot = await source.CaptureSaveSnapshotAsync(CancellationToken.None);
        var json = snapshot.Content.Payload.GetRawText();

        Assert.Contains("example.png", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Rgba", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Statistics", json, StringComparison.OrdinalIgnoreCase);
        Assert.True(source.IsDirty);

        using var restored = CreateDocument(codec);
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复位平面", snapshot.Content), CancellationToken.None);
        Assert.Equal("Alpha", restored.SelectedChannel);
        Assert.Equal(2, restored.FocusedBit);
        Assert.Equal(0x55, restored.MaskValue);
        Assert.False(restored.ShowCheckerboard);
        Assert.False(restored.IsDirty);
        Assert.Equal(0, codec.DecodePathCalls);
        Assert.Contains("不存在", restored.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 未知Schema安全回退到默认Y与bit7()
    {
        using var document = CreateDocument(new RecordingCodec(CreateImage()));
        var content = new DocumentContent(99, System.Text.Json.JsonSerializer.SerializeToElement(new { }));
        await document.InitializeAsync(new RestoreDocumentActivation("未知", content), CancellationToken.None);
        Assert.Equal("Y", document.SelectedChannel);
        Assert.Equal(7, document.FocusedBit);
        Assert.Equal(0x80, document.MaskValue);
        Assert.Contains("不支持 schema", document.StatusMessage, StringComparison.Ordinal);
    }

    private static AnalyzeBitPlaneChannelUseCase CreateAnalyzeUseCase() =>
        new(new BitPlaneChannelExtractor(), new BitPlaneStatisticsAnalyzer());

    private static BitPlaneViewerDocument CreateDocument(IImageCodec codec) => new(
        new PrepareBitPlaneSessionUseCase(codec),
        CreateAnalyzeUseCase(),
        new ProjectBitPlaneViewUseCase(new BitPlaneProjector(), new BitPlanePixelInspector()),
        new ExportBitPlaneImageUseCase(new BitPlaneReconstructor(), codec, new RecordingWriter()),
        new NullDialog(), codec, new Lifetime());

    private static PixelImage CreateImage() => new(new ImageSize(2, 1), [0xAA, 2, 3, 4, 0x10, 6, 7, 8]);

    private sealed class RecordingCodec(PixelImage decoded) : IImageCodec
    {
        public int DecodePathCalls { get; private set; }
        public ImageOutputFormat? LastFormat { get; private set; }
        public PixelImage? LastEncodedImage { get; private set; }
        public byte[] EncodedBytes { get; } = [9, 8, 7];

        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken)
        { DecodePathCalls++; return Task.FromResult(decoded.Clone()); }

        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) =>
            Task.FromResult(decoded.Clone());

        public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken)
        { LastEncodedImage = image.Clone(); LastFormat = format; return Task.FromResult(EncodedBytes); }
    }

    private sealed class RecordingWriter : IAtomicFileWriter
    {
        public string? Path { get; private set; }
        public byte[]? Content { get; private set; }
        public Task WriteAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        { Path = targetPath; Content = content.ToArray(); return Task.CompletedTask; }
    }

    private sealed class NullDialog : IImageFileDialog
    {
        public Task<string?> PickImageAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string?> PickOutputImageAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class Lifetime : IDocumentLifetime
    {
        public CancellationToken ClosingToken => CancellationToken.None;
        public bool IsClosing => false;
    }
}
