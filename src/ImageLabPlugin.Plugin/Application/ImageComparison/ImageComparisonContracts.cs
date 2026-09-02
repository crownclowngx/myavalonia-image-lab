using ImageLabPlugin.Domain.ImageComparison;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Application.ImageComparison;

internal sealed record ImageComparisonRequest(string ReferencePath, string CandidatePath, int MaximumDisplayEdge = 1024);

/// <summary>一次有效双图比较所拥有的大对象边界。</summary>
/// <remarks>
/// Session 只归一个 Document 所有。Dispose 会切断两张原图、两张代理和差异场的引用；调用方不得把其中对象
/// 缓存到 singleton。所有读取入口先检查释放状态，避免迟到投影继续访问已关闭的 Document。
/// </remarks>
internal sealed class ImageComparisonSession : IDisposable
{
    private bool _disposed;

    public ImageComparisonSession(
        PixelImage referenceImage,
        PixelImage candidateImage,
        PixelImage referenceProxy,
        PixelImage candidateProxy,
        ImageDifferenceProxy differenceProxy,
        ImageComparisonSummary summary)
    {
        ReferenceImage = referenceImage;
        CandidateImage = candidateImage;
        ReferenceProxy = referenceProxy;
        CandidateProxy = candidateProxy;
        DifferenceProxy = differenceProxy;
        Summary = summary;
    }

    public PixelImage ReferenceImage { get; private set; }
    public PixelImage CandidateImage { get; private set; }
    public PixelImage ReferenceProxy { get; private set; }
    public PixelImage CandidateProxy { get; private set; }
    public ImageDifferenceProxy DifferenceProxy { get; private set; }
    public ImageComparisonSummary Summary { get; }
    public bool IsDisposed => _disposed;

    public void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ImageComparisonSession));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var emptySize = new ImageSize(1, 1);
        var empty = new PixelImage(emptySize, [0, 0, 0, 0]);
        ReferenceImage = empty; CandidateImage = empty.Clone();
        ReferenceProxy = empty.Clone(); CandidateProxy = empty.Clone();
        DifferenceProxy = new ImageDifferenceProxy(emptySize, [0], [0], [0], [0], [0]);
    }
}

internal sealed record ImageComparisonResult(
    ImageComparisonSession? Session,
    PixelImage ReferencePreview,
    PixelImage CandidatePreview,
    ImagePairMismatch? Mismatch,
    ImageComparisonSummary Summary)
{
    public bool IsComparable => Session is not null;
}

internal sealed record ImageComparisonReport(
    int SchemaVersion,
    string ReferenceName,
    string CandidateName,
    DateTimeOffset CompletedAtUtc,
    ImageComparisonSummary Summary,
    ImageComparisonProjectionReport? Projection = null);

/// <summary>记录导出时当前视觉投影的解释参数；它不改变完整像素领域摘要。</summary>
internal sealed record ImageComparisonProjectionReport(
    DifferenceProjectionKind Kind,
    int Amplification,
    HeatmapScalarSource? HeatmapSource,
    int SaturatedProxyPixelCount);

internal interface IPrepareImageComparisonUseCase
{
    Task<ImageComparisonResult> ExecuteAsync(ImageComparisonRequest request, CancellationToken cancellationToken);
}

internal interface IProjectImageDifferenceUseCase
{
    DifferenceProjectionResult Execute(
        ImageComparisonSession session,
        DifferenceProjectionOptions options,
        CancellationToken cancellationToken);
}

internal interface IInspectImagePairUseCase
{
    ImagePairPixelReport Execute(ImageComparisonSession session, ImagePoint sourcePoint);
}

internal interface IExportComparisonSummaryUseCase
{
    Task ExecuteAsync(ImageComparisonReport report, string targetPath, CancellationToken cancellationToken);
    string CreateJson(ImageComparisonReport report);
    string CreateHumanReadableText(ImageComparisonReport report);
}
