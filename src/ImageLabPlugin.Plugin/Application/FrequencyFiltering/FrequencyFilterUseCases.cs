using System.Diagnostics;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Application.FrequencyFiltering;

/// <summary>解码一次、建立有界代理及其全局 FFT；失败或取消时不返回半成品 Session。</summary>
internal sealed class PrepareFrequencyFilterSessionUseCase(IImageCodec codec, ImageAnalysisProxyProjector proxyProjector,
    ImageChannelConverter channelConverter, FrequencySpectrumBuilder spectrumBuilder, Domain.Frequency.SpectrumProjector spectrumProjector)
    : IPrepareFrequencyFilterSessionUseCase
{
    public async Task<FrequencyFilterSession> ExecuteAsync(FrequencyFilterSessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePath);
        if (!ImageAnalysisProxyProjector.SupportedMaximumEdges.Contains(request.AnalysisMaximumEdge))
            throw new ArgumentOutOfRangeException(nameof(request), "分析最大边必须是 512、1024 或 2048。");
        var source = await codec.DecodeAsync(request.SourcePath, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var proxy = proxyProjector.Create(source, request.AnalysisMaximumEdge, cancellationToken);
            var plane = channelConverter.Extract(proxy, request.Channel, cancellationToken);
            var spectrum = spectrumBuilder.Build(plane, cancellationToken);
            var magnitude = spectrumProjector.CreateMagnitude(spectrum, Domain.Frequency.SpectrumMagnitudeMode.Logarithmic, cancellationToken);
            return new FrequencyFilterSession(request.SourcePath, source, proxy, plane, spectrum, magnitude, request.AnalysisMaximumEdge);
        }, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>协调遮罩、IFFT、输出投影和诊断；不读取文件、不创建 Bitmap，也不修改缓存频谱。</summary>
internal sealed class ApplyFrequencyFilterUseCase(FrequencyFilterMaskFactory maskFactory, FrequencyFilterEngine engine,
    FrequencySignalProjector signalProjector, FrequencyDifferenceProjector differenceProjector,
    FrequencySideEffectAnalyzer sideEffectAnalyzer, FullReferenceQualityAnalyzer qualityAnalyzer)
    : IApplyFrequencyFilterUseCase
{
    public Task<FrequencyFilterResult> ExecuteAsync(FrequencyFilterSession session, FrequencyFilterRecipe recipe,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(recipe); session.ThrowIfDisposed();
        if (session.Channel != recipe.Channel) throw new InvalidOperationException("配方通道与 Session 缓存通道不一致，请重新准备 Session。");
        return Task.Run(() => Execute(session, session.AnalysisProxy, session.AnalysisPlane, session.Spectrum,
            recipe, isFullSize: false, allowSessionCache: true, cancellationToken), cancellationToken);
    }

    internal FrequencyFilterResult Execute(FrequencyFilterSession session, PixelImage image, ImageChannelPlane plane,
        Domain.Frequency.FrequencySpectrum spectrum, FrequencyFilterRecipe recipe, bool isFullSize,
        bool allowSessionCache, CancellationToken token)
    {
        var watch = Stopwatch.StartNew(); var mask = maskFactory.Create(spectrum, recipe, token); var maskElapsed = watch.Elapsed;
        watch.Restart(); var raw = allowSessionCache ? session.TryGetRaw(recipe.MathematicalFingerprint()) : null;
        var usedCache = raw is not null;
        raw ??= engine.Apply(spectrum, mask, token);
        if (allowSessionCache && !usedCache) session.StoreRaw(raw);
        var inverseElapsed = watch.Elapsed;
        watch.Restart(); var projection = signalProjector.Project(image, plane, raw, recipe, token); var projectionElapsed = watch.Elapsed;
        watch.Restart(); var difference = differenceProjector.Project(plane, projection.Plane, cancellationToken: token);
        var diagnostics = sideEffectAnalyzer.Analyze(plane, raw, projection.Plane, cancellationToken: token);
        var quality = qualityAnalyzer.Analyze(image, projection.Image, token); var diagnosticsElapsed = watch.Elapsed;
        return new FrequencyFilterResult(session.SessionFingerprint, recipe.Fingerprint(), recipe.MathematicalFingerprint(), isFullSize,
            mask, maskFactory.CreatePreview(mask), raw, projection, difference, diagnostics, quality,
            new FrequencyFilterStageTimings(maskElapsed, inverseElapsed, projectionElapsed, diagnosticsElapsed, usedCache));
    }
}

/// <summary>显式执行空间有限核近似；采用相同 padded 平面、Wrap 边界和 raw double。</summary>
internal sealed class CompareFrequencySpatialUseCase(FrequencyFilterMaskFactory maskFactory,
    FrequencySpectrumBuilder spectrumBuilder, FrequencySpatialComparator comparator) : ICompareFrequencySpatialUseCase
{
    public Task<FrequencySpatialComparison> ExecuteAsync(FrequencyFilterSession session, FrequencyFilterRecipe recipe,
        int kernelSize, CancellationToken cancellationToken)
    {
        session.ThrowIfDisposed();
        if (session.Channel != recipe.Channel) throw new InvalidOperationException("配方通道与 Session 不一致。");
        return Task.Run(() =>
        {
            var mask = maskFactory.Create(session.Spectrum, recipe, cancellationToken);
            var padded = spectrumBuilder.CreatePaddedSpatialPlane(session.AnalysisPlane, session.Spectrum);
            return comparator.Compare(padded, session.Spectrum, mask, recipe.Kind, kernelSize, cancellationToken);
        }, cancellationToken);
    }
}

/// <summary>只在源图可落入共享 2048² FFT 预算时执行原尺寸结果，不通过缩放伪造原尺寸。</summary>
internal sealed class RenderFullFrequencyFilterUseCase(ImageChannelConverter channelConverter,
    FrequencySpectrumBuilder spectrumBuilder, ApplyFrequencyFilterUseCase apply) : IRenderFullFrequencyFilterUseCase
{
    public Task<FrequencyFilterResult> ExecuteAsync(FrequencyFilterSession session, FrequencyFilterRecipe recipe,
        CancellationToken cancellationToken)
    {
        session.ThrowIfDisposed();
        if (!session.CanRenderFullSize)
            throw new InvalidOperationException($"原图 {session.SourceImage.Size.Width}×{session.SourceImage.Size.Height} 补零后超出 2048×2048 FFT 预算，只能导出明确标识的代理结果。");
        return Task.Run(() =>
        {
            var plane = channelConverter.Extract(session.SourceImage, recipe.Channel, cancellationToken);
            var spectrum = spectrumBuilder.Build(plane, cancellationToken);
            return apply.Execute(session, session.SourceImage, plane, spectrum, recipe, isFullSize: true,
                allowSessionCache: false, cancellationToken);
        }, cancellationToken);
    }
}

/// <summary>在编码和原子写入前同时验证 Session 与完整配方指纹，拒绝 stale 结果。</summary>
internal sealed class ExportFrequencyFilterImageUseCase(IImageCodec codec, IAtomicFileWriter writer)
    : IExportFrequencyFilterImageUseCase
{
    public async Task<FrequencyFilterExportResult> ExecuteAsync(FrequencyFilterExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        if (!StringComparer.Ordinal.Equals(request.Result.SessionFingerprint, request.ExpectedSessionFingerprint) ||
            !StringComparer.Ordinal.Equals(request.Result.RecipeFingerprint, request.ExpectedRecipeFingerprint))
            throw new InvalidOperationException("滤波结果已过期，请用当前图片和参数重新执行后再导出。");
        var bytes = await codec.EncodeAsync(request.Result.Projection.Image, ImageOutputFormat.Png, 100, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(request.OutputPath, bytes, cancellationToken).ConfigureAwait(false);
        return new FrequencyFilterExportResult(request.OutputPath, request.Result.Projection.Image.Size,
            request.Result.IsFullSize, request.Result.RecipeFingerprint);
    }
}
