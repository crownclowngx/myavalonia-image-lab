using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.ImageOscilloscope;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Application.ImageOscilloscope;

/// <summary>解码一次并在后台完成全部主分析，只有完整候选成功后才返回可提交 Session。</summary>
internal sealed class PrepareImageOscilloscopeSessionUseCase(
    IImageCodec codec, ImageOscilloscopeAnalyzer analyzer, ClippingAnalyzer clippingAnalyzer,
    ImageOscilloscopePreviewProjector previewProjector) : IPrepareImageOscilloscopeSessionUseCase
{
    public async Task<ImageOscilloscopeSession> ExecuteAsync(string path, ClippingThresholds thresholds,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var source = await codec.DecodeAsync(path, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var analysis = analyzer.Analyze(source, cancellationToken);
            var clipping = clippingAnalyzer.Analyze(source, thresholds, cancellationToken);
            var preview = previewProjector.Project(source, cancellationToken);
            return new ImageOscilloscopeSession(source, preview, analysis, clipping);
        }, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>只重新扫描阈值事实；不重建主 Scope、直方图或显示代理。</summary>
internal sealed class RecalculateImageOscilloscopeClippingUseCase(ClippingAnalyzer analyzer)
    : IRecalculateImageOscilloscopeClippingUseCase
{
    public Task<ClippingAnalysis> ExecuteAsync(ImageOscilloscopeSession session, ClippingThresholds thresholds,
        long generation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.ThrowIfDisposed();
        return Task.Run(() => analyzer.Analyze(session.Source, thresholds, cancellationToken), cancellationToken);
    }
}

/// <summary>协调 P99.5 密度投影与着色；切换显示模式不会访问源像素。</summary>
internal sealed class ProjectImageOscilloscopeDisplayUseCase(
    ScopeDensityProjector densityProjector, ImageOscilloscopeRasterizer rasterizer,
    ClippingAnalyzer clippingAnalyzer) : IProjectImageOscilloscopeDisplayUseCase
{
    public ImageOscilloscopeDensitySet Project(ImageOscilloscopeSession session, ScopeDensityMode mode,
        CancellationToken cancellationToken = default)
    {
        session.ThrowIfDisposed();
        var analysis = session.Analysis;
        var parade = densityProjector.ProjectParade(analysis.RedParade, analysis.GreenParade, analysis.BlueParade, mode, cancellationToken);
        return new ImageOscilloscopeDensitySet(
            densityProjector.Project(analysis.Waveform, mode, cancellationToken), parade.Red, parade.Green, parade.Blue,
            densityProjector.Project(analysis.Vectorscope, mode, cancellationToken));
    }

    public ImageOscilloscopeRasterSet Rasterize(ImageOscilloscopeDensitySet densities,
        CancellationToken cancellationToken = default) => new(
        rasterizer.Rasterize(densities.Waveform, 80, 225, 255, cancellationToken),
        rasterizer.RasterizeParade(densities.RedParade, densities.GreenParade, densities.BlueParade, cancellationToken),
        rasterizer.Rasterize(densities.Vectorscope, 255, 190, 72, cancellationToken));

    public PixelImage CreateClippingOverlay(ImageOscilloscopeSession session, ScopeClippingMode mode,
        CancellationToken cancellationToken = default)
    {
        session.ThrowIfDisposed();
        return clippingAnalyzer.CreateOverlay(session.CurrentClipping, mode, cancellationToken);
    }
}

internal sealed class InspectImageOscilloscopePixelUseCase(ScopeProbeMapper mapper)
    : IInspectImageOscilloscopePixelUseCase
{
    public ScopeProbe Execute(ImageOscilloscopeSession session, int sourceX, int sourceY)
    {
        session.ThrowIfDisposed();
        return mapper.Map(session.Source, sourceX, sourceY, session.Analysis.Waveform.Width);
    }
}
