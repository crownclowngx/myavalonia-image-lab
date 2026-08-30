using System.Text.Json;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.Watermarking;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Watermarking;
using ImageLabPlugin.Features.WatermarkEmbed;
using ImageLabPlugin.Features.WatermarkInspect;
using ImageLabPlugin.Infrastructure.Persistence;
using ImageLabPlugin.Infrastructure.Watermarking;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>覆盖两个 Persistable Document 的敏感边界、修订与异步生命周期。</summary>
public sealed class DocumentLifecycleTests
{
    [Fact]
    public async Task 写入快照保存配方但不保存明文Payload和密码()
    {
        using var document = CreateEmbedDocument(new ImmediateEstimateUseCase());
        await document.InitializeAsync(new NewDocumentActivation("写入测试"), CancellationToken.None);
        document.SourcePath = "D:/images/source.png";
        document.PayloadText = "PAYLOAD-CANARY-DO-NOT-PERSIST";
        document.UseEncryption = true;
        document.Password = "PASSWORD-CANARY-DO-NOT-PERSIST";
        document.SelectedProfile = "鲁棒";

        var snapshot = await document.CaptureSaveSnapshotAsync(CancellationToken.None);
        var json = snapshot.Content.Payload.GetRawText();

        Assert.True(document.IsDirty);
        Assert.Equal(1, snapshot.Content.SchemaVersion);
        Assert.Contains("source.png", json, StringComparison.Ordinal);
        Assert.Equal("鲁棒", snapshot.Content.Payload.GetProperty("SelectedProfile").GetString());
        Assert.DoesNotContain("PAYLOAD-CANARY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PASSWORD-CANARY", json, StringComparison.Ordinal);
        document.AcceptChanges(snapshot.Revision);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public async Task 写入快照恢复时明确清空敏感输入并保持稳定标题()
    {
        using var source = CreateEmbedDocument(new ImmediateEstimateUseCase());
        await source.InitializeAsync(new NewDocumentActivation("原始"), CancellationToken.None);
        source.SourcePath = "D:/missing/source.png";
        source.PayloadText = "not-persisted";
        source.Password = "not-persisted";
        source.OutputJpeg = true;
        var snapshot = await source.CaptureSaveSnapshotAsync(CancellationToken.None);

        using var restored = CreateEmbedDocument(new ImmediateEstimateUseCase());
        await restored.InitializeAsync(
            new RestoreDocumentActivation("恢复的水印作业", snapshot.Content),
            CancellationToken.None);

        Assert.Equal("恢复的水印作业", restored.Presentation.Title);
        Assert.Equal("D:/missing/source.png", restored.SourcePath);
        Assert.True(restored.OutputJpeg);
        Assert.Empty(restored.PayloadText);
        Assert.Empty(restored.Password);
        Assert.False(restored.IsDirty);
    }

    [Fact]
    public async Task 快速重复估算时迟到的旧结果不会覆盖新结果()
    {
        var estimate = new SequencedEstimateUseCase();
        using var document = CreateEmbedDocument(estimate);
        var sourcePath = Path.GetTempFileName();
        try
        {
            await document.InitializeAsync(new NewDocumentActivation("并发测试"), CancellationToken.None);
            document.SourcePath = sourcePath;
            document.PayloadText = "x";

            var first = document.EstimateCapacityCommand.ExecuteAsync(null);
            await estimate.WaitForCallsAsync(1);
            var second = document.EstimateCapacityCommand.ExecuteAsync(null);
            await estimate.WaitForCallsAsync(2);
            estimate.Complete(1, maximumPayloadBytes: 222);
            await second;
            estimate.Complete(0, maximumPayloadBytes: 111);
            await first;

            Assert.Contains("222", document.CapacitySummary, StringComparison.Ordinal);
            Assert.DoesNotContain("111", document.CapacitySummary, StringComparison.Ordinal);
            Assert.False(document.IsBusy);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task 提取结果在密码变化后立即失效且快照不含密码和恢复内容()
    {
        var sourcePath = Path.GetTempFileName();
        using var lifetime = new TestLifetime();
        using var document = new WatermarkInspectDocument(
            new ImmediateInspectUseCase(),
            new SuccessfulExtractUseCase("RECOVERED-CANARY"u8.ToArray()),
            new NullDialog(),
            new AtomicFileWriter(),
            lifetime);
        try
        {
            await document.InitializeAsync(new NewDocumentActivation("提取测试"), CancellationToken.None);
            document.SourcePath = sourcePath;
            document.Password = "PASSWORD-CANARY";
            await document.ExtractCommand.ExecuteAsync(null);

            Assert.True(document.HasRecoveredPayload);
            Assert.Contains("5245434F", document.PayloadPreview, StringComparison.Ordinal);
            document.Password = "new-password";
            Assert.False(document.HasRecoveredPayload);
            Assert.Empty(document.PayloadPreview);

            var snapshot = await document.CaptureSaveSnapshotAsync(CancellationToken.None);
            var json = snapshot.Content.Payload.GetRawText();
            Assert.DoesNotContain("PASSWORD-CANARY", json, StringComparison.Ordinal);
            Assert.DoesNotContain("RECOVERED-CANARY", json, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task 关闭Document会取消正在等待的操作()
    {
        var estimate = new CancellationAwareEstimateUseCase();
        using var lifetime = new TestLifetime();
        using var document = CreateEmbedDocument(estimate, lifetime);
        var sourcePath = Path.GetTempFileName();
        try
        {
            await document.InitializeAsync(new NewDocumentActivation("关闭测试"), CancellationToken.None);
            document.SourcePath = sourcePath;
            var operation = document.EstimateCapacityCommand.ExecuteAsync(null);
            await estimate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            lifetime.Close();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(estimate.ObservedCancellation);
            Assert.False(document.IsBusy);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    private static WatermarkEmbedDocument CreateEmbedDocument(
        IEstimateWatermarkCapacityUseCase estimate,
        TestLifetime? lifetime = null) =>
        new(
            estimate,
            new NeverCalledEmbedUseCase(),
            new NullDialog(),
            new AtomicFileWriter(),
            lifetime ?? new TestLifetime());

    private sealed class ImmediateEstimateUseCase : IEstimateWatermarkCapacityUseCase
    {
        public Task<CapacityEstimate> ExecuteAsync(
            string sourcePath,
            EmbeddingProfileId profile,
            int payloadLength,
            bool encrypted,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CapacityEstimate(4000, 2688, 1312, 256, 240, payloadLength));
    }

    private sealed class SequencedEstimateUseCase : IEstimateWatermarkCapacityUseCase
    {
        private readonly List<TaskCompletionSource<CapacityEstimate>> _calls = [];

        public Task<CapacityEstimate> ExecuteAsync(
            string sourcePath,
            EmbeddingProfileId profile,
            int payloadLength,
            bool encrypted,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<CapacityEstimate>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_calls)
            {
                _calls.Add(completion);
                Monitor.PulseAll(_calls);
            }

            // 有意忽略取消，用来证明 Document 自己仍能拒绝迟到结果。
            return completion.Task;
        }

        public async Task WaitForCallsAsync(int count)
        {
            var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (true)
            {
                lock (_calls)
                {
                    if (_calls.Count >= count)
                    {
                        return;
                    }
                }

                if (DateTime.UtcNow >= timeout)
                {
                    throw new TimeoutException("未观察到预期的估算调用。");
                }

                await Task.Delay(10);
            }
        }

        public void Complete(int index, int maximumPayloadBytes)
        {
            TaskCompletionSource<CapacityEstimate> completion;
            lock (_calls)
            {
                completion = _calls[index];
            }

            completion.SetResult(new CapacityEstimate(4000, 2688, 1312, 300, maximumPayloadBytes, 1));
        }
    }

    private sealed class CancellationAwareEstimateUseCase : IEstimateWatermarkCapacityUseCase
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ObservedCancellation { get; private set; }

        public async Task<CapacityEstimate> ExecuteAsync(
            string sourcePath,
            EmbeddingProfileId profile,
            int payloadLength,
            bool encrypted,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }

            throw new InvalidOperationException("不可到达。");
        }
    }

    private sealed class NeverCalledEmbedUseCase : IEmbedWatermarkUseCase
    {
        public Task<EmbedResult> ExecuteAsync(EmbedWatermarkRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("该测试不应执行写入用例。");
    }

    private sealed class ImmediateInspectUseCase : IInspectWatermarkUseCase
    {
        public Task<(PixelImage Image, HeaderReadResult? Header, ExtractionReport Report)> ExecuteAsync(
            string sourcePath,
            CancellationToken cancellationToken) =>
            Task.FromResult<(PixelImage, HeaderReadResult?, ExtractionReport)>(
                (CreatePixel(), null, new ExtractionReport(WatermarkDetectionStatus.NoSupportedWatermark, "无水印")));
    }

    private sealed class SuccessfulExtractUseCase(byte[] payload) : IExtractWatermarkUseCase
    {
        private readonly ExtractionReport _report = new(
            WatermarkDetectionStatus.RecoveredIntegrityValid,
            "恢复成功",
            PayloadContentType.Binary,
            payload,
            EmbeddingProfileId.Balanced,
            IntegrityStatus.Valid);

        public Task<(PixelImage Image, ExtractionReport Report)> ExecuteAsync(
            string sourcePath,
            string? password,
            CancellationToken cancellationToken) => Task.FromResult((CreatePixel(), _report));

        public Task<ExtractionReport> ExecuteAsync(
            ReadOnlyMemory<byte> encodedImage,
            string? password,
            CancellationToken cancellationToken) => Task.FromResult(_report);

        public ExtractionReport Extract(PixelImage image, string? password, CancellationToken cancellationToken) => _report;
    }

    private sealed class NullDialog : IImageLabFileDialog
    {
        public Task<string?> PickImageAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string?> PickPayloadAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string?> PickOutputImageAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string?> PickPayloadExportAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class TestLifetime : IDocumentLifetime, IDisposable
    {
        private readonly CancellationTokenSource _closing = new();
        public CancellationToken ClosingToken => _closing.Token;
        public bool IsClosing => _closing.IsCancellationRequested;
        public void Close() => _closing.Cancel();
        public void Dispose() => _closing.Dispose();
    }

    private static PixelImage CreatePixel() => new(new ImageSize(1, 1), new byte[] { 0, 0, 0, 255 });
}
