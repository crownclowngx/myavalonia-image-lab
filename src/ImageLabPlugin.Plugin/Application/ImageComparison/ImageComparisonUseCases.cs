using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Application.ImageComparison;

/// <summary>按受控峰值内存顺序协调解码、验证、完整统计与 Session 建立。</summary>
internal sealed class PrepareImageComparisonUseCase(
    IImageCodec codec,
    ImagePairValidator validator,
    ImageAnalysisProxyProjector proxyProjector,
    FullReferenceQualityAnalyzer qualityAnalyzer,
    ImageHistogramAnalyzer histogramAnalyzer,
    ImageDifferenceProxyAnalyzer differenceAnalyzer) : IPrepareImageComparisonUseCase
{
    public async Task<ImageComparisonResult> ExecuteAsync(ImageComparisonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ReferencePath)) throw new ArgumentException("请选择参考图。", nameof(request));
        if (string.IsNullOrWhiteSpace(request.CandidatePath)) throw new ArgumentException("请选择待比较图。", nameof(request));
        if (request.MaximumDisplayEdge != 1024) throw new ArgumentOutOfRangeException(nameof(request), "V1 显示代理最大边固定为 1024。 ");

        // 顺序解码避免两次解码同时制造临时峰值；编解码端口自己负责取消与安全尺寸上限。
        var reference = await codec.DecodeAsync(request.ReferencePath, cancellationToken).ConfigureAwait(false);
        var candidate = await codec.DecodeAsync(request.CandidatePath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var mismatch = validator.Validate(reference, candidate);
        if (mismatch is not null)
        {
            var referencePreview = proxyProjector.Create(reference, 1024, cancellationToken);
            var candidatePreview = proxyProjector.Create(candidate, 1024, cancellationToken);
            var summary = new ImageComparisonSummary(
                ImageComparisonSummary.CurrentAlgorithmId, reference.Size, candidate.Size, false, mismatch,
                ImageComparisonSummary.CurrentColorFormulaId, ImageComparisonSummary.CurrentAlphaRule, null, null);
            return new ImageComparisonResult(null, referencePreview, candidatePreview, mismatch, summary);
        }

        return await Task.Run(() => CreateComparable(reference, candidate, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private ImageComparisonResult CreateComparable(PixelImage reference, PixelImage candidate, CancellationToken cancellationToken)
    {
        var referenceProxy = proxyProjector.Create(reference, 1024, cancellationToken);
        var candidateProxy = proxyProjector.Create(candidate, 1024, cancellationToken);
        var metrics = qualityAnalyzer.Analyze(reference, candidate, cancellationToken);
        var histograms = histogramAnalyzer.Analyze(reference, candidate, cancellationToken);
        var difference = differenceAnalyzer.Analyze(reference, candidate, 1024, cancellationToken);
        var summary = new ImageComparisonSummary(
            ImageComparisonSummary.CurrentAlgorithmId, reference.Size, candidate.Size, true, null,
            ImageComparisonSummary.CurrentColorFormulaId, ImageComparisonSummary.CurrentAlphaRule, metrics, histograms);
        var session = new ImageComparisonSession(reference, candidate, referenceProxy, candidateProxy, difference, summary);
        return new ImageComparisonResult(session, referenceProxy, candidateProxy, null, summary);
    }
}

internal sealed class ProjectImageDifferenceUseCase(
    ImageDifferenceProxyProjector differenceProjector,
    DifferenceHeatmapProjector heatmapProjector) : IProjectImageDifferenceUseCase
{
    public DifferenceProjectionResult Execute(
        ImageComparisonSession session,
        DifferenceProjectionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(options);
        session.ThrowIfDisposed();
        return options.Kind == DifferenceProjectionKind.Rgb
            ? differenceProjector.Project(session.DifferenceProxy, options.Amplification, cancellationToken)
            : heatmapProjector.Project(session.DifferenceProxy, options.HeatmapSource, options.Amplification, cancellationToken);
    }
}

internal sealed class InspectImagePairUseCase(ImagePairPixelInspector inspector) : IInspectImagePairUseCase
{
    public ImagePairPixelReport Execute(ImageComparisonSession session, ImagePoint sourcePoint)
    {
        ArgumentNullException.ThrowIfNull(session); session.ThrowIfDisposed();
        return inspector.Inspect(session.ReferenceImage, session.CandidateImage, sourcePoint);
    }
}
