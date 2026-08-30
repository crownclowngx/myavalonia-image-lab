using ImageLabPlugin.Application.Fingerprinting;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Fingerprinting;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Features.ImageFingerprint;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class ImageFingerprintDocumentTests
{
    [Fact]
    public async Task 快照只保存路径和轻量参数且恢复不自动读图()
    {
        var prepare = new CountingPrepare();
        using var source = CreateDocument(prepare);
        await source.InitializeAsync(new NewDocumentActivation("指纹"), default);
        source.ReferencePath = "D:/secret/reference.png";
        source.CandidatePath = "D:/secret/candidate.jpg";
        source.SelectedStabilityKind = "中心裁剪";
        source.StabilityValuesText = "0,2,5";
        source.ShowFingerprintBitmaps = false;
        var snapshot = await source.CaptureSaveSnapshotAsync(default);
        var json = snapshot.Content.Payload.GetRawText();
        Assert.DoesNotContain("Rgba", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FingerprintComparisonSummary", json, StringComparison.Ordinal);

        using var restored = CreateDocument(prepare);
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复", snapshot.Content), default);
        Assert.Equal("中心裁剪", restored.SelectedStabilityKind);
        Assert.Equal("0,2,5", restored.StabilityValuesText);
        Assert.False(restored.ShowFingerprintBitmaps);
        Assert.False(restored.HasSession);
        Assert.False(restored.IsDirty);
        Assert.Equal(0, prepare.Calls);
        Assert.Contains("不会读取", restored.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 路径变化拒绝忽略取消的迟到Session并释放它()
    {
        var late = new LatePrepare();
        using var document = CreateDocument(late);
        await document.InitializeAsync(new NewDocumentActivation("迟到"), default);
        document.ReferencePath = "reference"; document.CandidatePath = "candidate";
        var operation = document.ComputeCommand.ExecuteAsync(null);
        await late.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        document.CandidatePath = "changed";
        var session = CreateSession();
        late.Complete(session);
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(document.HasSession);
        Assert.False(document.HasResult);
        Assert.True(session.IsDisposed);
        Assert.Contains("重新计算", document.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 关闭令牌取消正在运行的计算()
    {
        var prepare = new CancellationPrepare();
        using var lifetime = new CloseLifetime();
        using var document = CreateDocument(prepare, lifetime);
        await document.InitializeAsync(new NewDocumentActivation("关闭"), default);
        document.ReferencePath = "reference"; document.CandidatePath = "candidate";
        var operation = document.ComputeCommand.ExecuteAsync(null);
        await prepare.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lifetime.Close();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(prepare.ObservedCancellation);
        Assert.False(document.IsBusy);
    }

    [Fact]
    public async Task 路径与轻量选项推进Revision而算法选择不推进()
    {
        using var document = CreateDocument(new CountingPrepare());
        await document.InitializeAsync(new NewDocumentActivation("修订"), default);
        var snapshot = await document.CaptureSaveSnapshotAsync(default); document.AcceptChanges(snapshot.Revision);
        document.ShowLimitations = false;
        Assert.True(document.IsDirty);
        var changed = await document.CaptureSaveSnapshotAsync(default); document.AcceptChanges(changed.Revision);
        document.SelectedAlgorithm = new("aHash", FingerprintAlgorithmId.AverageHash.Value, "0", "0", 0, "100%", "相同", 8, "限制", 0, 0);
        Assert.False(document.IsDirty);
    }

    private static ImageFingerprintDocument CreateDocument(IPrepareFingerprintComparisonUseCase prepare, IDocumentLifetime? lifetime = null) => new(
        prepare, new NeverStability(), new NullExport(), new NullImageDialog(), new NullReportDialog(), new NullClipboard(), new NullCodec(), lifetime ?? new TestLifetime());

    private static FingerprintComparisonSession CreateSession()
    {
        var image = FingerprintNormalizationTests.GrayImage(1, 1, [10]);
        var facts = new FingerprintImageFacts("image.png", image.Size, false);
        var summary = new FingerprintComparisonSummary(FingerprintLumaNormalizer.NormalizationId, FingerprintDecisionPolicy.PolicyId, facts, facts, [], FingerprintOverview.Incomplete, DateTimeOffset.UnixEpoch, "限制");
        return new(image, image.Clone(), image.Clone(), image.Clone(), summary);
    }

    private sealed class CountingPrepare : IPrepareFingerprintComparisonUseCase
    { public int Calls { get; private set; } public Task<FingerprintComparisonSession> ExecuteAsync(FingerprintComparisonRequest request, CancellationToken cancellationToken) { Calls++; throw new InvalidOperationException(); } }
    private sealed class LatePrepare : IPrepareFingerprintComparisonUseCase
    {
        private readonly TaskCompletionSource<FingerprintComparisonSession> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<FingerprintComparisonSession> ExecuteAsync(FingerprintComparisonRequest request, CancellationToken cancellationToken) { Started.TrySetResult(); return _completion.Task; }
        public void Complete(FingerprintComparisonSession session) => _completion.SetResult(session);
    }
    private sealed class CancellationPrepare : IPrepareFingerprintComparisonUseCase
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ObservedCancellation { get; private set; }
        public async Task<FingerprintComparisonSession> ExecuteAsync(FingerprintComparisonRequest request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { ObservedCancellation = true; throw; }
            throw new InvalidOperationException();
        }
    }
    private sealed class NeverStability : IRunFingerprintStabilityUseCase
    { public Task<FingerprintStabilityResult> ExecuteAsync(FingerprintComparisonSession baseline, FingerprintStabilityRecipe recipe, IProgress<FingerprintStabilityProgress>? progress, CancellationToken cancellationToken) => throw new InvalidOperationException(); }
    private sealed class NullExport : IExportFingerprintReportUseCase
    { public string CreateJson(FingerprintReport report) => "{}"; public string CreateHumanReadableText(FingerprintReport report) => string.Empty; public Task ExecuteAsync(FingerprintReport report, string path, CancellationToken cancellationToken) => Task.CompletedTask; }
    private sealed class NullImageDialog : IImageFileDialog
    { public Task<string?> PickImageAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null); public Task<string?> PickOutputImageAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null); }
    private sealed class NullReportDialog : IFingerprintReportFileDialog
    { public Task<string?> PickFingerprintJsonOutputAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null); }
    private sealed class NullClipboard : ITextClipboard
    { public Task<bool> TrySetTextAsync(string text, CancellationToken cancellationToken) => Task.FromResult(false); }
    private sealed class NullCodec : IImageCodec
    {
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
    private sealed class TestLifetime : IDocumentLifetime { public CancellationToken ClosingToken => default; public bool IsClosing => false; }
    private sealed class CloseLifetime : IDocumentLifetime, IDisposable
    {
        private readonly CancellationTokenSource _source = new(); public CancellationToken ClosingToken => _source.Token; public bool IsClosing => _source.IsCancellationRequested;
        public void Close() => _source.Cancel(); public void Dispose() => _source.Dispose();
    }
}
