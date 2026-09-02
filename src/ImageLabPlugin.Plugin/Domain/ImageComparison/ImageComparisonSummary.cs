using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.ImageComparison;

/// <summary>不依赖 UI、路径、JSON 与文件系统的统一比较事实。</summary>
internal sealed record ImageComparisonSummary(
    string AlgorithmId,
    ImageSize ReferenceSize,
    ImageSize CandidateSize,
    bool IsComparable,
    ImagePairMismatch? Mismatch,
    string ColorFormulaId,
    string AlphaRule,
    FullReferenceQualityMetrics? Metrics,
    ImagePairHistograms? Histograms)
{
    public const string CurrentAlgorithmId = "image-compare-v1";
    public const string CurrentColorFormulaId = "bt601-full-range-unpremultiplied-rgba8888";
    public const string CurrentAlphaRule = "alpha-excluded-from-color-metrics-and-reported-separately";
}
