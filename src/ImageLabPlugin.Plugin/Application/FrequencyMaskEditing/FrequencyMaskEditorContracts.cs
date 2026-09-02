using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.FrequencyMaskEditing;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Application.FrequencyMaskEditing;

internal sealed record FrequencyMaskSessionRequest(string SourcePath, ImageChannel Channel, int AnalysisMaximumEdge);

/// <summary>独占拥有一次解码、代理通道和只读 FFT；释放后所有用例必须拒绝继续执行。</summary>
internal sealed class FrequencyMaskEditorSession : IDisposable
{
    private bool _disposed;

    public FrequencyMaskEditorSession(string sourcePath, PixelImage sourceImage, PixelImage analysisProxy,
        ImageChannelPlane analysisPlane, FrequencySpectrum spectrum, PixelImage magnitudePreview, int analysisMaximumEdge)
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
    public void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(FrequencyMaskEditorSession)); }
    public void Dispose() => _disposed = true;
}

internal sealed record FrequencyMaskRenderTimings(TimeSpan Raster, TimeSpan MultiplyAndInverse,
    TimeSpan Projection, TimeSpan Diagnostics);

internal sealed record FrequencyMaskRawStatistics(double Minimum, double Maximum, long BelowZero, long Above255,
    int ColorReconstructionClippedPixels);

/// <summary>与 Session、配方、强度和尺寸绑定的不可变重建结果。</summary>
internal sealed record FrequencyMaskRenderResult(
    string SessionFingerprint,
    string RecipeFingerprint,
    string ResultFingerprint,
    bool IsFullSize,
    FrequencyGainMask EditMask,
    FrequencyGainMask EffectiveMask,
    PixelImage MaskPreview,
    FrequencyMaskApplicationResult Raw,
    PixelImage Reconstruction,
    ChannelDifferenceProjection Difference,
    FrequencyMaskStatistics MaskStatistics,
    FrequencyMaskRawStatistics RawStatistics,
    FullReferenceQualityMetrics Quality,
    FrequencyMaskRenderTimings Timings);

internal enum FrequencyMaskExportArtifact { Reconstruction, MaskPreview }
internal sealed record FrequencyMaskImageExportRequest(FrequencyMaskRenderResult Result, string ExpectedSessionFingerprint,
    string ExpectedRecipeFingerprint, FrequencyMaskExportArtifact Artifact, string OutputPath);
internal sealed record FrequencyMaskImageExportResult(string OutputPath, ImageSize Size,
    FrequencyMaskExportArtifact Artifact, bool IsFullSize);

internal sealed record FrequencyMaskPointInspection(int DisplayX, int DisplayY, int InternalX, int InternalY,
    int ConjugateDisplayX, int ConjugateDisplayY, int ConjugateInternalX, int ConjugateInternalY, double FrequencyX, double FrequencyY, double Radius,
    double OriginalMagnitude, double EditGain, double EffectiveGain);

internal interface IPrepareFrequencyMaskEditorSessionUseCase
{
    Task<FrequencyMaskEditorSession> ExecuteAsync(FrequencyMaskSessionRequest request, CancellationToken cancellationToken);
}
internal interface IRenderFrequencyMaskUseCase
{
    Task<FrequencyMaskRenderResult> ExecuteAsync(FrequencyMaskEditorSession session, FrequencyMaskRecipe recipe,
        CancellationToken cancellationToken);
}
internal interface IRenderFullFrequencyMaskUseCase
{
    Task<FrequencyMaskRenderResult> ExecuteAsync(FrequencyMaskEditorSession session, FrequencyMaskRecipe recipe,
        CancellationToken cancellationToken);
}
internal interface IExportFrequencyMaskImageUseCase
{
    Task<FrequencyMaskImageExportResult> ExecuteAsync(FrequencyMaskImageExportRequest request, CancellationToken cancellationToken);
}
internal interface IInspectFrequencyMaskPointUseCase
{
    FrequencyMaskPointInspection Execute(FrequencyMaskEditorSession session, FrequencyMaskRenderResult result,
        double normalizedX, double normalizedY);
}
internal interface IFrequencyMaskRecipeSerializer
{
    byte[] Serialize(FrequencyMaskRecipe recipe);
    FrequencyMaskRecipe Deserialize(ReadOnlySpan<byte> json);
}
internal interface IImportFrequencyMaskRecipeUseCase
{
    Task<FrequencyMaskRecipe> ExecuteAsync(string path, CancellationToken cancellationToken);
}
internal interface IExportFrequencyMaskRecipeUseCase
{
    Task ExecuteAsync(FrequencyMaskRecipe recipe, string path, CancellationToken cancellationToken);
}
