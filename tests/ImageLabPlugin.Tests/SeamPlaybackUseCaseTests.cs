using System.Text;
using System.Text.Json;
using ImageLabPlugin.Application.SeamCarving;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.SeamCarving;
using ImageLabPlugin.Infrastructure.SeamCarving;
using ImageLabPlugin.Application.Ports;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class SeamPlaybackUseCaseTests
{
    [Fact]
    public async Task 预览不修改图片且单步只删除一条缝()
    {
        using var session = CreateSession(4, 3);
        var services = CreateServices();
        new PlanSeamResizeUseCase(new(new())).Execute(session,
            new(new ImageSize(3, 3), SeamAxisOrder.Auto, ReferenceResizeAlgorithm.Bilinear));
        var before = session.CurrentImage!.Rgba.ToArray();
        var preview = await services.Preview.ExecuteAsync(session, default);
        Assert.NotNull(preview); Assert.Equal(before, session.CurrentImage.Rgba.ToArray());
        var record = await services.Apply.ExecuteAsync(session, default);
        Assert.NotNull(record); Assert.Equal(new ImageSize(3, 3), session.CurrentImage.Size);
        Assert.Equal(1, session.StepIndex); Assert.True(session.HasCompletedResult);
    }

    [Fact]
    public async Task 逐单步与播放最终像素完全相同()
    {
        using var stepped = CreateSession(8, 8);
        using var played = CreateSession(8, 8);
        var steppedServices = CreateServices(); var playedServices = CreateServices();
        var request = new SeamResizeRequest(new ImageSize(6, 7), SeamAxisOrder.WidthFirst, ReferenceResizeAlgorithm.Bilinear);
        var planner = new PlanSeamResizeUseCase(new(new())); planner.Execute(stepped, request); planner.Execute(played, request);
        while (!stepped.HasCompletedResult) await steppedServices.Apply.ExecuteAsync(stepped, default);
        await playedServices.Playback.ExecuteAsync(played, null, null, default);
        Assert.Equal(stepped.CurrentImage!.Rgba.ToArray(), played.CurrentImage!.Rgba.ToArray());
        Assert.Equal(stepped.Records, played.Records);
    }

    [Fact]
    public async Task 播放暂停不会启动下一步且可继续()
    {
        using var session = CreateSession(8, 4); var services = CreateServices();
        new PlanSeamResizeUseCase(new(new())).Execute(session,
            new(new ImageSize(6, 4), SeamAxisOrder.Auto, ReferenceResizeAlgorithm.Bilinear));
        await services.Playback.ExecuteAsync(session, null, () => true, default);
        Assert.Equal(0, session.StepIndex); Assert.Equal(SeamPlaybackState.Paused, session.State);
        await services.Playback.ExecuteAsync(session, null, null, default);
        Assert.True(session.HasCompletedResult);
    }

    [Fact]
    public async Task 取消在步骤边界可观察且状态为Canceled()
    {
        using var session = CreateSession(8, 4); var services = CreateServices();
        new PlanSeamResizeUseCase(new(new())).Execute(session,
            new(new ImageSize(6, 4), SeamAxisOrder.Auto, ReferenceResizeAlgorithm.Bilinear));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => services.Playback.ExecuteAsync(session, null, null, cancellation.Token));
        Assert.Equal(SeamPlaybackState.Canceled, session.State); Assert.Equal(0, session.StepIndex);
    }

    [Fact]
    public async Task 插入计划逐批应用后精确命中目标且不保存历史帧()
    {
        using var session = CreateSession(8, 4); var services = CreateServices();
        new PlanSeamResizeUseCase(new(new())).Execute(session,
            new(new ImageSize(10, 4), SeamAxisOrder.Auto, ReferenceResizeAlgorithm.Bilinear));
        await services.Playback.ExecuteAsync(session, null, null, default);
        Assert.Equal(new ImageSize(10, 4), session.CurrentImage!.Size);
        Assert.Equal(2, session.Records.Count); Assert.True(session.HasCompletedResult);
        Assert.Null(session.InsertionBatch); Assert.Empty(session.AppliedInsertionPaths);
    }

    [Fact]
    public async Task 对照输出同尺寸并使用seamVsReference差异语义()
    {
        using var session = CreateSession(4, 3); var services = CreateServices();
        new PlanSeamResizeUseCase(new(new())).Execute(session,
            new(new ImageSize(3, 3), SeamAxisOrder.Auto, ReferenceResizeAlgorithm.Bilinear));
        await services.Playback.ExecuteAsync(session, null, null, default);
        var compare = new CompareSeamResizeUseCase(
            [new BilinearReferenceResampler(), new BicubicReferenceResampler()],
            new FullReferenceQualityAnalyzer(new ImagePairValidator()));
        var result = await compare.ExecuteAsync(session, default);
        Assert.Equal(session.CurrentImage!.Size, result.ReferenceImage.Size);
        Assert.Equal(session.CurrentImage.Size, result.DifferenceImage.Size);
        Assert.True(double.IsFinite(result.SeamVsReference.MeanAbsoluteErrorRgb));
    }

    [Fact]
    public void 参数或蒙版变化使计划结果过期且多实例隔离()
    {
        using var first = CreateSession(4, 4); using var second = CreateSession(4, 4);
        var plan = new PlanSeamResizeUseCase(new(new()));
        plan.Execute(first, new(new ImageSize(3, 4), SeamAxisOrder.Auto, ReferenceResizeAlgorithm.Bilinear));
        var edit = new EditSeamMaskUseCase(new SeamMaskRasterizer());
        edit.Apply(first, [new(SeamBrushTool.Protect, 0.1, [new(0.5, 0.5)], 0)]);
        Assert.Equal(SeamPlaybackState.Stale, first.State); Assert.Null(first.Plan);
        Assert.Equal(SeamPlaybackState.Ready, second.State); Assert.Empty(second.Strokes);
    }

    [Fact]
    public void 报告Json不含路径像素蒙版栅格且Csv带Utf8Bom与固定列()
    {
        using var session = CreateSession(2, 2);
        new PlanSeamResizeUseCase(new(new())).Execute(session,
            new(new ImageSize(2, 2), SeamAxisOrder.Auto, ReferenceResizeAlgorithm.Bilinear));
        var serializer = new SeamCarvingReportSerializer();
        var json = Encoding.UTF8.GetString(serializer.SerializeJson(session.CreateReport()));
        Assert.Contains(SeamCarvingProtocols.ReportSchema, json, StringComparison.Ordinal);
        Assert.DoesNotContain("source.png", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coordinates", json, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        Assert.Contains("算法间差异", document.RootElement.GetProperty("interpretation").GetString(), StringComparison.Ordinal);
        var csv = serializer.SerializeCsv(session.CreateReport());
        Assert.Equal([0xEF, 0xBB, 0xBF], csv[..3]);
        Assert.Contains("stepNumber,orientation,operation", Encoding.UTF8.GetString(csv), StringComparison.Ordinal);
    }

    [Fact]
    public void Dispose清空所有权并拒绝后续操作()
    {
        var session = CreateSession(2, 2); session.Dispose();
        Assert.Null(session.InputImage); Assert.Null(session.CurrentImage); Assert.Null(session.CurrentMask);
        Assert.Throws<ObjectDisposedException>(() => session.Reset());
    }

    [Fact]
    public async Task 完成结果只编码Png并通过原子端口发布且拒绝覆盖源图()
    {
        using var session = CreateSession(2, 2);
        new PlanSeamResizeUseCase(new(new())).Execute(session,
            new(new ImageSize(2, 2), SeamAxisOrder.Auto, ReferenceResizeAlgorithm.Bilinear));
        var codec = new RecordingCodec(); var writer = new RecordingWriter();
        var useCase = new ExportSeamResultUseCase(codec, writer);
        await useCase.ExecuteAsync(session, "result.png", default);
        Assert.Equal(ImageOutputFormat.Png, codec.LastFormat); Assert.Equal("result.png", writer.Path);
        Assert.Equal(new byte[] { 1, 2, 3 }, writer.Content);
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(session, "source.png", default));
    }

    [Fact]
    public async Task 未完成或非Png结果在编码前拒绝()
    {
        using var session = CreateSession(4, 4); var codec = new RecordingCodec();
        var useCase = new ExportSeamResultUseCase(codec, new RecordingWriter());
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(session, "result.png", default));
        new PlanSeamResizeUseCase(new(new())).Execute(session,
            new(new ImageSize(4, 4), SeamAxisOrder.Auto, ReferenceResizeAlgorithm.Bilinear));
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(session, "result.jpg", default));
        Assert.Null(codec.LastFormat);
    }

    private static SeamCarvingSession CreateSession(int width, int height)
    {
        var session = new SeamCarvingSession();
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = ((y * width) + x) * 4;
            bytes[offset] = (byte)(x * 30); bytes[offset + 1] = (byte)(y * 40);
            bytes[offset + 2] = (byte)((x + y) * 20); bytes[offset + 3] = 255;
        }
        session.Initialize("source.png", new PixelImage(new ImageSize(width, height), bytes));
        return session;
    }

    private static (PreviewNextSeamUseCase Preview, ApplySeamStepUseCase Apply,
        RunSeamPlaybackUseCase Playback) CreateServices()
    {
        var energy = SeamEnergyTests.Calculator(); var finder = new MinimumEnergySeamFinder();
        var remover = new SeamRemover(); var insertionPlanner = new SeamInsertionPlanner(energy, finder, remover);
        var preview = new PreviewNextSeamUseCase(energy, finder, insertionPlanner);
        var apply = new ApplySeamStepUseCase(remover, new SeamInserter(), preview);
        return (preview, apply, new RunSeamPlaybackUseCase(preview, apply));
    }

    private sealed class RecordingCodec : IImageCodec
    {
        public ImageOutputFormat? LastFormat { get; private set; }
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality,
            CancellationToken cancellationToken)
        { LastFormat = format; return Task.FromResult(new byte[] { 1, 2, 3 }); }
    }

    private sealed class RecordingWriter : IAtomicFileWriter
    {
        public string? Path { get; private set; }
        public byte[]? Content { get; private set; }
        public Task WriteAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        { Path = targetPath; Content = content.ToArray(); return Task.CompletedTask; }
    }
}
