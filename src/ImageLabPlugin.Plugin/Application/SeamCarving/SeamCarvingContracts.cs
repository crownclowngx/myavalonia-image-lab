using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.SeamCarving;

namespace ImageLabPlugin.Application.SeamCarving;

internal sealed record SeamComparison(
    ReferenceResizeAlgorithm Algorithm,
    PixelImage ReferenceImage,
    PixelImage DifferenceImage,
    FullReferenceQualityMetrics SeamVsReference);

internal sealed record SeamCarvingReport(
    string InputFingerprint,
    ImageSize InputSize,
    ImageSize TargetSize,
    SeamAxisOrder AxisOrder,
    ReferenceResizeAlgorithm ReferenceAlgorithm,
    SeamPlaybackState Status,
    SeamResourceEstimate ResourceEstimate,
    IReadOnlyList<SeamStepRecord> Steps,
    (long Normal, long Protect, long PreferRemoval) MaskCounts,
    FullReferenceQualityMetrics? SeamVsReference,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<string> Warnings);

internal interface IPrepareSeamCarvingSessionUseCase
{
    Task ExecuteAsync(SeamCarvingSession session, string sourcePath, CancellationToken cancellationToken);
}

internal interface IEditSeamMaskUseCase
{
    void Apply(SeamCarvingSession session, IReadOnlyList<SeamBrushStroke> strokes,
        CancellationToken cancellationToken = default);
}

internal interface IPlanSeamResizeUseCase
{
    SeamResizePlan Execute(SeamCarvingSession session, SeamResizeRequest request);
}

internal interface IPreviewNextSeamUseCase
{
    Task<SeamStepPreview?> ExecuteAsync(SeamCarvingSession session, CancellationToken cancellationToken);
}

internal interface IApplySeamStepUseCase
{
    Task<SeamStepRecord?> ExecuteAsync(SeamCarvingSession session, CancellationToken cancellationToken);
}

internal interface IRunSeamPlaybackUseCase
{
    Task ExecuteAsync(SeamCarvingSession session, Func<SeamStepRecord, Task>? progress,
        Func<bool>? shouldPause, CancellationToken cancellationToken);
}

internal interface ICompareSeamResizeUseCase
{
    Task<SeamComparison> ExecuteAsync(SeamCarvingSession session, CancellationToken cancellationToken);
}

internal interface IExportSeamResultUseCase
{
    Task ExecuteAsync(SeamCarvingSession session, string outputPath, CancellationToken cancellationToken);
}

internal interface IExportSeamReportUseCase
{
    Task ExecuteAsync(SeamCarvingReport report, string outputPath, bool csv, CancellationToken cancellationToken);
}

internal interface ISeamCarvingReportSerializer
{
    byte[] SerializeJson(SeamCarvingReport report);
    byte[] SerializeCsv(SeamCarvingReport report);
}
