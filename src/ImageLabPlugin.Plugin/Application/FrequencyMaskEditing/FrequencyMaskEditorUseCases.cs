using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.FrequencyMaskEditing;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Application.FrequencyMaskEditing;

/// <summary>解码一次并建立有界代理、选定通道和只读全局 FFT。</summary>
internal sealed class PrepareFrequencyMaskEditorSessionUseCase(IImageCodec codec,
    ImageAnalysisProxyProjector proxyProjector, ImageChannelConverter channelConverter,
    FrequencySpectrumBuilder spectrumBuilder, SpectrumProjector spectrumProjector)
    : IPrepareFrequencyMaskEditorSessionUseCase
{
    public async Task<FrequencyMaskEditorSession> ExecuteAsync(FrequencyMaskSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePath);
        if (!ImageAnalysisProxyProjector.SupportedMaximumEdges.Contains(request.AnalysisMaximumEdge))
            throw new ArgumentOutOfRangeException(nameof(request), "分析最大边必须是 512、1024 或 2048。");
        var source = await codec.DecodeAsync(request.SourcePath, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var proxy = proxyProjector.Create(source, request.AnalysisMaximumEdge, cancellationToken);
            var plane = channelConverter.Extract(proxy, request.Channel, cancellationToken);
            var spectrum = spectrumBuilder.Build(plane, cancellationToken);
            var magnitude = spectrumProjector.CreateMagnitude(spectrum, SpectrumMagnitudeMode.Logarithmic, cancellationToken);
            return new FrequencyMaskEditorSession(request.SourcePath, source, proxy, plane, spectrum, magnitude,
                request.AnalysisMaximumEdge);
        }, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>协调配方重放、共享 IFFT、通道回写和解释性诊断。</summary>
internal sealed class RenderFrequencyMaskUseCase(FrequencyMaskRasterizer rasterizer, FrequencyMaskApplier applier,
    ImageChannelConverter channelConverter, FrequencyMaskDiagnostics maskDiagnostics,
    ChannelDifferenceProjector differenceProjector, FullReferenceQualityAnalyzer qualityAnalyzer)
    : IRenderFrequencyMaskUseCase
{
    public Task<FrequencyMaskRenderResult> ExecuteAsync(FrequencyMaskEditorSession session, FrequencyMaskRecipe recipe,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(recipe);
        session.ThrowIfDisposed();
        return Task.Run(() => ExecuteCore(session, session.AnalysisProxy, session.AnalysisPlane, session.Spectrum,
            recipe, false, cancellationToken), cancellationToken);
    }

    internal FrequencyMaskRenderResult ExecuteCore(FrequencyMaskEditorSession session, PixelImage source,
        ImageChannelPlane sourcePlane, FrequencySpectrum spectrum, FrequencyMaskRecipe recipe, bool isFullSize,
        CancellationToken cancellationToken)
    {
        session.ThrowIfDisposed();
        if (sourcePlane.Channel != session.Channel) throw new InvalidOperationException("重建通道与 Session 不一致。");

        var watch = Stopwatch.StartNew();
        var editMask = rasterizer.Rasterize(recipe, spectrum.PaddedWidth, spectrum.PaddedHeight, cancellationToken);
        var effectiveMask = rasterizer.CreateEffective(editMask, recipe.Strength, cancellationToken);
        var rasterElapsed = watch.Elapsed;

        watch.Restart();
        var raw = applier.Apply(spectrum, effectiveMask, cancellationToken);
        var inverseElapsed = watch.Elapsed;

        watch.Restart();
        var rawValues = raw.ValueSpan;
        var reconstructedPlane = new ImageChannelPlane(source.Size, sourcePlane.Channel, rawValues);
        var reconstruction = channelConverter.Apply(source, reconstructedPlane, MidpointRounding.AwayFromZero);
        var maskPreview = CreateMaskPreview(effectiveMask, cancellationToken);
        var projectionElapsed = watch.Elapsed;

        watch.Restart();
        var difference = differenceProjector.Project(sourcePlane, reconstructedPlane, cancellationToken: cancellationToken);
        var statistics = maskDiagnostics.Analyze(spectrum, effectiveMask, cancellationToken);
        var quality = qualityAnalyzer.Analyze(source, reconstruction.Image, cancellationToken);
        var rawStatistics = AnalyzeRaw(rawValues, reconstruction.ClippedPixelCount, cancellationToken);
        var diagnosticsElapsed = watch.Elapsed;

        var recipeFingerprint = recipe.Fingerprint();
        var resultFingerprint = Fingerprint(session.SessionFingerprint, recipeFingerprint, isFullSize, source.Size);
        return new FrequencyMaskRenderResult(session.SessionFingerprint, recipeFingerprint, resultFingerprint,
            isFullSize, editMask, effectiveMask, maskPreview, raw, reconstruction.Image, difference, statistics,
            rawStatistics, quality, new FrequencyMaskRenderTimings(rasterElapsed, inverseElapsed,
                projectionElapsed, diagnosticsElapsed));
    }

    private static PixelImage CreateMaskPreview(FrequencyGainMask mask, CancellationToken cancellationToken)
    {
        var rgba = new byte[checked(mask.Width * mask.Height * 4)];
        for (var displayY = 0; displayY < mask.Height; displayY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var displayX = 0; displayX < mask.Width; displayX++)
            {
                var point = FrequencyCoordinates.FromDisplay(displayX, displayY, mask.Width, mask.Height);
                var level = (byte)Math.Clamp((int)Math.Round(mask[point.InternalX, point.InternalY] * 255d), 0, 255);
                var offset = ((displayY * mask.Width) + displayX) * 4;
                rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = level;
                rgba[offset + 3] = 255;
            }
        }
        return new PixelImage(new ImageSize(mask.Width, mask.Height), rgba);
    }

    private static FrequencyMaskRawStatistics AnalyzeRaw(ReadOnlySpan<double> values, int clippedPixels,
        CancellationToken cancellationToken)
    {
        double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
        long low = 0, high = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            minimum = Math.Min(minimum, values[i]);
            maximum = Math.Max(maximum, values[i]);
            if (values[i] < 0d) low++;
            else if (values[i] > 255d) high++;
        }
        return new FrequencyMaskRawStatistics(minimum, maximum, low, high, clippedPixels);
    }

    private static string Fingerprint(string session, string recipe, bool fullSize, ImageSize size)
    {
        var canonical = $"frequency-mask-result-v1|{session}|{recipe}|{fullSize}|{size.Width}|{size.Height}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16].ToLowerInvariant();
    }
}

/// <summary>仅在共享 2048² 预算内显式建立原尺寸 FFT，不用缩放回填冒充原尺寸。</summary>
internal sealed class RenderFullFrequencyMaskUseCase(ImageChannelConverter channelConverter,
    FrequencySpectrumBuilder spectrumBuilder, RenderFrequencyMaskUseCase render) : IRenderFullFrequencyMaskUseCase
{
    public Task<FrequencyMaskRenderResult> ExecuteAsync(FrequencyMaskEditorSession session, FrequencyMaskRecipe recipe,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(recipe);
        session.ThrowIfDisposed();
        if (!session.CanRenderFullSize) throw new InvalidOperationException("原图补零后超出共享 2048² FFT 预算，只能导出代理结果。");
        return Task.Run(() =>
        {
            var plane = channelConverter.Extract(session.SourceImage, session.Channel, cancellationToken);
            var spectrum = spectrumBuilder.Build(plane, cancellationToken);
            return render.ExecuteCore(session, session.SourceImage, plane, spectrum, recipe, true, cancellationToken);
        }, cancellationToken);
    }
}

internal sealed class ExportFrequencyMaskImageUseCase(IImageCodec codec, IAtomicFileWriter writer)
    : IExportFrequencyMaskImageUseCase
{
    public async Task<FrequencyMaskImageExportResult> ExecuteAsync(FrequencyMaskImageExportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!StringComparer.Ordinal.Equals(request.Result.SessionFingerprint, request.ExpectedSessionFingerprint) ||
            !StringComparer.Ordinal.Equals(request.Result.RecipeFingerprint, request.ExpectedRecipeFingerprint))
            throw new InvalidOperationException("结果已过期，Session 或配方指纹与当前状态不一致。");
        var image = request.Artifact == FrequencyMaskExportArtifact.Reconstruction
            ? request.Result.Reconstruction
            : request.Result.MaskPreview;
        var bytes = await codec.EncodeAsync(image, ImageOutputFormat.Png, 100, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(request.OutputPath, bytes, cancellationToken).ConfigureAwait(false);
        return new FrequencyMaskImageExportResult(request.OutputPath, image.Size, request.Artifact, request.Result.IsFullSize);
    }
}

internal sealed class InspectFrequencyMaskPointUseCase : IInspectFrequencyMaskPointUseCase
{
    public FrequencyMaskPointInspection Execute(FrequencyMaskEditorSession session, FrequencyMaskRenderResult result,
        double normalizedX, double normalizedY)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);
        session.ThrowIfDisposed();
        if (!double.IsFinite(normalizedX) || normalizedX is < 0d or > 1d ||
            !double.IsFinite(normalizedY) || normalizedY is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(normalizedX));
        if (!StringComparer.Ordinal.Equals(session.SessionFingerprint, result.SessionFingerprint) || result.IsFullSize)
            throw new InvalidOperationException("探针只检查当前 Session 的代理结果。");
        var width = session.Spectrum.PaddedWidth;
        var height = session.Spectrum.PaddedHeight;
        var displayX = Math.Clamp((int)Math.Round(normalizedX * (width - 1)), 0, width - 1);
        var displayY = Math.Clamp((int)Math.Round(normalizedY * (height - 1)), 0, height - 1);
        var point = FrequencyCoordinates.FromDisplay(displayX, displayY, width, height);
        var conjugate = FrequencyCoordinates.ConjugateIndex(point.InternalX, point.InternalY, width, height);
        var conjugateDisplay = FrequencyCoordinates.FromInternal(conjugate.X, conjugate.Y, width, height);
        return new FrequencyMaskPointInspection(displayX, displayY, point.InternalX, point.InternalY,
            conjugateDisplay.DisplayX, conjugateDisplay.DisplayY, conjugate.X, conjugate.Y, point.Fx, point.Fy, point.Radius,
            Complex.Abs(session.Spectrum[point.InternalX, point.InternalY]),
            result.EditMask[point.InternalX, point.InternalY], result.EffectiveMask[point.InternalX, point.InternalY]);
    }
}

internal sealed class ImportFrequencyMaskRecipeUseCase(ITextFileReader reader, IFrequencyMaskRecipeSerializer serializer)
    : IImportFrequencyMaskRecipeUseCase
{
    public const int MaximumJsonBytes = 1024 * 1024;
    public async Task<FrequencyMaskRecipe> ExecuteAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await reader.ReadAsync(path, MaximumJsonBytes, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return serializer.Deserialize(bytes);
    }
}

internal sealed class ExportFrequencyMaskRecipeUseCase(IFrequencyMaskRecipeSerializer serializer, IAtomicFileWriter writer)
    : IExportFrequencyMaskRecipeUseCase
{
    public async Task ExecuteAsync(FrequencyMaskRecipe recipe, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var bytes = serializer.Serialize(recipe);
        if (bytes.Length > ImportFrequencyMaskRecipeUseCase.MaximumJsonBytes)
            throw new InvalidDataException("规范化配方 JSON 超过 1 MiB 上限。");
        await writer.WriteAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }
}
