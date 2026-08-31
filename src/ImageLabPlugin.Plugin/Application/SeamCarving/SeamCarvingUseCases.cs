using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.SeamCarving;

namespace ImageLabPlugin.Application.SeamCarving;

internal sealed class PrepareSeamCarvingSessionUseCase(IImageCodec codec)
    : IPrepareSeamCarvingSessionUseCase
{
    public async Task ExecuteAsync(SeamCarvingSession session, string sourcePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var image = await codec.DecodeAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        if (image.Size.PixelCount > SeamResourceEstimator.MaximumWorkingPixels)
            throw new InvalidOperationException($"输入图片 {image.Size.PixelCount:N0} 像素超过内容感知缩放的 " +
                $"{SeamResourceEstimator.MaximumWorkingPixels:N0} 工作上限；请先用普通工具缩小图片。");
        cancellationToken.ThrowIfCancellationRequested();
        session.Initialize(sourcePath, image);
    }
}

internal sealed class EditSeamMaskUseCase(SeamMaskRasterizer rasterizer) : IEditSeamMaskUseCase
{
    public void Apply(SeamCarvingSession session, IReadOnlyList<SeamBrushStroke> strokes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.InputImage is null) throw new InvalidOperationException("请先载入图片再编辑区域。");
        var mask = rasterizer.Rasterize(session.InputImage.Size, strokes, cancellationToken);
        session.SetMask(strokes, mask);
    }
}

internal sealed class PlanSeamResizeUseCase(SeamResizePlanner planner) : IPlanSeamResizeUseCase
{
    public SeamResizePlan Execute(SeamCarvingSession session, SeamResizeRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.InputImage is null || session.InputMask is null) throw new InvalidOperationException("请先载入图片。");
        var plan = planner.Plan(session.InputFingerprint, SeamFingerprint.ForMask(session.InputMask),
            session.InputImage.Size, request);
        session.SetPlan(plan);
        return plan;
    }
}

internal sealed class PreviewNextSeamUseCase(
    SobelEnergyCalculator energyCalculator,
    MinimumEnergySeamFinder seamFinder,
    SeamInsertionPlanner insertionPlanner) : IPreviewNextSeamUseCase
{
    public Task<SeamStepPreview?> ExecuteAsync(SeamCarvingSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Task.Run(() => ExecuteCore(session, cancellationToken), cancellationToken);
    }

    private SeamStepPreview? ExecuteCore(SeamCarvingSession session, CancellationToken cancellationToken)
    {
        session.ThrowIfDisposed();
        if (session.Plan is null || session.CurrentImage is null || session.CurrentMask is null)
            throw new InvalidOperationException("请先建立执行计划。");
        if (session.StepIndex >= session.Plan.Steps.Count) return null;
        if (session.Preview is not null) return session.Preview;
        var step = session.Plan.Steps[session.StepIndex];
        var energy = energyCalculator.Calculate(session.CurrentImage, session.CurrentMask, cancellationToken);
        SeamPath path;
        if (step.Operation == SeamOperation.Remove)
        {
            path = seamFinder.Find(energy, session.CurrentMask, step.Orientation, cancellationToken);
        }
        else
        {
            EnsureInsertionBatch(session, step, cancellationToken);
            var planned = session.GetCurrentInsertionPath();
            var adjusted = SeamInserter.AdjustCoordinates(planned, session.AppliedInsertionPaths);
            double baseTotal = 0d, effectiveTotal = 0d;
            var protectHits = 0; var removalHits = 0;
            for (var main = 0; main < adjusted.Length; main++)
            {
                var x = step.Orientation == SeamOrientation.Vertical ? adjusted[main] : main;
                var y = step.Orientation == SeamOrientation.Vertical ? main : adjusted[main];
                baseTotal += energy.GetBase(x, y); effectiveTotal += energy.GetEffective(x, y);
                var value = session.CurrentMask.Get(x, y);
                if (value == SeamMaskValue.Protect) protectHits++;
                else if (value == SeamMaskValue.PreferRemoval) removalHits++;
            }
            path = new SeamPath(step.Orientation, session.CurrentImage.Size, adjusted,
                baseTotal, effectiveTotal, protectHits, removalHits);
        }
        var preview = new SeamStepPreview(session.StepIndex + 1, session.Plan.Steps.Count,
            step.Operation, energy, path);
        session.SetPreview(preview);
        return preview;
    }

    private void EnsureInsertionBatch(SeamCarvingSession session,
        (SeamOrientation Orientation, SeamOperation Operation) step, CancellationToken cancellationToken)
    {
        if (session.InsertionBatch is not null && session.InsertionBatch.Orientation == step.Orientation) return;
        var remaining = 0;
        for (var index = session.StepIndex; index < session.Plan!.Steps.Count; index++)
        {
            var candidate = session.Plan.Steps[index];
            if (candidate != step) break;
            remaining++;
        }
        var secondary = step.Orientation == SeamOrientation.Vertical
            ? session.CurrentImage!.Size.Width : session.CurrentImage!.Size.Height;
        if (secondary <= 1) throw new InvalidOperationException("次轴尺寸为 1，缺少可插值邻居，不能规划插入缝。");
        var batchCount = Math.Min(remaining, secondary - 1);
        session.SetInsertionBatch(insertionPlanner.Plan(session.CurrentImage!, session.CurrentMask!,
            step.Orientation, batchCount, cancellationToken));
    }
}

internal sealed class ApplySeamStepUseCase(
    SeamRemover remover,
    SeamInserter inserter,
    IPreviewNextSeamUseCase previewUseCase) : IApplySeamStepUseCase
{
    public async Task<SeamStepRecord?> ExecuteAsync(SeamCarvingSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var preview = session.Preview ?? await previewUseCase.ExecuteAsync(session, cancellationToken).ConfigureAwait(false);
        if (preview is null) return null;
        return await Task.Run(() => ApplyCore(session, preview, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private SeamStepRecord ApplyCore(SeamCarvingSession session, SeamStepPreview preview,
        CancellationToken cancellationToken)
    {
        if (session.CurrentImage is null || session.CurrentMask is null || session.Plan is null)
            throw new InvalidOperationException("当前执行状态不完整。");
        var before = session.CurrentImage.Size;
        PixelImage image;
        SeamMask mask;
        SeamInsertionPath? insertedPath = null;
        if (preview.Operation == SeamOperation.Remove)
        {
            (image, mask) = remover.Remove(session.CurrentImage, session.CurrentMask, preview.Path, cancellationToken);
        }
        else
        {
            insertedPath = session.GetCurrentInsertionPath();
            (image, mask, _) = inserter.Insert(session.CurrentImage, session.CurrentMask, insertedPath,
                session.AppliedInsertionPaths, cancellationToken);
        }
        var record = new SeamStepRecord(preview.StepNumber, preview.Path.Orientation, preview.Operation,
            before, image.Size, preview.Path.BaseEnergy, preview.Path.EffectiveEnergy,
            preview.Path.ProtectHits, preview.Path.PreferRemovalHits);
        cancellationToken.ThrowIfCancellationRequested();
        session.CommitStep(image, mask, record, insertedPath);
        return record;
    }
}

internal sealed class RunSeamPlaybackUseCase(
    IPreviewNextSeamUseCase preview,
    IApplySeamStepUseCase apply) : IRunSeamPlaybackUseCase
{
    public async Task ExecuteAsync(SeamCarvingSession session, Func<SeamStepRecord, Task>? progress,
        Func<bool>? shouldPause, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.SetState(SeamPlaybackState.Playing);
        try
        {
            while (session.Plan is not null && session.StepIndex < session.Plan.Steps.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (shouldPause?.Invoke() == true) { session.SetState(SeamPlaybackState.Paused); return; }
                await preview.ExecuteAsync(session, cancellationToken).ConfigureAwait(false);
                var result = await apply.ExecuteAsync(session, cancellationToken).ConfigureAwait(false);
                if (result is not null && progress is not null) await progress(result).ConfigureAwait(false);
                if (session.State != SeamPlaybackState.Completed) session.SetState(SeamPlaybackState.Playing);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            session.SetState(SeamPlaybackState.Canceled);
            throw;
        }
        catch
        {
            session.SetState(SeamPlaybackState.Faulted);
            throw;
        }
    }
}

internal sealed class CompareSeamResizeUseCase(
    IEnumerable<IReferenceImageResampler> resamplers,
    FullReferenceQualityAnalyzer qualityAnalyzer) : ICompareSeamResizeUseCase
{
    private readonly IReadOnlyDictionary<ReferenceResizeAlgorithm, IReferenceImageResampler> _resamplers =
        BuildCatalog(resamplers);

    public Task<SeamComparison> ExecuteAsync(SeamCarvingSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.HasCompletedResult || session.InputImage is null || session.CurrentImage is null || session.Plan is null)
            throw new InvalidOperationException("只有完整且未过期的 Seam 结果才能生成普通缩放对照。");
        return Task.Run(() =>
        {
            var algorithm = session.Plan.Request.ReferenceAlgorithm;
            var reference = _resamplers[algorithm].Resize(session.InputImage, session.Plan.Request.TargetSize, cancellationToken);
            var quality = qualityAnalyzer.Analyze(reference, session.CurrentImage, cancellationToken);
            var difference = ImageDifferenceProjector.Create(reference, session.CurrentImage);
            var comparison = new SeamComparison(algorithm, reference, difference, quality);
            cancellationToken.ThrowIfCancellationRequested();
            session.SetComparison(comparison);
            return comparison;
        }, cancellationToken);
    }

    private static IReadOnlyDictionary<ReferenceResizeAlgorithm, IReferenceImageResampler> BuildCatalog(
        IEnumerable<IReferenceImageResampler> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = new Dictionary<ReferenceResizeAlgorithm, IReferenceImageResampler>();
        foreach (var value in values)
            if (!result.TryAdd(value.Algorithm, value)) throw new InvalidOperationException($"参考缩放算法 {value.Algorithm} 重复登记。");
        foreach (var algorithm in Enum.GetValues<ReferenceResizeAlgorithm>())
            if (!result.ContainsKey(algorithm)) throw new InvalidOperationException($"参考缩放算法 {algorithm} 未登记。");
        return result;
    }
}

internal sealed class ExportSeamResultUseCase(IImageCodec codec, IAtomicFileWriter writer)
    : IExportSeamResultUseCase
{
    public async Task ExecuteAsync(SeamCarvingSession session, string outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!session.HasCompletedResult || session.CurrentImage is null)
            throw new InvalidOperationException("只允许导出完整且未过期的内容感知缩放结果。");
        if (!Path.GetExtension(outputPath).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("内容感知缩放结果只允许导出 PNG。");
        if (Path.GetFullPath(session.SourcePath).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("结果不能覆盖源图片。");
        var bytes = await codec.EncodeAsync(session.CurrentImage, ImageOutputFormat.Png, 100, cancellationToken)
            .ConfigureAwait(false);
        await writer.WriteAsync(outputPath, bytes, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class ExportSeamReportUseCase(ISeamCarvingReportSerializer serializer, IAtomicFileWriter writer)
    : IExportSeamReportUseCase
{
    public Task ExecuteAsync(SeamCarvingReport report, string outputPath, bool csv,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var bytes = csv ? serializer.SerializeCsv(report) : serializer.SerializeJson(report);
        return writer.WriteAsync(outputPath, bytes, cancellationToken);
    }
}
