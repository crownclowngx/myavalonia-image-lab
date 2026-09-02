using ImageLabPlugin.Domain.ImageOscilloscope;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Application.ImageOscilloscope;

internal static class ImageOscilloscopeProtocol
{
    public const int SnapshotSchema = 1;
    public const int CoordinateProtocolVersion = 1;
}

internal sealed record ImageOscilloscopeDensitySet(
    ScopeDensityProjection Waveform, ScopeDensityProjection RedParade,
    ScopeDensityProjection GreenParade, ScopeDensityProjection BlueParade,
    ScopeDensityProjection Vectorscope);

internal sealed record ImageOscilloscopeRasterSet(PixelImage Waveform, PixelImage Parade, PixelImage Vectorscope);

internal sealed record ImageOscilloscopeSnapshotState(
    bool WaveformVisible, bool ParadeVisible, bool VectorscopeVisible, bool HistogramVisible,
    ScopeDensityMode DensityMode, int ShadowThreshold, int HighlightThreshold,
    ScopeClippingMode ClippingMode, double? PinnedX, double? PinnedY, double Zoom);

internal interface IPrepareImageOscilloscopeSessionUseCase
{
    Task<ImageOscilloscopeSession> ExecuteAsync(string path, ClippingThresholds thresholds, CancellationToken cancellationToken);
}

internal interface IRecalculateImageOscilloscopeClippingUseCase
{
    Task<ClippingAnalysis> ExecuteAsync(ImageOscilloscopeSession session, ClippingThresholds thresholds,
        long generation, CancellationToken cancellationToken);
}

internal interface IProjectImageOscilloscopeDisplayUseCase
{
    ImageOscilloscopeDensitySet Project(ImageOscilloscopeSession session, ScopeDensityMode mode,
        CancellationToken cancellationToken = default);
    ImageOscilloscopeRasterSet Rasterize(ImageOscilloscopeDensitySet densities,
        CancellationToken cancellationToken = default);
    PixelImage CreateClippingOverlay(ImageOscilloscopeSession session, ScopeClippingMode mode,
        CancellationToken cancellationToken = default);
}

internal interface IInspectImageOscilloscopePixelUseCase
{
    ScopeProbe Execute(ImageOscilloscopeSession session, int sourceX, int sourceY);
}
