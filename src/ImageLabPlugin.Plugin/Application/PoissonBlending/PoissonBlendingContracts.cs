using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.PoissonBlending;

namespace ImageLabPlugin.Application.PoissonBlending;

internal sealed record PoissonBlendingReport(
    string SourceFingerprint,
    string TargetFingerprint,
    ImageSize SourceSize,
    ImageSize TargetSize,
    PoissonBlendMode Mode,
    ImageOffset Offset,
    PoissonMaskTopology Topology,
    PoissonBlendOptions Options,
    PoissonResourceEstimate ResourceEstimate,
    IReadOnlyList<PoissonResidual> Residuals,
    PoissonStopReason StopReason,
    PoissonBlendDiagnostics Diagnostics,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<string> Warnings);

internal interface IPreparePoissonSessionUseCase
{ Task ExecuteAsync(PoissonBlendingSession session, string sourcePath, string targetPath, CancellationToken cancellationToken); }
internal interface IEditPoissonMaskUseCase
{ PoissonMaskTopology Apply(PoissonBlendingSession session, PoissonMaskDefinition definition, CancellationToken cancellationToken = default); }
internal interface IPlacePoissonRegionUseCase
{ PoissonPlacementValidation Apply(PoissonBlendingSession session, ImageOffset offset, CancellationToken cancellationToken = default); }
internal interface IBuildPoissonProblemUseCase
{ PoissonProblem Execute(PoissonBlendingSession session, PoissonBlendOptions options, CancellationToken cancellationToken = default); }
internal interface IStepPoissonSolverUseCase
{ Task<PoissonResidual> ExecuteAsync(PoissonBlendingSession session, CancellationToken cancellationToken); }
internal interface IRunPoissonSolverUseCase
{ Task ExecuteAsync(PoissonBlendingSession session, Func<PoissonResidual, Task>? progress, Func<bool>? shouldPause, CancellationToken cancellationToken); }
internal interface IExportPoissonImageUseCase
{ Task ExecuteAsync(PoissonBlendingSession session, string outputPath, bool alphaBaseline, bool allowUnconvergedPreview, CancellationToken cancellationToken); }
internal interface IExportPoissonReportUseCase
{ Task ExecuteAsync(PoissonBlendingReport report, string outputPath, bool csv, CancellationToken cancellationToken); }
internal interface IPoissonBlendingReportSerializer
{ byte[] SerializeJson(PoissonBlendingReport report); byte[] SerializeCsv(PoissonBlendingReport report); }

/// <summary>梯度域融合只暴露两种 PNG 和 JSON/CSV 报告选择意图，避免继续扩大通用图片端口。</summary>
