using System.Text;
using ImageLabPlugin.Application.LsbSteganography;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Robustness.Operators;
using ImageLabPlugin.Domain.Steganography;
using ImageLabPlugin.Infrastructure.Steganography;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class LsbUseCaseAndReportTests
{
    [Fact]
    public async Task 写入用例在内存与编码回读两处自检并提交统计和预览()
    {
        var image = CreateImage(32, 32);
        var codec = new LosslessMemoryCodec(image);
        var recipe = new LsbRecipe(LsbChannelStrategy.RgbRoundRobin, 0, LsbPlacementKind.PseudoRandom, 42);
        var prepared = await new PrepareLsbExperimentUseCase(codec, new LsbCapacityCalculator()).ExecuteAsync("cover.png", recipe, CancellationToken.None);
        using var payload = LsbPayload.FromText("应用层自检");

        var result = await CreateEmbed(codec).ExecuteAsync(prepared.Session, payload, recipe, LsbStatisticsScope.SelectedSlots, CancellationToken.None);

        Assert.Equal(LsbReadStatus.Success, result.SelfCheck.Status);
        Assert.True(prepared.Session.HasVerifiedStego);
        Assert.Equal(result.Facts.SelectedLogicalSlots.Length, result.Statistics.Cover.SampleCount);
        Assert.InRange(result.Preview.Placement.Size.Width, 1, 1024);
        Assert.True(codec.EncodeCount >= 1);
        Assert.True(codec.MemoryDecodeCount >= 1);
    }

    [Fact]
    public async Task 导出只发布通过真实回读的Png且原子端口收到字节()
    {
        var image = CreateImage(32, 32);
        var codec = new LosslessMemoryCodec(image);
        var recipe = new LsbRecipe(LsbChannelStrategy.Red, 1, LsbPlacementKind.Sequential, 0);
        var session = (await new PrepareLsbExperimentUseCase(codec, new LsbCapacityCalculator()).ExecuteAsync("cover.png", recipe, CancellationToken.None)).Session;
        using var payload = LsbPayload.FromText(string.Empty);
        await CreateEmbed(codec).ExecuteAsync(session, payload, recipe, LsbStatisticsScope.EligibleImage, CancellationToken.None);
        var writer = new RecordingWriter();

        var exported = await new ExportLsbImageUseCase(codec, writer, CreateExtraction()).ExecuteAsync(session, "result.png", CancellationToken.None);

        Assert.Equal(LsbReadStatus.Success, exported.SelfCheck.Status);
        Assert.Equal("result.png", writer.Path);
        Assert.NotEmpty(writer.Bytes);
    }

    [Fact]
    public async Task 报告不包含载荷Frame绝对路径用户名或非有限JSON()
    {
        var image = CreateImage(32, 32);
        var codec = new LosslessMemoryCodec(image);
        var recipe = new LsbRecipe(LsbChannelStrategy.Red, 0, LsbPlacementKind.Sequential, 123);
        var session = (await new PrepareLsbExperimentUseCase(codec, new LsbCapacityCalculator()).ExecuteAsync(@"C:\Users\alice\secret-cover.png", recipe, CancellationToken.None)).Session;
        using var payload = LsbPayload.FromText("TOP-SECRET-PAYLOAD");
        await CreateEmbed(codec).ExecuteAsync(session, payload, recipe, LsbStatisticsScope.EligibleImage, CancellationToken.None);
        var serializer = new LsbExperimentReportSerializer();

        var json = Encoding.UTF8.GetString(serializer.SerializeJson(session));
        var csv = Encoding.UTF8.GetString(serializer.SerializeCsv(session));

        Assert.DoesNotContain("TOP-SECRET-PAYLOAD", json);
        Assert.DoesNotContain("alice", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", json);
        Assert.DoesNotContain("Infinity", json);
        Assert.Contains("不是密码或密钥", json);
        Assert.Contains("schema_version", csv);
    }

    [Fact]
    public async Task Gaussian脆弱性预设每次从同一Stego开始并返回Ber与结构化状态()
    {
        var image = CreateImage(32, 32);
        var codec = new LosslessMemoryCodec(image);
        var recipe = new LsbRecipe(LsbChannelStrategy.RgbRoundRobin, 0, LsbPlacementKind.PseudoRandom, 987);
        var session = (await new PrepareLsbExperimentUseCase(codec, new LsbCapacityCalculator()).ExecuteAsync("cover.png", recipe, CancellationToken.None)).Session;
        using var payload = LsbPayload.FromText("fragile");
        await CreateEmbed(codec).ExecuteAsync(session, payload, recipe, LsbStatisticsScope.EligibleImage, CancellationToken.None);
        var useCase = new RunLsbFragilityUseCase([new GaussianBlurOperator()], CreateExtraction(), new FullReferenceQualityAnalyzer(new ImagePairValidator()));

        var first = await useCase.ExecuteAsync(session, LsbFragilityPreset.GaussianLight, CancellationToken.None);
        var second = await useCase.ExecuteAsync(session, LsbFragilityPreset.GaussianLight, CancellationToken.None);

        Assert.Equal(first.Image.Rgba.ToArray(), second.Image.Rgba.ToArray());
        Assert.True(first.FrameBer.ComparedBits > 0);
        Assert.NotEqual(LsbReadStatus.Success, first.Extraction.Status);
    }

    [Fact]
    public async Task 二进制载荷读取端口与会话释放都会清除所有权()
    {
        var reader = new MemoryPayloadReader([1, 2, 3]);
        using var payload = await new LoadLsbPayloadUseCase(reader).ExecuteAsync("payload.bin", CancellationToken.None);
        Assert.Equal(new byte[] { 1, 2, 3 }, payload.Bytes.ToArray());

        var image = CreateImage(20, 20);
        var session = new LsbExperimentSession("x", image, new LsbSlotLayout(image));
        session.Dispose();
        Assert.True(session.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = session.Frame);
    }

    private static EmbedAndAnalyzeLsbUseCase CreateEmbed(IImageCodec codec)
    {
        var orders = Orders();
        return new(new LsbFrameCodec(), new LsbCapacityCalculator(), new LsbEmbeddingEngine(orders), CreateExtraction(),
            new LsbStatisticsAnalyzer(), new LsbPreviewProjector(), codec);
    }

    private static LsbExtractionEngine CreateExtraction() => new(new LsbFrameCodec(), Orders());
    private static ILsbSlotOrder[] Orders() => [new SequentialLsbSlotOrder(), new PseudoRandomLsbSlotOrder()];

    private static PixelImage CreateImage(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            bytes[pixel * 4] = (byte)(pixel * 13);
            bytes[(pixel * 4) + 1] = (byte)(pixel * 29);
            bytes[(pixel * 4) + 2] = (byte)(pixel * 53);
            bytes[(pixel * 4) + 3] = 255;
        }
        return new(new(width, height), bytes);
    }

    private sealed class LosslessMemoryCodec : IImageCodec
    {
        private readonly PixelImage _pathImage;
        private PixelImage _last;
        public LosslessMemoryCodec(PixelImage pathImage) => (_pathImage, _last) = (pathImage, pathImage);
        public int EncodeCount { get; private set; }
        public int MemoryDecodeCount { get; private set; }
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken) => Task.FromResult(_pathImage.Clone());
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) { MemoryDecodeCount++; return Task.FromResult(_last.Clone()); }
        public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken)
        {
            Assert.Equal(ImageOutputFormat.Png, format);
            EncodeCount++; _last = image.Clone(); return Task.FromResult(new byte[] { 0x89, 0x50, 0x4e, 0x47 });
        }
    }

    private sealed class RecordingWriter : IAtomicFileWriter
    {
        public string? Path { get; private set; }
        public byte[] Bytes { get; private set; } = [];
        public Task WriteAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        { Path = targetPath; Bytes = content.ToArray(); return Task.CompletedTask; }
    }

    private sealed class MemoryPayloadReader(byte[] bytes) : ILsbPayloadFileReader
    {
        public Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken) => Task.FromResult(bytes.ToArray());
    }
}
