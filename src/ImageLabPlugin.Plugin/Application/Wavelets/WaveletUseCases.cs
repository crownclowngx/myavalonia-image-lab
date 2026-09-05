using System.Diagnostics;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Wavelets;

namespace ImageLabPlugin.Application.Wavelets;

/// <summary>只解码一次并建立代理；不会自动启动分解或恢复上次完整结果。</summary>
internal sealed class PrepareWaveletSessionUseCase(IImageCodec codec, ImageAnalysisProxyProjector projector)
    : IPrepareWaveletSessionUseCase
{
    public async Task<WaveletSession> ExecuteAsync(string sourcePath, string? referencePath, int analysisMaximumEdge,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var source = await codec.DecodeAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        PixelImage? reference = null;
        if (!string.IsNullOrWhiteSpace(referencePath))
        {
            reference = await codec.DecodeAsync(referencePath, cancellationToken).ConfigureAwait(false);
            if (reference.Size != source.Size) throw new InvalidDataException("干净参考图必须与源图尺寸完全一致。");
        }
        var proxy = await Task.Run(() => projector.Create(source, analysisMaximumEdge, cancellationToken), cancellationToken).ConfigureAwait(false);
        var referenceProxy = reference is null ? null
            : await Task.Run(() => projector.Create(reference, analysisMaximumEdge, cancellationToken), cancellationToken).ConfigureAwait(false);
        return new(sourcePath, source, proxy, analysisMaximumEdge, referencePath, reference, referenceProxy);
    }
}

/// <summary>协调通道抽取、策略分解、噪声估计和有界投影；不写文件或创建 Avalonia Bitmap。</summary>
internal sealed class DecomposeWaveletUseCase(
    ImageChannelConverter channelConverter,
    WaveletTransformCatalog catalog,
    WaveletNoiseEstimator noiseEstimator,
    WaveletSubbandProjector projector) : IDecomposeWaveletUseCase
{
    public Task<WaveletAnalysisResult> ExecuteAsync(WaveletSession session, WaveletDenoiseRecipe recipe, bool fullSize,
        int projectionLevel, WaveletSubband projectionSubband, WaveletProjectionMode projectionMode, CancellationToken cancellationToken)
    {
        session.ThrowIfDisposed();
        return Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var image = fullSize ? session.SourceImage : session.AnalysisProxy;
            var plane = channelConverter.Extract(image, recipe.Channel, cancellationToken);
            var pyramid = catalog.Resolve(recipe.Transform).Forward(plane, recipe.Levels, cancellationToken);
            var noise = noiseEstimator.Estimate(pyramid);
            var projection = projector.Project(pyramid, Math.Clamp(projectionLevel, 1, recipe.Levels), projectionSubband,
                projectionMode, cancellationToken);
            return new WaveletAnalysisResult(plane, pyramid, noise, projection, recipe.Fingerprint(), fullSize, stopwatch.Elapsed);
        }, cancellationToken);
    }
}

internal sealed class DenoiseWaveletUseCase(
    WaveletThresholdProcessor processor,
    WaveletImageReconstructor reconstructor,
    FullReferenceQualityAnalyzer qualityAnalyzer) : IDenoiseWaveletUseCase
{
    public Task<WaveletDenoiseResult> ExecuteAsync(WaveletSession session, WaveletAnalysisResult analysis,
        WaveletDenoiseRecipe recipe, CancellationToken cancellationToken)
    {
        session.ThrowIfDisposed();
        if (!StringComparer.Ordinal.Equals(analysis.RecipeFingerprint, recipe.Fingerprint()))
            throw new InvalidOperationException("分解结果与当前配方指纹不一致，请重新分解。");
        return Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var processed = processor.Apply(analysis.Pyramid, recipe, cancellationToken);
            var source = analysis.IsFullSize ? session.SourceImage : session.AnalysisProxy;
            var reconstruction = reconstructor.Reconstruct(source, analysis.OriginalPlane, processed.Pyramid, cancellationToken);
            FullReferenceQualityMetrics? quality = null;
            var reference = analysis.IsFullSize ? session.ReferenceImage : session.ReferenceProxy;
            if (reference is not null)
                quality = qualityAnalyzer.Analyze(reference, reconstruction.Image, cancellationToken);
            return new WaveletDenoiseResult(processed.Pyramid, reconstruction, processed.Statistics, quality,
                recipe.Fingerprint(), analysis.IsFullSize, stopwatch.Elapsed);
        }, cancellationToken);
    }
}

/// <summary>从最深 LL 逐层逆变换到指定层，并生成只用于教学显示的灰度预览。</summary>
internal sealed class ReconstructWaveletLevelUseCase(WaveletTransformCatalog catalog) : IReconstructWaveletLevelUseCase
{
    public Task<WaveletLevelReconstructionResult> ExecuteAsync(WaveletAnalysisResult analysis, int targetLevel,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var plane = catalog.Resolve(analysis.Pyramid.Transform).InverseToLevel(analysis.Pyramid, targetLevel, cancellationToken);
        var rgba = new byte[checked((int)(plane.Size.PixelCount * 4))];
        var values = plane.Values.Span;
        for (var y = 0; y < plane.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < plane.Size.Width; x++)
            {
                var index = (y * plane.Size.Width) + x;
                var gray = (byte)Math.Clamp((int)Math.Round(values[index], MidpointRounding.AwayFromZero), 0, 255);
                var offset = index * 4; rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = gray; rgba[offset + 3] = 255;
            }
        }
        return new WaveletLevelReconstructionResult(targetLevel, plane, new PixelImage(plane.Size, rgba));
    }, cancellationToken);
}

/// <summary>按固定“层数优先、阈值次序”串行执行有限扫描；取消时保留已完成统计。</summary>
internal sealed class RunWaveletQualityScanUseCase(
    IDecomposeWaveletUseCase decompose,
    IDenoiseWaveletUseCase denoise) : IRunWaveletQualityScanUseCase
{
    public async Task<WaveletScanResult> ExecuteAsync(WaveletSession session, WaveletDenoiseRecipe template,
        IReadOnlyList<double> thresholds, IReadOnlyList<int> levels, CancellationToken cancellationToken)
    {
        if (thresholds.Count is < 1 or > WaveletLimits.MaximumScanThresholds)
            throw new ArgumentException($"阈值扫描点必须为 1–{WaveletLimits.MaximumScanThresholds} 个。", nameof(thresholds));
        if (levels.Count is < 1 or > WaveletLimits.MaximumLevels || checked(thresholds.Count * levels.Count) > WaveletLimits.MaximumScanCases)
            throw new ArgumentException($"扫描案例总数不能超过 {WaveletLimits.MaximumScanCases}。", nameof(levels));
        if (thresholds.Any(value => !double.IsFinite(value) || value < 0d) || levels.Any(level => level is < 1 or > WaveletLimits.MaximumLevels))
            throw new ArgumentException("扫描阈值或层数包含非法值。");
        var cases = new List<WaveletScanCase>();
        try
        {
            foreach (var level in levels.Distinct().Order())
                foreach (var threshold in thresholds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var targetLevels = template.TargetLevels.Where(value => value <= level).DefaultIfEmpty(level);
                    var recipe = new WaveletDenoiseRecipe(template.Transform, template.Channel, level, template.Mode,
                        template.Source, threshold, targetLevels, template.TargetSubbands);
                    var analysis = await decompose.ExecuteAsync(session, recipe, fullSize: false, 1,
                        WaveletSubband.DiagonalDetail, WaveletProjectionMode.Symmetric, cancellationToken).ConfigureAwait(false);
                    var result = await denoise.ExecuteAsync(session, analysis, recipe, cancellationToken).ConfigureAwait(false);
                    cases.Add(new(cases.Count, level, threshold, result.ThresholdStatistics,
                        result.Reconstruction.RootMeanSquareError, result.ReferenceQuality?.PsnrLumaDb,
                        result.ReferenceQuality?.GlobalSsimLuma, result.Elapsed));
                }
            return new(cases, false, session.ReferenceImage is null
                ? "无干净参考图：不提供最佳去噪质量排序。"
                : "扫描在与源图同规则生成的参考代理上计算 PSNR/SSIM；最终导出结论仍应复核完整尺寸结果。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(cases, true, "扫描已取消；仅保留取消前完成的案例，不视为完整扫描。");
        }
    }
}

internal sealed class ExportWaveletImageUseCase(IImageCodec codec, IAtomicFileWriter writer) : IExportWaveletImageUseCase
{
    public async Task ExecuteAsync(WaveletDenoiseResult result, string expectedFingerprint, string path, CancellationToken cancellationToken)
    {
        if (!result.IsFullSize || !StringComparer.Ordinal.Equals(result.RecipeFingerprint, expectedFingerprint))
            throw new InvalidOperationException("完整尺寸结果已过期或配方不一致，禁止导出。");
        var bytes = await codec.EncodeAsync(result.Reconstruction.Image, ImageOutputFormat.Png, 100, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class ExportWaveletReportUseCase(IWaveletReportSerializer serializer, IAtomicFileWriter writer)
    : IExportWaveletReportUseCase
{
    public Task ExecuteAsync(WaveletExperimentReport report, string path, bool csv, CancellationToken cancellationToken) =>
        writer.WriteAsync(path, csv ? serializer.SerializeCsv(report) : serializer.SerializeJson(report), cancellationToken);
}
