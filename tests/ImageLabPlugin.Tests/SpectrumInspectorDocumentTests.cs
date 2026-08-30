using System.Text.Json;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.SpectrumAnalysis;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Features.SpectrumInspector;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>验证频域 Document 的轻量持久化、参数失效和恢复边界。</summary>
public sealed class SpectrumInspectorDocumentTests
{
    [Fact]
    public async Task 快照只保存轻量配方且Schema1可往返()
    {
        using var source = CreateDocument();
        await source.InitializeAsync(new NewDocumentActivation("频域测试"), CancellationToken.None);
        source.SourcePath = "D:/missing/frequency.png";
        source.SelectedChannel = "Cr";
        source.SelectedMaximumEdge = 2048;
        source.SelectedBand = "自定义";
        source.CustomInner = 0.2;
        source.CustomOuter = 0.7;

        var snapshot = await source.CaptureSaveSnapshotAsync(CancellationToken.None);
        var json = snapshot.Content.Payload.GetRawText();
        Assert.Equal(1, snapshot.Content.SchemaVersion);
        Assert.Contains("frequency.png", json, StringComparison.Ordinal);
        Assert.Contains("Cr", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Rgba", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Spectrum", json, StringComparison.Ordinal);

        using var restored = CreateDocument();
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复频域", snapshot.Content), CancellationToken.None);
        Assert.Equal("Cr", restored.SelectedChannel);
        Assert.Equal(2048, restored.SelectedMaximumEdge);
        Assert.Equal("自定义", restored.SelectedBand);
        Assert.False(restored.HasSession);
        Assert.False(restored.IsDirty);
        Assert.Contains("不存在", restored.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 非法快照参数回退安全默认值且不自动分析()
    {
        var calls = new CountingAnalyzeUseCase();
        using var document = CreateDocument(calls);
        var payload = JsonSerializer.SerializeToElement(new
        {
            SourcePath = "D:/missing.png", Channel = "Unknown", MaximumEdge = 999, View = "Unknown",
            Low = 0.9, High = 0.1, Band = "Unknown", Inner = 0.8, Outer = 0.2, SourceX = -1, SourceY = -2
        });

        await document.InitializeAsync(
            new RestoreDocumentActivation("非法恢复", new DocumentContent(1, payload)), CancellationToken.None);

        Assert.Equal("Y", document.SelectedChannel);
        Assert.Equal(1024, document.SelectedMaximumEdge);
        Assert.Equal("对数幅度", document.SelectedSpectrumView);
        Assert.Equal(0.15, document.LowBoundary, 8);
        Assert.Equal(0.50, document.HighBoundary, 8);
        Assert.Equal(0, calls.CallCount);
    }

    [Fact]
    public async Task 持久参数变化推进Revision而频点悬停不推进()
    {
        using var document = CreateDocument();
        await document.InitializeAsync(new NewDocumentActivation("修订测试"), CancellationToken.None);
        var initial = await document.CaptureSaveSnapshotAsync(CancellationToken.None);
        document.AcceptChanges(initial.Revision);
        Assert.False(document.IsDirty);

        document.SelectedBand = "低频";
        Assert.True(document.IsDirty);
        var changed = await document.CaptureSaveSnapshotAsync(CancellationToken.None);
        document.AcceptChanges(changed.Revision);
        Assert.False(document.IsDirty);
        document.InspectFrequencyAt(0.5, 0.5);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public async Task 新图片会拒绝忽略取消的迟到分析结果()
    {
        var sourcePath = Path.GetTempFileName();
        var late = new LateAnalyzeUseCase();
        using var document = CreateDocument(late);
        try
        {
            await document.InitializeAsync(new NewDocumentActivation("迟到结果"), CancellationToken.None);
            document.SourcePath = sourcePath;
            var operation = document.AnalyzeCommand.ExecuteAsync(null);
            await late.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            document.SourcePath = "D:/another-image.png";
            late.Complete(CreateAnalysisResult());
            await operation.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(document.HasSession);
            Assert.Contains("重新分析", document.StatusMessage, StringComparison.Ordinal);
        }
        finally { File.Delete(sourcePath); }
    }

    [Fact]
    public async Task Document关闭令牌会取消正在分析的用例()
    {
        var sourcePath = Path.GetTempFileName();
        var analyze = new CancellationAwareAnalyzeUseCase();
        using var lifetime = new CloseLifetime();
        using var document = CreateDocument(analyze, lifetime);
        try
        {
            await document.InitializeAsync(new NewDocumentActivation("关闭取消"), CancellationToken.None);
            document.SourcePath = sourcePath;
            var operation = document.AnalyzeCommand.ExecuteAsync(null);
            await analyze.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            lifetime.Close();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(analyze.ObservedCancellation);
            Assert.False(document.IsBusy);
        }
        finally { File.Delete(sourcePath); }
    }

    private static SpectrumInspectorDocument CreateDocument(IAnalyzeSpectrumUseCase? analyze = null, IDocumentLifetime? lifetime = null) =>
        new(
            analyze ?? new CountingAnalyzeUseCase(),
            new NeverInspectUseCase(),
            new NeverReconstructUseCase(),
            new NeverProjectUseCase(),
            new NullImageDialog(),
            new NullCodec(),
            new NullWriter(),
            lifetime ?? new TestLifetime());

    private sealed class CountingAnalyzeUseCase : IAnalyzeSpectrumUseCase
    {
        public int CallCount { get; private set; }
        public Task<SpectrumAnalysisResult> ExecuteAsync(SpectrumAnalysisRequest request, CancellationToken cancellationToken)
        { CallCount++; throw new InvalidOperationException("该测试不应执行分析。 "); }
    }

    private sealed class LateAnalyzeUseCase : IAnalyzeSpectrumUseCase
    {
        private readonly TaskCompletionSource<SpectrumAnalysisResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SpectrumAnalysisResult> ExecuteAsync(SpectrumAnalysisRequest request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            // 故意忽略取消，验证 Document 自己的 generation 门禁。
            return _completion.Task;
        }
        public void Complete(SpectrumAnalysisResult result) => _completion.SetResult(result);
    }

    private sealed class CancellationAwareAnalyzeUseCase : IAnalyzeSpectrumUseCase
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ObservedCancellation { get; private set; }
        public async Task<SpectrumAnalysisResult> ExecuteAsync(SpectrumAnalysisRequest request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { ObservedCancellation = true; throw; }
            throw new InvalidOperationException();
        }
    }

    private sealed class NeverInspectUseCase : IInspectDctBlockUseCase
    {
        public DctBlockReport Execute(SpectrumAnalysisSession session, ImagePoint sourcePoint) => throw new InvalidOperationException();
    }

    private sealed class NeverReconstructUseCase : IReconstructSpectrumBandUseCase
    {
        public Task<BandReconstructionResult> ExecuteAsync(SpectrumAnalysisSession session, FrequencyBandDefinition band, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class NeverProjectUseCase : IProjectSpectrumUseCase
    {
        public PixelImage CreateMagnitude(SpectrumAnalysisSession session, SpectrumMagnitudeMode mode, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public FrequencyPointInfo Inspect(SpectrumAnalysisSession session, int displayX, int displayY, FrequencyBandBoundaries boundaries) => throw new InvalidOperationException();
        public RadialEnergyReport AnalyzeEnergy(SpectrumAnalysisSession session, FrequencyBandBoundaries boundaries, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class NullImageDialog : IImageFileDialog
    {
        public Task<string?> PickImageAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string?> PickOutputImageAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class NullCodec : IImageCodec
    {
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class NullWriter : IAtomicFileWriter
    {
        public Task WriteAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class TestLifetime : IDocumentLifetime
    {
        public CancellationToken ClosingToken => CancellationToken.None;
        public bool IsClosing => false;
    }

    private sealed class CloseLifetime : IDocumentLifetime, IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        public CancellationToken ClosingToken => _source.Token;
        public bool IsClosing => _source.IsCancellationRequested;
        public void Close() => _source.Cancel();
        public void Dispose() => _source.Dispose();
    }

    private static SpectrumAnalysisResult CreateAnalysisResult()
    {
        var image = new PixelImage(new ImageSize(1, 1), [10, 20, 30, 255]);
        var plane = new ImageChannelPlane(image.Size, ImageChannel.Luma, [18.15d]);
        var spectrum = new FrequencySpectrum(image.Size, 1, 1, [new System.Numerics.Complex(18.15d, 0d)]);
        var radial = new RadialEnergyAnalyzer().Analyze(spectrum, FrequencyBandBoundaries.Default);
        var session = new SpectrumAnalysisSession(image, image.Clone(), ImageChannel.Luma, plane, spectrum, radial);
        return new SpectrumAnalysisResult(session, image.Clone(), image.Clone(), image.Clone());
    }
}
