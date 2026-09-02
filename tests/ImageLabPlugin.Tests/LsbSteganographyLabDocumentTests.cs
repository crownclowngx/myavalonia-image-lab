using ImageLabPlugin.Application.LsbSteganography;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Steganography;
using ImageLabPlugin.Features.LsbSteganographyLab;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class LsbSteganographyLabDocumentTests
{
    [Fact]
    public async Task 快照不含PayloadFrame统计像素或输出路径且恢复不自动运行()
    {
        var prepare = new CountingPrepare();
        using var source = CreateDocument(prepare);
        await source.InitializeAsync(new NewDocumentActivation("LSB"), default);
        source.SourcePath = "D:/carrier/cover.png";
        source.PayloadKind = "文本";
        source.PayloadText = "绝不能持久化的秘密文本";
        source.SelectedChannel = "B";
        source.BitPlane = 1;
        source.Placement = "伪随机";
        source.SeedText = "123456";
        var snapshot = await source.CaptureSaveSnapshotAsync(default);
        var json = snapshot.Content.Payload.GetRawText();

        Assert.DoesNotContain("绝不能持久化", json);
        Assert.DoesNotContain("Frame", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Statistics", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Rgba", json, StringComparison.OrdinalIgnoreCase);

        using var restored = CreateDocument(prepare);
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复", snapshot.Content), default);
        Assert.Equal("B", restored.SelectedChannel);
        Assert.Equal(1, restored.BitPlane);
        Assert.Equal("伪随机", restored.Placement);
        Assert.Equal("123456", restored.SeedText);
        Assert.Empty(restored.PayloadText);
        Assert.False(restored.HasCarrier);
        Assert.False(restored.IsDirty);
        Assert.Equal(0, prepare.Calls);
        Assert.Contains("未持久化", restored.StatusMessage);
    }

    [Fact]
    public async Task 换路径拒绝忽略取消的迟到Session并释放大对象()
    {
        var late = new LatePrepare();
        using var document = CreateDocument(late);
        await document.InitializeAsync(new NewDocumentActivation("LSB"), default);
        document.SourcePath = "first.png";
        var operation = document.PrepareCommand.ExecuteAsync(null);
        await late.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        document.SourcePath = "second.png";
        var session = CreateSession();
        late.Complete(new(session, new LsbCapacityCalculator().Calculate(session.SourceImage.Size, session.Layout.OpaquePixelCount, new(LsbChannelStrategy.RgbRoundRobin, 0, LsbPlacementKind.Sequential, 1), 0)));
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(document.HasCarrier);
        Assert.True(session.IsDisposed);
        Assert.Contains("显式", document.StatusMessage);
    }

    [Fact]
    public async Task Payload与配方变化推进Revision且安全措辞完整()
    {
        using var document = CreateDocument(new CountingPrepare());
        await document.InitializeAsync(new NewDocumentActivation("LSB"), default);
        var saved = await document.CaptureSaveSnapshotAsync(default);
        document.AcceptChanges(saved.Revision);

        document.PayloadText = "changed";

        Assert.True(document.IsDirty);
        Assert.Contains("不是密码", document.SeedNotice);
        Assert.Contains("不是图片含隐写的概率", document.StatisticsNotice);
        Assert.Contains("不认证来源", document.CrcNotice);
        Assert.Contains("不是频域鲁棒水印", document.PrimaryNotice);
    }

    private static LsbSteganographyLabDocument CreateDocument(IPrepareLsbExperimentUseCase prepare) => new(
        prepare, new NullEstimate(), new NullEmbed(), new NullLoad(), new NullInspect(), new NullFragility(), new NullImageExport(), new NullReportExport(),
        new NullImageDialog(), new NullPayloadDialog(), new NullReportDialog(), new NullCodec(), new TestLifetime());

    private static LsbExperimentSession CreateSession()
    {
        var bytes = new byte[20 * 20 * 4];
        for (var pixel = 0; pixel < 400; pixel++) bytes[(pixel * 4) + 3] = 255;
        var image = new PixelImage(new(20, 20), bytes);
        return new("first.png", image, new(image));
    }

    private sealed class CountingPrepare : IPrepareLsbExperimentUseCase
    {
        public int Calls { get; private set; }
        public Task<LsbPreparedSession> ExecuteAsync(string sourcePath, LsbRecipe recipe, CancellationToken cancellationToken) { Calls++; throw new InvalidOperationException(); }
    }

    private sealed class LatePrepare : IPrepareLsbExperimentUseCase
    {
        private readonly TaskCompletionSource<LsbPreparedSession> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<LsbPreparedSession> ExecuteAsync(string sourcePath, LsbRecipe recipe, CancellationToken cancellationToken) { Started.TrySetResult(); return _completion.Task; }
        public void Complete(LsbPreparedSession value) => _completion.SetResult(value);
    }

    private sealed class NullEstimate : IEstimateLsbCapacityUseCase { public LsbCapacity Execute(LsbExperimentSession session, LsbRecipe recipe, int payloadLength) => throw new InvalidOperationException(); }
    private sealed class NullEmbed : IEmbedAndAnalyzeLsbUseCase { public Task<LsbEmbedUseCaseResult> ExecuteAsync(LsbExperimentSession session, LsbPayload payload, LsbRecipe recipe, LsbStatisticsScope scope, CancellationToken cancellationToken) => throw new InvalidOperationException(); }
    private sealed class NullLoad : ILoadLsbPayloadUseCase { public Task<LsbPayload> ExecuteAsync(string path, CancellationToken cancellationToken) => throw new InvalidOperationException(); }
    private sealed class NullInspect : IInspectLsbPixelUseCase { public LsbPixelProbe Execute(LsbExperimentSession session, int x, int y) => throw new InvalidOperationException(); }
    private sealed class NullFragility : IRunLsbFragilityUseCase { public Task<LsbFragilityResult> ExecuteAsync(LsbExperimentSession session, LsbFragilityPreset preset, CancellationToken cancellationToken) => throw new InvalidOperationException(); }
    private sealed class NullImageExport : IExportLsbImageUseCase { public Task<LsbImageExportResult> ExecuteAsync(LsbExperimentSession session, string outputPath, CancellationToken cancellationToken) => throw new InvalidOperationException(); }
    private sealed class NullReportExport : IExportLsbReportUseCase { public Task<LsbReportExportResult> ExecuteAsync(LsbExperimentSession session, string outputPath, string format, CancellationToken cancellationToken) => throw new InvalidOperationException(); }
    private sealed class NullImageDialog : IImageFileDialog { public Task<string?> PickImageAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null); public Task<string?> PickOutputImageAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null); }
    private sealed class NullPayloadDialog : IPayloadFileDialog { public Task<string?> PickPayloadAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null); public Task<string?> PickPayloadExportAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null); }
    private sealed class NullReportDialog : ILsbReportFileDialog { public Task<string?> PickLsbJsonOutputAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null); public Task<string?> PickLsbCsvOutputAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null); }
    private sealed class NullCodec : IImageCodec { public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken) => throw new InvalidOperationException(); public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) => throw new InvalidOperationException(); public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken) => throw new InvalidOperationException(); }
    private sealed class TestLifetime : IDocumentLifetime { public CancellationToken ClosingToken => default; public bool IsClosing => false; }
}
