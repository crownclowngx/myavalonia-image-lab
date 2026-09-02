using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Application.FrequencyFiltering;

internal sealed record FrequencyFilterSessionRequest(string SourcePath, ImageChannel Channel, int AnalysisMaximumEdge);

/// <summary>拥有一次解码、一个分析代理和该代理的一份只读 FFT 缓存。</summary>
/// <remarks>
/// Session 不认识 Document、Bitmap、文件对话框或 DI。它只缓存“最后一个数学配方”的 raw IFFT，用于输出模式变化时
/// 跳过 FFT/IFFT；缓存由实例独占并在释放后拒绝访问，不会跨 Document Scope 泄漏。
/// </remarks>
internal sealed class FrequencyFilterSession : IDisposable
{
    private readonly object _cacheLock = new();
    private bool _disposed;
    private FrequencyFilterPlaneResult? _cachedRaw;
    public FrequencyFilterSession(string sourcePath, PixelImage sourceImage, PixelImage analysisProxy,
        ImageChannelPlane analysisPlane, FrequencySpectrum spectrum, PixelImage magnitudePreview, int analysisMaximumEdge)
    {
        SourcePath = sourcePath; SourceImage = sourceImage; AnalysisProxy = analysisProxy; AnalysisPlane = analysisPlane;
        Spectrum = spectrum; MagnitudePreview = magnitudePreview; AnalysisMaximumEdge = analysisMaximumEdge;
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
    public void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(FrequencyFilterSession)); }
    internal FrequencyFilterPlaneResult? TryGetRaw(string mathematicalFingerprint)
    { lock (_cacheLock) { ThrowIfDisposed(); return _cachedRaw is not null && _cachedRaw.MathematicalFingerprint == mathematicalFingerprint ? _cachedRaw : null; } }
    internal void StoreRaw(FrequencyFilterPlaneResult result)
    { lock (_cacheLock) { ThrowIfDisposed(); _cachedRaw = result; } }
    public void Dispose() { lock (_cacheLock) { _disposed = true; _cachedRaw = null; } }
}

internal sealed record FrequencyFilterStageTimings(TimeSpan Mask, TimeSpan MultiplyAndInverse,
    TimeSpan Projection, TimeSpan Diagnostics, bool UsedCachedRaw);

/// <summary>与 Session 和配方指纹绑定的一次代理或原尺寸结果。</summary>
internal sealed record FrequencyFilterResult(
    string SessionFingerprint,
    string RecipeFingerprint,
    string MathematicalFingerprint,
    bool IsFullSize,
    FrequencyFilterMask Mask,
    PixelImage MaskPreview,
    FrequencyFilterPlaneResult Raw,
    FrequencyProjectionResult Projection,
    ChannelDifferenceProjection Difference,
    FrequencySideEffectDiagnostics Diagnostics,
    FullReferenceQualityMetrics Quality,
    FrequencyFilterStageTimings Timings);

internal sealed record FrequencyFilterExportRequest(FrequencyFilterResult Result, string ExpectedSessionFingerprint,
    string ExpectedRecipeFingerprint, string OutputPath);
internal sealed record FrequencyFilterExportResult(string OutputPath, ImageSize Size, bool IsFullSize, string RecipeFingerprint);

internal interface IPrepareFrequencyFilterSessionUseCase
{
    Task<FrequencyFilterSession> ExecuteAsync(FrequencyFilterSessionRequest request, CancellationToken cancellationToken);
}
internal interface IApplyFrequencyFilterUseCase
{
    Task<FrequencyFilterResult> ExecuteAsync(FrequencyFilterSession session, FrequencyFilterRecipe recipe, CancellationToken cancellationToken);
}
internal interface ICompareFrequencySpatialUseCase
{
    Task<FrequencySpatialComparison> ExecuteAsync(FrequencyFilterSession session, FrequencyFilterRecipe recipe,
        int kernelSize, CancellationToken cancellationToken);
}
internal interface IRenderFullFrequencyFilterUseCase
{
    Task<FrequencyFilterResult> ExecuteAsync(FrequencyFilterSession session, FrequencyFilterRecipe recipe, CancellationToken cancellationToken);
}
internal interface IExportFrequencyFilterImageUseCase
{
    Task<FrequencyFilterExportResult> ExecuteAsync(FrequencyFilterExportRequest request, CancellationToken cancellationToken);
}
