using System.Text.Json;
using ImageLabPlugin.Application.ImageComparison;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Features.ImageCompareLab;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class ImageCompareLabDocumentTests
{
    [Fact]
    public async Task 快照只保存轻量配方且恢复不自动比较()
    {
        var prepare = new CountingPrepare();
        using var source = CreateDocument(prepare);
        await source.InitializeAsync(new NewDocumentActivation("比较"), CancellationToken.None);
        source.ReferencePath = "D:/missing/reference.png";
        source.CandidatePath = "D:/missing/candidate.png";
        source.SelectedMode = "热力图";
        source.DifferenceAmplification = 16;
        source.SelectedHeatmapSource = "Y";
        source.Zoom = 4d;
        var snapshot = await source.CaptureSaveSnapshotAsync(CancellationToken.None);
        var json = snapshot.Content.Payload.GetRawText();

        Assert.Equal(1, snapshot.Content.SchemaVersion);
        Assert.DoesNotContain("Rgba", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Metrics", json, StringComparison.Ordinal);

        using var restored = CreateDocument(prepare);
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复", snapshot.Content), CancellationToken.None);
        Assert.Equal("热力图", restored.SelectedMode);
        Assert.Equal(16, restored.DifferenceAmplification);
        Assert.Equal(4d, restored.Zoom);
        Assert.False(restored.HasSession);
        Assert.False(restored.IsDirty);
        Assert.Equal(0, prepare.CallCount);
        Assert.Contains("显式点击", restored.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 非法快照回退安全默认值()
    {
        using var document = CreateDocument(new CountingPrepare());
        var payload = JsonSerializer.SerializeToElement(new
        {
            ReferencePath = "r", CandidatePath = "c", Mode = "未知", SplitRatio = 4d, OverlayOpacity = -2d,
            BlinkIntervalMilliseconds = 1, DifferenceAmplification = 3, HeatmapSource = "未知", HistogramChannel = "未知",
            UseLogarithmicHistogram = true, Zoom = 99d, CenterX = -1d, CenterY = 4d, ShowCrosshair = true
        });
        await document.InitializeAsync(new RestoreDocumentActivation("非法", new DocumentContent(1, payload)), CancellationToken.None);

        Assert.Equal("并排", document.SelectedMode);
        Assert.Equal(1d, document.SplitRatio);
        Assert.Equal(0d, document.OverlayOpacity);
        Assert.Equal(250, document.BlinkIntervalMilliseconds);
        Assert.Equal(4, document.DifferenceAmplification);
        Assert.Equal(0d, document.Zoom);
        Assert.Equal(0d, document.ViewportCenterX);
        Assert.Equal(1d, document.ViewportCenterY);
    }

    [Fact]
    public async Task 新路径拒绝忽略取消的迟到结果()
    {
        var prepare = new LatePrepare();
        using var document = CreateDocument(prepare);
        await document.InitializeAsync(new NewDocumentActivation("迟到"), CancellationToken.None);
        document.ReferencePath = "reference"; document.CandidatePath = "candidate";
        var operation = document.CompareCommand.ExecuteAsync(null);
        await prepare.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        document.CandidatePath = "new-candidate";
        prepare.Complete(CreateResult());
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(document.HasSession);
        Assert.False(document.HasSummary);
        Assert.Contains("重新比较", document.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Document关闭令牌会取消正在比较的用例()
    {
        var prepare = new CancellationAwarePrepare();
        using var lifetime = new CloseLifetime();
        using var document = CreateDocument(prepare, lifetime);
        await document.InitializeAsync(new NewDocumentActivation("关闭"), CancellationToken.None);
        document.ReferencePath = "reference"; document.CandidatePath = "candidate";
        var operation = document.CompareCommand.ExecuteAsync(null);
        await prepare.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lifetime.Close();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(prepare.ObservedCancellation);
        Assert.False(document.IsBusy);
        Assert.False(document.IsBlinkTimerRunning);
    }

    [Fact]
    public async Task 路径变化推进Revision而纯像素检查不会推进()
    {
        using var document = CreateDocument(new CountingPrepare());
        await document.InitializeAsync(new NewDocumentActivation("修订"), CancellationToken.None);
        var initial = await document.CaptureSaveSnapshotAsync(CancellationToken.None);
        document.AcceptChanges(initial.Revision);
        document.ReferencePath = "r";
        Assert.True(document.IsDirty);
        var changed = await document.CaptureSaveSnapshotAsync(CancellationToken.None);
        document.AcceptChanges(changed.Revision);
        document.InspectProxyAt(new ImagePoint(0, 0));
        Assert.False(document.IsDirty);
    }

    private static ImageCompareLabDocument CreateDocument(IPrepareImageComparisonUseCase prepare, IDocumentLifetime? lifetime = null) => new(
        prepare,
        new NeverProject(),
        new NeverInspect(),
        new NullExport(),
        new NullDialog(),
        new NullReportDialog(),
        new NullClipboard(),
        new NullCodec(),
        lifetime ?? new TestLifetime());

    private static ImageComparisonResult CreateResult()
    {
        var image = new PixelImage(new ImageSize(1, 1), [1, 2, 3, 255]);
        var validator = new ImagePairValidator();
        var metrics = new FullReferenceQualityAnalyzer(validator).Analyze(image, image.Clone());
        var histograms = new ImageHistogramAnalyzer(validator).Analyze(image, image.Clone());
        var summary = new ImageComparisonSummary(ImageComparisonSummary.CurrentAlgorithmId, image.Size, image.Size, true, null,
            ImageComparisonSummary.CurrentColorFormulaId, ImageComparisonSummary.CurrentAlphaRule, metrics, histograms);
        var proxy = new ImageDifferenceProxy(image.Size, [0], [0], [0], [0], [0]);
        var session = new ImageComparisonSession(image, image.Clone(), image.Clone(), image.Clone(), proxy, summary);
        return new ImageComparisonResult(session, image.Clone(), image.Clone(), null, summary);
    }

    private sealed class CountingPrepare : IPrepareImageComparisonUseCase
    {
        public int CallCount { get; private set; }
        public Task<ImageComparisonResult> ExecuteAsync(ImageComparisonRequest request, CancellationToken cancellationToken)
        { CallCount++; throw new InvalidOperationException("该测试不应执行比较。 "); }
    }
    private sealed class LatePrepare : IPrepareImageComparisonUseCase
    {
        private readonly TaskCompletionSource<ImageComparisonResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ImageComparisonResult> ExecuteAsync(ImageComparisonRequest request, CancellationToken cancellationToken)
        { Started.TrySetResult(); return _completion.Task; }
        public void Complete(ImageComparisonResult result) => _completion.SetResult(result);
    }
    private sealed class CancellationAwarePrepare : IPrepareImageComparisonUseCase
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ObservedCancellation { get; private set; }
        public async Task<ImageComparisonResult> ExecuteAsync(ImageComparisonRequest request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { ObservedCancellation = true; throw; }
            throw new InvalidOperationException();
        }
    }
    private sealed class NeverProject : IProjectImageDifferenceUseCase
    { public DifferenceProjectionResult Execute(ImageComparisonSession session, DifferenceProjectionOptions options, CancellationToken cancellationToken) => throw new InvalidOperationException(); }
    private sealed class NeverInspect : IInspectImagePairUseCase
    { public ImagePairPixelReport Execute(ImageComparisonSession session, ImagePoint sourcePoint) => throw new InvalidOperationException(); }
    private sealed class NullExport : IExportComparisonSummaryUseCase
    {
        public Task ExecuteAsync(ImageComparisonReport report, string targetPath, CancellationToken cancellationToken) => Task.CompletedTask;
        public string CreateJson(ImageComparisonReport report) => "{}";
        public string CreateHumanReadableText(ImageComparisonReport report) => string.Empty;
    }
    private sealed class NullDialog : IImageFileDialog
    {
        public Task<string?> PickImageAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string?> PickOutputImageAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
    private sealed class NullReportDialog : IComparisonReportFileDialog
    { public Task<string?> PickSummaryOutputAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null); }
    private sealed class NullClipboard : ITextClipboard
    { public Task<bool> TrySetTextAsync(string text, CancellationToken cancellationToken) => Task.FromResult(false); }
    private sealed class NullCodec : IImageCodec
    {
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
    private sealed class TestLifetime : IDocumentLifetime
    { public CancellationToken ClosingToken => CancellationToken.None; public bool IsClosing => false; }
    private sealed class CloseLifetime : IDocumentLifetime, IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        public CancellationToken ClosingToken => _source.Token;
        public bool IsClosing => _source.IsCancellationRequested;
        public void Close() => _source.Cancel();
        public void Dispose() => _source.Dispose();
    }
}
