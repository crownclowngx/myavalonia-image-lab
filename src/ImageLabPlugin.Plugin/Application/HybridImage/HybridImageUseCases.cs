using System.Diagnostics;
using System.Security.Cryptography;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.HybridImage;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Application.HybridImage;

/// <summary>显式各解码一次 A/B，并建立完整亮度与最大边 1024 的交互代理。</summary>
internal sealed class PrepareHybridInputsUseCase(
    IImageCodec codec,
    ImageAreaResampler resampler,
    HybridLumaProjector lumaProjector) : IPrepareHybridInputsUseCase
{
    public async Task<HybridImageSession> ExecuteAsync(
        PrepareHybridInputsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PathA);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PathB);
        if (request.ProxyMaximumEdge is < 32 or > HybridImageProtocol.ProxyMaximumEdge)
            throw new ArgumentOutOfRangeException(nameof(request), "代理最大边必须位于 [32,1024]。");
        var sourceA = await codec.DecodeAsync(request.PathA, cancellationToken).ConfigureAwait(false);
        var sourceB = await codec.DecodeAsync(request.PathB, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var lumaA = lumaProjector.Project(sourceA, cancellationToken);
            var lumaB = lumaProjector.Project(sourceB, cancellationToken);
            var proxyA = resampler.ResizeToMaximumEdge(sourceA, request.ProxyMaximumEdge, cancellationToken);
            var proxyB = resampler.ResizeToMaximumEdge(sourceB, request.ProxyMaximumEdge, cancellationToken);
            var proxyLumaA = lumaProjector.Project(proxyA, cancellationToken);
            var proxyLumaB = lumaProjector.Project(proxyB, cancellationToken);
            return new HybridImageSession(request.PathA, request.PathB, sourceA, sourceB, proxyA, proxyB,
                lumaA, lumaB, proxyLumaA, proxyLumaB, Fingerprint(sourceA), Fingerprint(sourceB));
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string Fingerprint(PixelImage image)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(image.Size.Width));
        hash.AppendData(BitConverter.GetBytes(image.Size.Height));
        hash.AppendData(image.Rgba.Span);
        return Convert.ToHexString(hash.GetHashAndReset())[..24].ToLowerInvariant();
    }
}

/// <summary>只协调控制点求解、有效掩码与默认裁切，不执行滤波。</summary>
internal sealed class SolveHybridAlignmentUseCase(
    SimilarityTransformSolver solver,
    AlignedImageSampler sampler,
    HybridCropValidator cropValidator) : ISolveHybridAlignmentUseCase
{
    public Task<HybridAlignmentState> ExecuteAsync(HybridImageSession session,
        SolveHybridAlignmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        session.ThrowIfDisposed();
        return Task.Run(() =>
        {
            var solution = solver.Solve(request.Points, session.ProxyA.Size, session.ProxyB.Size);
            var warp = sampler.Warp(session.ProxyLumaB, session.ProxyA.Size, solution.Transform, cancellationToken);
            var maximum = cropValidator.FindMaximumValidRectangle(session.ProxyA.Size, warp.ValidMask.Span, cancellationToken);
            var coverage = cropValidator.ValidateUsable(maximum, session.ProxyA.Size);
            var pointRecipe = new HybridImageRecipe(request.Points,
                HybridNormalizedCrop.FromPixels(maximum, session.ProxyA.Size));
            var state = new HybridAlignmentState(solution, maximum, coverage, pointRecipe.Fingerprint());
            session.CommitAlignment(state);
            return state;
        }, cancellationToken);
    }
}

/// <summary>共享预览与完整尺寸的固定渲染顺序；调用方只选择哪一组原始平面进入管线。</summary>
/// <remarks>
/// 本类不是通用流水线框架，只封装本产品两条执行路径完全相同的数学顺序。候选结果在全部分量、四尺度、
/// 重影和共享量程频谱成功后才返回，Session 的原子提交仍由调用方按 generation 完成。
/// </remarks>
internal sealed class HybridRenderCoordinator(
    SimilarityTransformSolver solver,
    AlignedImageSampler sampler,
    HybridCropValidator cropValidator,
    HybridImageComposer composer,
    HybridScaleProjector scaleProjector,
    HybridImageDiagnostics diagnostics,
    FrequencySpectrumBuilder spectrumBuilder,
    SpectrumProjector spectrumProjector,
    HybridResourceEstimator resourceEstimator)
{
    public HybridRenderResult Render(HybridImageSession session, HybridImageRecipe recipe,
        HybridLumaPlane sourceA, HybridLumaPlane sourceB, bool fullSize, long generation,
        CancellationToken cancellationToken)
    {
        session.ThrowIfDisposed();
        resourceEstimator.EnsureWithinBudget(sourceA.Size, recipe.LowSigmaPixels, recipe.HighSigmaPixels);
        var watch = Stopwatch.StartNew();
        var alignment = solver.Solve(recipe.Points, sourceA.Size, sourceB.Size);
        var warped = sampler.Warp(sourceB, sourceA.Size, alignment.Transform, cancellationToken);
        var maximumCrop = cropValidator.FindMaximumValidRectangle(sourceA.Size, warped.ValidMask.Span, cancellationToken);
        var requestedCrop = recipe.Crop.ToPixels(sourceA.Size);
        cropValidator.ValidateUserCrop(requestedCrop, maximumCrop);
        var coverage = cropValidator.ValidateUsable(requestedCrop, sourceA.Size);
        var croppedA = cropValidator.Crop(sourceA, requestedCrop, cancellationToken);
        var croppedB = cropValidator.Crop(warped.AlignedB, requestedCrop, cancellationToken);
        var composition = composer.Compose(croppedA, croppedB, recipe.LowSigmaPixels,
            recipe.HighSigmaPixels, recipe.LowGain, recipe.HighGain, cancellationToken);
        var scales = scaleProjector.CreateAll(composition.Raw, cancellationToken);
        var edgeOverlay = diagnostics.CreateRedCyanEdgeOverlay(croppedA, croppedB, cancellationToken);
        var alignmentDiagnostics = diagnostics.Describe(alignment, coverage);
        var spectra = CreateSpectra(croppedA, croppedB, composition, cancellationToken);
        var lowFifty = GaussianPlaneFilter.FiftyPercentCutoff(recipe.LowSigmaPixels);
        var highFifty = GaussianPlaneFilter.FiftyPercentCutoff(recipe.HighSigmaPixels);
        var cutoff = new HybridCutoffDiagnostics(
            lowFifty * Math.Min(composition.Raw.Size.Width, composition.Raw.Size.Height),
            highFifty * Math.Min(composition.Raw.Size.Width, composition.Raw.Size.Height),
            lowFifty * Math.Min(spectra.PaddedWidth, spectra.PaddedHeight),
            highFifty * Math.Min(spectra.PaddedWidth, spectra.PaddedHeight),
            "圆环是连续 Gaussian 的理论 50% 幅度截止；3σ 离散截断核会略有差异。");
        return new HybridRenderResult(session.SessionFingerprint, recipe.Fingerprint(), generation, fullSize,
            alignment, requestedCrop, coverage, composition, scales, edgeOverlay,
            alignmentDiagnostics, spectra, cutoff, watch.Elapsed);
    }

    private HybridSpectrumBundle CreateSpectra(HybridLumaPlane sourceA, HybridLumaPlane sourceB,
        HybridCompositionResult composition, CancellationToken cancellationToken)
    {
        // 完整图可能大于共享 FFT 上限；频谱只消费最大边 1024 的 double 面积代理，不长期缓存 Complex[]。
        var bounded = new Dictionary<HybridSpectrumKind, HybridLumaPlane>
        {
            [HybridSpectrumKind.SourceA] = scaleProjector.ResizeToMaximumEdge(sourceA, 1024, cancellationToken),
            [HybridSpectrumKind.LowA] = scaleProjector.ResizeToMaximumEdge(composition.LowA, 1024, cancellationToken),
            [HybridSpectrumKind.SourceB] = scaleProjector.ResizeToMaximumEdge(sourceB, 1024, cancellationToken),
            [HybridSpectrumKind.HighB] = scaleProjector.ResizeToMaximumEdge(composition.HighB, 1024, cancellationToken),
            [HybridSpectrumKind.Raw] = scaleProjector.ResizeToMaximumEdge(composition.Raw, 1024, cancellationToken)
        };
        // 五张频谱只在此处短暂同时存在以确定共同量程；返回对象不保存任何 Complex[]。
        var spectra = bounded.Values.Select(ToChannelPlane)
            .Select(plane => spectrumBuilder.Build(plane, cancellationToken)).ToArray();
        var scale = spectrumProjector.CreateSharedScale(spectra, SpectrumMagnitudeMode.Logarithmic);
        return new HybridSpectrumBundle(scale, bounded, spectrumBuilder, spectrumProjector,
            spectra[0].PaddedWidth, spectra[0].PaddedHeight);
    }

    private static ImageChannelPlane ToChannelPlane(HybridLumaPlane plane)
    {
        var values = new double[plane.Values.Length];
        for (var i = 0; i < values.Length; i++) values[i] = plane.Values.Span[i] * 255d;
        return new ImageChannelPlane(plane.Size, ImageChannel.Luma, values);
    }
}

internal sealed class RenderHybridPreviewUseCase(HybridRenderCoordinator coordinator) : IRenderHybridPreviewUseCase
{
    public Task<HybridRenderResult> ExecuteAsync(HybridImageSession session, HybridImageRecipe recipe,
        long generation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(recipe);
        return Task.Run(() => coordinator.Render(session, recipe, session.ProxyLumaA, session.ProxyLumaB,
            false, generation, cancellationToken), cancellationToken);
    }
}

internal sealed class RenderHybridFullSizeUseCase(HybridRenderCoordinator coordinator) : IRenderHybridFullSizeUseCase
{
    public Task<HybridRenderResult> ExecuteAsync(HybridImageSession session, HybridImageRecipe recipe,
        long generation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(recipe);
        // 完整尺寸直接消费首次解码的原图亮度，不放大代理，也不再次触碰路径。
        return Task.Run(() => coordinator.Render(session, recipe, session.SourceLumaA, session.SourceLumaB,
            true, generation, cancellationToken), cancellationToken);
    }
}
