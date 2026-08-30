using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.PeriodicNoiseRemoval;

namespace ImageLabPlugin.Application.PeriodicNoiseRemoval;

/// <summary>解码一次并建立有界分析代理及其只读 FFT Session。</summary>
internal sealed class PreparePeriodicNoiseSessionUseCase(IImageCodec codec,
    ImageAnalysisProxyProjector proxyProjector, ImageChannelConverter channelConverter,
    FrequencySpectrumBuilder spectrumBuilder, SpectrumProjector spectrumProjector)
    : IPreparePeriodicNoiseSessionUseCase
{
    public async Task<PeriodicNoiseSession> ExecuteAsync(PeriodicNoiseSessionRequest request,
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
            var preview = spectrumProjector.CreateMagnitude(spectrum, SpectrumMagnitudeMode.Logarithmic,
                cancellationToken);
            return new PeriodicNoiseSession(request.SourcePath, source, proxy, plane, spectrum, preview,
                request.AnalysisMaximumEdge);
        }, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>只编排候选检测，不修改 Session、草案或已采用配方。</summary>
internal sealed class DetectPeriodicNoiseCandidatesUseCase(PeriodicPeakDetector detector)
    : IDetectPeriodicNoiseCandidatesUseCase
{
    public Task<PeriodicNoiseDetectionResult> ExecuteAsync(PeriodicNoiseSession session,
        PeriodicNoiseDetectionSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.ThrowIfDisposed();
        return Task.Run(() => detector.Detect(session.Spectrum, settings, cancellationToken), cancellationToken);
    }
}

/// <summary>把频谱控件提交的归一化显示坐标映射为 canonical cycles/pixel 频率。</summary>
/// <remarks>控件和 Document 都不接触 FFT 自然索引；边缘坐标先限制到最后一个有效 display bin，再复用统一坐标事实源。</remarks>
internal sealed class MapPeriodicSpectrumSelectionUseCase : IMapPeriodicSpectrumSelectionUseCase
{
    public PeriodicFrequency Execute(PeriodicNoiseSession session, double normalizedX, double normalizedY)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.ThrowIfDisposed();
        if (!double.IsFinite(normalizedX) || normalizedX is < 0d or > 1d ||
            !double.IsFinite(normalizedY) || normalizedY is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(normalizedX));
        var width = session.Spectrum.PaddedWidth;
        var height = session.Spectrum.PaddedHeight;
        var displayX = Math.Clamp((int)Math.Floor(normalizedX * width), 0, width - 1);
        var displayY = Math.Clamp((int)Math.Floor(normalizedY * height), 0, height - 1);
        var point = FrequencyCoordinates.FromDisplay(displayX, displayY, width, height);
        return PeriodicFrequency.Canonical(PeriodicFrequency.FromInternal(point.InternalX, point.InternalY,
            width, height));
    }
}

/// <summary>协调遮罩、共享 IFFT、精确处理后频谱、通道回写、差异和损失诊断。</summary>
/// <remarks>
/// 本用例不读取文件、不创建 Bitmap，也不改变原频谱。<paramref name="isDraft"/> 被写入结果并由导出用例硬校验，
/// 因而自动建议或参数调整得到的未确认草案即使已有预览，也不能绕过人工采用边界。
/// </remarks>
internal sealed class RenderPeriodicNoisePreviewUseCase(NotchMaskFactory maskFactory,
    FrequencyMaskApplier maskApplier, FrequencyGainSpectrumProjector filteredSpectrumProjector,
    ImageChannelConverter channelConverter, FrequencyDifferenceProjector differenceProjector,
    FullReferenceQualityAnalyzer qualityAnalyzer, PeriodicNoiseLossAnalyzer lossAnalyzer)
    : IRenderPeriodicNoisePreviewUseCase
{
    public Task<PeriodicNoiseRenderResult> ExecuteAsync(PeriodicNoiseSession session, PeriodicNoiseRecipe recipe,
        IReadOnlyList<PeriodicFrequencyCandidate> selectedCandidates, bool isDraft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(selectedCandidates);
        session.ThrowIfDisposed();
        if (session.Channel != recipe.Channel) throw new InvalidOperationException("配方通道与 Session 不一致。");
        return Task.Run(() => Execute(session, session.AnalysisProxy, session.AnalysisPlane, session.Spectrum,
            recipe, selectedCandidates, isDraft, isFullSize: false, cancellationToken), cancellationToken);
    }

    internal PeriodicNoiseRenderResult Execute(PeriodicNoiseSession session, PixelImage image,
        ImageChannelPlane plane, FrequencySpectrum spectrum, PeriodicNoiseRecipe recipe,
        IReadOnlyList<PeriodicFrequencyCandidate> selectedCandidates, bool isDraft, bool isFullSize,
        CancellationToken cancellationToken)
    {
        var mask = maskFactory.Create(spectrum, recipe, cancellationToken);
        var raw = maskApplier.Apply(spectrum, mask.GainMask, cancellationToken);
        var resultPlane = new ImageChannelPlane(plane.Size, recipe.Channel, raw.Values.Span);
        var reconstruction = channelConverter.Apply(image, resultPlane, MidpointRounding.AwayFromZero);
        var filteredSpectrum = filteredSpectrumProjector.Project(spectrum, mask.GainMask, cancellationToken);
        var difference = differenceProjector.Project(plane, resultPlane, cancellationToken: cancellationToken);
        var quality = qualityAnalyzer.Analyze(image, reconstruction.Image, cancellationToken);
        var diagnostics = lossAnalyzer.Analyze(spectrum, mask, raw, plane, resultPlane,
            reconstruction.ClippedPixelCount, selectedCandidates, quality, cancellationToken);
        return new PeriodicNoiseRenderResult(session.SessionFingerprint, recipe.Fingerprint(),
            recipe.MathematicalFingerprint(), isDraft, isFullSize, mask, raw, reconstruction.Image,
            filteredSpectrum.Preview, difference, diagnostics);
    }
}

/// <summary>只在源图补零后落入共享 2048² 预算时执行已采用配方的原尺寸结果。</summary>
internal sealed class RenderFullPeriodicNoiseResultUseCase(ImageChannelConverter channelConverter,
    FrequencySpectrumBuilder spectrumBuilder, RenderPeriodicNoisePreviewUseCase renderer)
    : IRenderFullPeriodicNoiseResultUseCase
{
    public Task<PeriodicNoiseRenderResult> ExecuteAsync(PeriodicNoiseSession session, PeriodicNoiseRecipe recipe,
        IReadOnlyList<PeriodicFrequencyCandidate> selectedCandidates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.ThrowIfDisposed();
        if (!session.CanRenderFullSize)
            throw new InvalidOperationException("原图补零后超出 2048×2048 FFT 预算，只能保留明确标识的代理结果。");
        return Task.Run(() =>
        {
            var plane = channelConverter.Extract(session.SourceImage, recipe.Channel, cancellationToken);
            var spectrum = spectrumBuilder.Build(plane, cancellationToken);
            return renderer.Execute(session, session.SourceImage, plane, spectrum, recipe, selectedCandidates,
                isDraft: false, isFullSize: true, cancellationToken);
        }, cancellationToken);
    }
}

/// <summary>导入不超过 1 MiB 的严格配方 JSON，失败时不产生半成品配方。</summary>
internal sealed class ImportPeriodicNoiseRecipeUseCase(ITextFileReader reader, IPeriodicNoiseRecipeSerializer serializer)
    : IImportPeriodicNoiseRecipeUseCase
{
    internal const int MaximumJsonBytes = 1024 * 1024;
    public async Task<PeriodicNoiseRecipe> ExecuteAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = await reader.ReadAsync(path, MaximumJsonBytes, cancellationToken).ConfigureAwait(false);
        return serializer.Deserialize(bytes);
    }
}

/// <summary>把规范配方 JSON 通过原子写入端口发布到新文件。</summary>
internal sealed class ExportPeriodicNoiseRecipeUseCase(IPeriodicNoiseRecipeSerializer serializer,
    IAtomicFileWriter writer) : IExportPeriodicNoiseRecipeUseCase
{
    public Task ExecuteAsync(PeriodicNoiseRecipe recipe, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return writer.WriteAsync(path, serializer.Serialize(recipe), cancellationToken);
    }
}

/// <summary>把只读候选摘要通过独立 schema 原子导出，不与可重放配方混合。</summary>
internal sealed class ExportPeriodicNoiseCandidateSummaryUseCase(IPeriodicNoiseCandidateSummarySerializer serializer,
    IAtomicFileWriter writer) : IExportPeriodicNoiseCandidateSummaryUseCase
{
    public Task ExecuteAsync(PeriodicNoiseSession session, PeriodicNoiseDetectionResult detection, string path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(detection);
        session.ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return writer.WriteAsync(path, serializer.Serialize(session, detection), cancellationToken);
    }
}

/// <summary>只导出当前 Session、已采用配方和结果指纹一致的 PNG。</summary>
internal sealed class ExportPeriodicNoiseArtifactUseCase(IImageCodec codec, IAtomicFileWriter writer)
    : IExportPeriodicNoiseArtifactUseCase
{
    public async Task<PeriodicNoiseArtifactExportResult> ExecuteAsync(PeriodicNoiseArtifactExportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        if (request.Result.IsDraft) throw new InvalidOperationException("未确认草案结果禁止导出，请先采用草案并重新执行。");
        if (!StringComparer.Ordinal.Equals(request.Result.SessionFingerprint, request.ExpectedSessionFingerprint) ||
            !StringComparer.Ordinal.Equals(request.Result.RecipeFingerprint, request.ExpectedRecipeFingerprint))
            throw new InvalidOperationException("结果与当前 Session 或已采用配方指纹不一致，请重新执行。");
        var image = request.Artifact == PeriodicNoiseExportArtifact.Reconstruction
            ? request.Result.Reconstruction : request.Result.Mask.Preview;
        var bytes = await codec.EncodeAsync(image, ImageOutputFormat.Png, 100, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(request.OutputPath, bytes, cancellationToken).ConfigureAwait(false);
        return new PeriodicNoiseArtifactExportResult(request.OutputPath, image.Size, request.Artifact,
            request.Result.IsFullSize);
    }
}
