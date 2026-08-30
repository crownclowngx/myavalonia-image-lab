using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Application.SpectrumAnalysis;

internal sealed record SpectrumAnalysisRequest(string SourcePath, ImageChannel Channel, int MaximumEdge);

internal sealed class SpectrumAnalysisSession : IDisposable
{
    private bool _disposed;

    public SpectrumAnalysisSession(
        PixelImage sourceImage,
        PixelImage proxyImage,
        ImageChannel channel,
        ImageChannelPlane channelPlane,
        FrequencySpectrum spectrum,
        RadialEnergyReport radialEnergy)
    {
        SourceImage = sourceImage;
        ProxyImage = proxyImage;
        Channel = channel;
        ChannelPlane = channelPlane;
        Spectrum = spectrum;
        RadialEnergy = radialEnergy;
    }

    public PixelImage SourceImage { get; private set; }
    public PixelImage ProxyImage { get; private set; }
    public ImageChannel Channel { get; }
    public ImageChannelPlane ChannelPlane { get; private set; }
    public FrequencySpectrum Spectrum { get; private set; }
    public RadialEnergyReport RadialEnergy { get; }
    public bool IsDisposed => _disposed;

    public void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpectrumAnalysisSession));
    }

    public void Dispose()
    {
        _disposed = true;
        // 托管大数组由 Session 的唯一引用控制。替换为空对象可立即切断 Document 到大缓冲的引用链。
        SourceImage = new PixelImage(new ImageSize(1, 1), [0, 0, 0, 0]);
        ProxyImage = new PixelImage(new ImageSize(1, 1), [0, 0, 0, 0]);
        ChannelPlane = new ImageChannelPlane(new ImageSize(1, 1), Channel, [0d]);
        Spectrum = new FrequencySpectrum(new ImageSize(1, 1), 1, 1, [System.Numerics.Complex.Zero]);
    }
}

internal sealed record SpectrumAnalysisResult(
    SpectrumAnalysisSession Session,
    PixelImage MagnitudePreview,
    PixelImage PhasePreview,
    PixelImage DctPreview);

internal sealed record BandReconstructionResult(
    PixelImage Image,
    PixelImage MaskPreview,
    int ClippedPixelCount,
    double MaximumImaginaryResidual,
    bool UsedExactAllPassShortcut);

internal interface IAnalyzeSpectrumUseCase
{
    Task<SpectrumAnalysisResult> ExecuteAsync(SpectrumAnalysisRequest request, CancellationToken cancellationToken);
}

internal interface IInspectDctBlockUseCase
{
    DctBlockReport Execute(SpectrumAnalysisSession session, ImagePoint sourcePoint);
}

internal interface IReconstructSpectrumBandUseCase
{
    Task<BandReconstructionResult> ExecuteAsync(
        SpectrumAnalysisSession session,
        FrequencyBandDefinition band,
        CancellationToken cancellationToken);
}

/// <summary>按需把缓存频谱转换为显示投影或频点 DTO，不重新执行 FFT。</summary>
internal interface IProjectSpectrumUseCase
{
    PixelImage CreateMagnitude(SpectrumAnalysisSession session, SpectrumMagnitudeMode mode, CancellationToken cancellationToken);
    FrequencyPointInfo Inspect(SpectrumAnalysisSession session, int displayX, int displayY, FrequencyBandBoundaries boundaries);
    RadialEnergyReport AnalyzeEnergy(SpectrumAnalysisSession session, FrequencyBandBoundaries boundaries, CancellationToken cancellationToken);
}
