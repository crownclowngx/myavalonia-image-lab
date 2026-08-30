using System.Numerics;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Application.SpectrumAnalysis;

internal sealed class AnalyzeSpectrumUseCase(
    IImageCodec codec,
    ImageAnalysisProxyProjector proxyProjector,
    ImageChannelConverter channelConverter,
    Fft2DTransform fft,
    SpectrumProjector spectrumProjector,
    DctSpectrumProjector dctProjector,
    RadialEnergyAnalyzer radialAnalyzer) : IAnalyzeSpectrumUseCase
{
    public async Task<SpectrumAnalysisResult> ExecuteAsync(SpectrumAnalysisRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SourcePath)) throw new ArgumentException("请选择图片。", nameof(request));
        var source = await codec.DecodeAsync(request.SourcePath, cancellationToken).ConfigureAwait(false);
        var proxy = proxyProjector.Create(source, request.MaximumEdge, cancellationToken);
        var plane = channelConverter.Extract(proxy, request.Channel, cancellationToken);
        var paddedWidth = FrequencySpectrum.NextPowerOfTwo(proxy.Size.Width);
        var paddedHeight = FrequencySpectrum.NextPowerOfTwo(proxy.Size.Height);
        var values = new Complex[checked(paddedWidth * paddedHeight)];
        var neutral = ImageChannelConverter.NeutralValue(request.Channel);
        if (neutral != 0d) Array.Fill(values, new Complex(neutral, 0d));
        for (var y = 0; y < proxy.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < proxy.Size.Width; x++) values[(y * paddedWidth) + x] = new Complex(plane[x, y], 0d);
        }
        fft.Forward(values, paddedWidth, paddedHeight, cancellationToken);
        var spectrum = new FrequencySpectrum(proxy.Size, paddedWidth, paddedHeight, values);
        var radial = radialAnalyzer.Analyze(spectrum, FrequencyBandBoundaries.Default, cancellationToken);
        var session = new SpectrumAnalysisSession(source, proxy, request.Channel, plane, spectrum, radial);
        try
        {
            return new SpectrumAnalysisResult(
                session,
                spectrumProjector.CreateMagnitude(spectrum, SpectrumMagnitudeMode.Logarithmic, cancellationToken),
                spectrumProjector.CreatePhase(spectrum, cancellationToken),
                dctProjector.Create(proxy, request.Channel, cancellationToken));
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }
}

internal sealed class InspectDctBlockUseCase(DctBlockAnalyzer analyzer) : IInspectDctBlockUseCase
{
    public DctBlockReport Execute(SpectrumAnalysisSession session, ImagePoint sourcePoint)
    {
        ArgumentNullException.ThrowIfNull(session); session.ThrowIfDisposed();
        return analyzer.Analyze(session.SourceImage, session.Channel, sourcePoint);
    }
}

internal sealed class ReconstructSpectrumBandUseCase(
    Fft2DTransform fft,
    FrequencyBandMaskFactory maskFactory,
    ImageChannelConverter channelConverter) : IReconstructSpectrumBandUseCase
{
    public Task<BandReconstructionResult> ExecuteAsync(
        SpectrumAnalysisSession session,
        FrequencyBandDefinition band,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session); session.ThrowIfDisposed();
        // IFFT 和通道合成是 CPU 密集工作。放到线程池后，Document 的 150 ms 防抖和取消才能真正保持 UI 响应。
        return Task.Run(() => Execute(session, band, cancellationToken), cancellationToken);
    }

    private BandReconstructionResult Execute(
        SpectrumAnalysisSession session,
        FrequencyBandDefinition band,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var spectrum = session.Spectrum;
        var proxy = session.ProxyImage;
        var channel = session.Channel;
        var mask = maskFactory.Create(spectrum, band, cancellationToken);
        var maskPreview = maskFactory.CreatePreview(spectrum, mask);
        if (band.Kind == FrequencyBandKind.All)
        {
            return new BandReconstructionResult(proxy.Clone(), maskPreview, 0, 0d, true);
        }

        var working = spectrum.CreateWorkingCopy();
        for (var i = 0; i < working.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (mask[i] == 0) working[i] = Complex.Zero;
        }
        fft.Inverse(working, spectrum.PaddedWidth, spectrum.PaddedHeight, cancellationToken);
        double maximumImaginary = 0d;
        var reconstructed = new double[checked((int)proxy.Size.PixelCount)];
        for (var y = 0; y < proxy.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < proxy.Size.Width; x++)
            {
                var value = working[(y * spectrum.PaddedWidth) + x];
                maximumImaginary = Math.Max(maximumImaginary, Math.Abs(value.Imaginary));
                reconstructed[(y * proxy.Size.Width) + x] = value.Real;
            }
        }
        if (maximumImaginary > 1e-8)
            throw new InvalidDataException($"IFFT 虚部残差 {maximumImaginary:E3} 超出 1E-8 数值门禁。 ");
        var plane = new ImageChannelPlane(proxy.Size, channel, reconstructed);
        var result = channelConverter.Apply(proxy, plane);
        return new BandReconstructionResult(result.Image, maskPreview, result.ClippedPixelCount, maximumImaginary, false);
    }
}

internal sealed class ProjectSpectrumUseCase(SpectrumProjector projector, RadialEnergyAnalyzer energyAnalyzer) : IProjectSpectrumUseCase
{
    public PixelImage CreateMagnitude(SpectrumAnalysisSession session, SpectrumMagnitudeMode mode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session); session.ThrowIfDisposed();
        return projector.CreateMagnitude(session.Spectrum, mode, cancellationToken);
    }

    public FrequencyPointInfo Inspect(SpectrumAnalysisSession session, int displayX, int displayY, FrequencyBandBoundaries boundaries)
    {
        ArgumentNullException.ThrowIfNull(session); session.ThrowIfDisposed();
        return projector.Inspect(session.Spectrum, displayX, displayY, boundaries);
    }

    public RadialEnergyReport AnalyzeEnergy(SpectrumAnalysisSession session, FrequencyBandBoundaries boundaries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session); session.ThrowIfDisposed();
        return energyAnalyzer.Analyze(session.Spectrum, boundaries, cancellationToken);
    }
}
