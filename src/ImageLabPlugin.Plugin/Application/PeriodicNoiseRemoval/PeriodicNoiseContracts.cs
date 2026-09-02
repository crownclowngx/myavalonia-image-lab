using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.PeriodicNoiseRemoval;

namespace ImageLabPlugin.Application.PeriodicNoiseRemoval;

internal sealed record PeriodicNoiseSessionRequest(string SourcePath, ImageChannel Channel, int AnalysisMaximumEdge);

/// <summary>独占一次解码、代理、选定通道、只读 FFT 和原始频谱预览。</summary>
/// <remarks>
/// Session 由一个 Document Scope 显式拥有；路径、通道或代理档位变化时整体释放。它不持有 Avalonia Bitmap，
/// 释放后所有应用用例都必须拒绝执行，防止异步迟到结果跨实例复用缓存。
/// </remarks>
internal sealed class PeriodicNoiseSession : IDisposable
{
    private bool _disposed;

    public PeriodicNoiseSession(string sourcePath, PixelImage sourceImage, PixelImage analysisProxy,
        ImageChannelPlane analysisPlane, FrequencySpectrum spectrum, PixelImage magnitudePreview,
        int analysisMaximumEdge)
    {
        SourcePath = sourcePath;
        SourceImage = sourceImage;
        AnalysisProxy = analysisProxy;
        AnalysisPlane = analysisPlane;
        Spectrum = spectrum;
        MagnitudePreview = magnitudePreview;
        AnalysisMaximumEdge = analysisMaximumEdge;
        SessionFingerprint = Guid.NewGuid().ToString("N");
    }

    public string SourcePath { get; }
    public PixelImage SourceImage { get; }
    public PixelImage AnalysisProxy { get; }
    public ImageChannelPlane AnalysisPlane { get; }
    public FrequencySpectrum Spectrum { get; }
    public PixelImage MagnitudePreview { get; }
    public int AnalysisMaximumEdge { get; }
    public ImageChannel Channel => AnalysisPlane.Channel;
    public string SessionFingerprint { get; }
    public bool IsDisposed => _disposed;
    public bool CanRenderFullSize => SourceImage.Size.Width <= 2048 && SourceImage.Size.Height <= 2048;
    public void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PeriodicNoiseSession));
    }
    public void Dispose() => _disposed = true;
}

/// <summary>与 Session、配方、草案状态和尺寸绑定的不可变周期陷波结果。</summary>
internal sealed record PeriodicNoiseRenderResult(
    string SessionFingerprint,
    string RecipeFingerprint,
    string MathematicalFingerprint,
    bool IsDraft,
    bool IsFullSize,
    PeriodicNotchMask Mask,
    FrequencyMaskApplicationResult Raw,
    PixelImage Reconstruction,
    PixelImage FilteredSpectrumPreview,
    ChannelDifferenceProjection Difference,
    PeriodicNoiseLossDiagnostics Diagnostics);

internal enum PeriodicNoiseExportArtifact { Reconstruction, MaskPreview }
internal sealed record PeriodicNoiseArtifactExportRequest(PeriodicNoiseRenderResult Result,
    string ExpectedSessionFingerprint, string ExpectedRecipeFingerprint, PeriodicNoiseExportArtifact Artifact,
    string OutputPath);
internal sealed record PeriodicNoiseArtifactExportResult(string OutputPath, ImageSize Size,
    PeriodicNoiseExportArtifact Artifact, bool IsFullSize);

internal interface IPreparePeriodicNoiseSessionUseCase
{
    Task<PeriodicNoiseSession> ExecuteAsync(PeriodicNoiseSessionRequest request, CancellationToken cancellationToken);
}

internal interface IDetectPeriodicNoiseCandidatesUseCase
{
    Task<PeriodicNoiseDetectionResult> ExecuteAsync(PeriodicNoiseSession session,
        PeriodicNoiseDetectionSettings settings, CancellationToken cancellationToken);
}

internal interface IMapPeriodicSpectrumSelectionUseCase
{
    PeriodicFrequency Execute(PeriodicNoiseSession session, double normalizedX, double normalizedY);
}

internal interface IRenderPeriodicNoisePreviewUseCase
{
    Task<PeriodicNoiseRenderResult> ExecuteAsync(PeriodicNoiseSession session, PeriodicNoiseRecipe recipe,
        IReadOnlyList<PeriodicFrequencyCandidate> selectedCandidates, bool isDraft, CancellationToken cancellationToken);
}

internal interface IRenderFullPeriodicNoiseResultUseCase
{
    Task<PeriodicNoiseRenderResult> ExecuteAsync(PeriodicNoiseSession session, PeriodicNoiseRecipe recipe,
        IReadOnlyList<PeriodicFrequencyCandidate> selectedCandidates, CancellationToken cancellationToken);
}

internal interface IPeriodicNoiseRecipeSerializer
{
    byte[] Serialize(PeriodicNoiseRecipe recipe);
    PeriodicNoiseRecipe Deserialize(ReadOnlySpan<byte> json);
}

internal interface IPeriodicNoiseCandidateSummarySerializer
{
    byte[] Serialize(PeriodicNoiseSession session, PeriodicNoiseDetectionResult detection);
}

internal interface IImportPeriodicNoiseRecipeUseCase
{
    Task<PeriodicNoiseRecipe> ExecuteAsync(string path, CancellationToken cancellationToken);
}

internal interface IExportPeriodicNoiseRecipeUseCase
{
    Task ExecuteAsync(PeriodicNoiseRecipe recipe, string path, CancellationToken cancellationToken);
}

internal interface IExportPeriodicNoiseCandidateSummaryUseCase
{
    Task ExecuteAsync(PeriodicNoiseSession session, PeriodicNoiseDetectionResult detection, string path,
        CancellationToken cancellationToken);
}

internal interface IExportPeriodicNoiseArtifactUseCase
{
    Task<PeriodicNoiseArtifactExportResult> ExecuteAsync(PeriodicNoiseArtifactExportRequest request,
        CancellationToken cancellationToken);
}
