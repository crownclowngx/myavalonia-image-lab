using ImageLabPlugin.Domain.Fingerprinting;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Application.Fingerprinting;

internal sealed record FingerprintComparisonRequest(string ReferencePath, string CandidatePath, int MaximumDisplayEdge = 1024);

internal sealed record FingerprintImageFacts(string Name, ImageSize Size, bool HasAlpha);

internal sealed record FingerprintAlgorithmResult(
    FingerprintAlgorithmId AlgorithmId,
    ImageFingerprint Reference,
    ImageFingerprint Candidate,
    FingerprintDistance Distance,
    int ReferenceThreshold,
    FingerprintDecision Decision,
    TimeSpan Elapsed,
    string Limitation);

internal sealed record FingerprintComparisonSummary(
    string NormalizationId,
    string DecisionPolicyId,
    FingerprintImageFacts Reference,
    FingerprintImageFacts Candidate,
    IReadOnlyList<FingerprintAlgorithmResult> Algorithms,
    FingerprintOverview Overview,
    DateTimeOffset CompletedAtUtc,
    string Disclaimer);

/// <summary>一次双图比较的私有大对象所有者。</summary>
/// <remarks>
/// Session 只归一个 Document 所有，长期持有两张完整图、两个 1024 代理和不可变摘要。
/// Dispose 后用 1×1 空图切断大数组引用，并使所有读取入口失败，防止迟到任务访问已关闭文档。
/// </remarks>
internal sealed class FingerprintComparisonSession : IDisposable
{
    private bool _disposed;

    public FingerprintComparisonSession(PixelImage referenceImage, PixelImage candidateImage, PixelImage referenceProxy, PixelImage candidateProxy, FingerprintComparisonSummary summary)
    {
        ReferenceImage = referenceImage;
        CandidateImage = candidateImage;
        ReferenceProxy = referenceProxy;
        CandidateProxy = candidateProxy;
        Summary = summary;
    }

    public PixelImage ReferenceImage { get; private set; }
    public PixelImage CandidateImage { get; private set; }
    public PixelImage ReferenceProxy { get; private set; }
    public PixelImage CandidateProxy { get; private set; }
    public FingerprintComparisonSummary Summary { get; }
    public bool IsDisposed => _disposed;

    public void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FingerprintComparisonSession));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var empty = new PixelImage(new ImageSize(1, 1), [0, 0, 0, 0]);
        ReferenceImage = empty;
        CandidateImage = empty.Clone();
        ReferenceProxy = empty.Clone();
        CandidateProxy = empty.Clone();
    }
}

internal sealed record FingerprintReport(int SchemaVersion, FingerprintComparisonSummary Comparison, FingerprintStabilityResult? Stability = null);

internal interface IPrepareFingerprintComparisonUseCase
{
    Task<FingerprintComparisonSession> ExecuteAsync(FingerprintComparisonRequest request, CancellationToken cancellationToken);
}

internal interface IExportFingerprintReportUseCase
{
    string CreateJson(FingerprintReport report);
    string CreateHumanReadableText(FingerprintReport report);
    Task ExecuteAsync(FingerprintReport report, string path, CancellationToken cancellationToken);
}
