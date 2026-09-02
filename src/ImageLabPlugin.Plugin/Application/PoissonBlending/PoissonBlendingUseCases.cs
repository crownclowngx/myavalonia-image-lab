using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.PoissonBlending;

namespace ImageLabPlugin.Application.PoissonBlending;

internal sealed class PreparePoissonSessionUseCase(IImageCodec codec) : IPreparePoissonSessionUseCase
{
    public async Task ExecuteAsync(PoissonBlendingSession session, string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath); ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        // 两次解码仍受 PixelImage 的 16 MP 边界约束；Prepare 只载入，不建立遮罩、方程或后台求解。
        var source = await codec.DecodeAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var target = await codec.DecodeAsync(targetPath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested(); session.Initialize(sourcePath, source, targetPath, target);
    }
}

internal sealed class EditPoissonMaskUseCase(PoissonMaskRasterizer rasterizer, PoissonMaskTopologyAnalyzer analyzer) : IEditPoissonMaskUseCase
{
    public PoissonMaskTopology Apply(PoissonBlendingSession session, PoissonMaskDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session); if (session.SourceImage is null) throw new InvalidOperationException("请先载入两张图片。 ");
        var mask = rasterizer.Rasterize(session.SourceImage.Size, definition, cancellationToken);
        var topology = analyzer.Analyze(mask, cancellationToken); session.SetMask(definition, mask, topology); return topology;
    }
}

internal sealed class PlacePoissonRegionUseCase(PoissonPlacementValidator validator) : IPlacePoissonRegionUseCase
{
    public PoissonPlacementValidation Apply(PoissonBlendingSession session, ImageOffset offset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.SourceImage is null || session.TargetImage is null || session.Mask is null) throw new InvalidOperationException("请先载入两图并建立遮罩。 ");
        var result = validator.Validate(session.SourceImage, session.TargetImage, session.Mask, offset, cancellationToken);
        session.SetPlacement(offset, result); return result;
    }
}

internal sealed class BuildPoissonProblemUseCase(PoissonProblemBuilder builder, PoissonRelaxationSolver solver,
    DirectAlphaCompositor alphaCompositor, PoissonBlendComposer composer,
    PoissonBlendDiagnosticsAnalyzer analyzer) : IBuildPoissonProblemUseCase
{
    public PoissonProblem Execute(PoissonBlendingSession session, PoissonBlendOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.SourceImage is null || session.TargetImage is null || session.Mask is null || session.Placement?.IsValid != true)
            throw new InvalidOperationException("请先通过遮罩、放置、halo 与透明度预检。 ");
        var problem = builder.Build(session.SourceImage, session.TargetImage, session.Mask, session.Offset, options, cancellationToken);
        var state = solver.CreateState(problem, options);
        var alpha = alphaCompositor.Compose(session.SourceImage, session.TargetImage, session.Mask, session.Offset);
        cancellationToken.ThrowIfCancellationRequested(); session.SetProblem(options, problem, state, alpha);
        // 初值可能已经严格满足方程（例如常量源/目标）。这种第零轮收敛也必须生成诚实结果，
        // 不能强迫用户执行一轮会改变“迭代 0”事实的伪 Step。
        if (state.StopReason == PoissonStopReason.Converged)
        {
            var composed = composer.Compose(session.TargetImage, problem, state); session.SetCurrentSolution(composed.Image);
            var diagnostics = analyzer.Analyze(session.SourceImage, session.TargetImage, composed.Image, session.Mask,
                session.Offset, problem, state, composed.ClampStatistics);
            session.SetResult(new(problem.Fingerprint, composed.Image, alpha, diagnostics, state.History[^1], PoissonStopReason.Converged));
        }
        return problem;
    }
}

internal sealed class StepPoissonSolverUseCase(PoissonRelaxationSolver solver, PoissonBlendComposer composer,
    PoissonBlendDiagnosticsAnalyzer analyzer) : IStepPoissonSolverUseCase
{
    public Task<PoissonResidual> ExecuteAsync(PoissonBlendingSession session, CancellationToken cancellationToken) =>
        Task.Run(() => ExecuteCore(session, cancellationToken), cancellationToken);

    private PoissonResidual ExecuteCore(PoissonBlendingSession session, CancellationToken token)
    {
        session.ThrowIfDisposed();
        if (session.Problem is null || session.SolverState is null || session.Options is null || session.TargetImage is null ||
            session.SourceImage is null || session.Mask is null || session.AlphaBaseline is null)
            throw new InvalidOperationException("请先建立 Poisson 问题。 ");
        try
        {
            var residual = solver.Step(session.Problem, session.SolverState, session.Options, token);
            // 当前解只保留一张目标尺寸 PixelImage；下一轮替换旧引用，不保存全部迭代帧。
            // Run 的 UI 提交间隔只控制观察频率，数值核心与最终 double 不受它影响。
            var composed = composer.Compose(session.TargetImage, session.Problem, session.SolverState);
            session.SetCurrentSolution(composed.Image);
            if (session.SolverState.StopReason is { } reason)
            {
                var diagnostics = analyzer.Analyze(session.SourceImage, session.TargetImage, composed.Image, session.Mask,
                    session.Offset, session.Problem, session.SolverState, composed.ClampStatistics);
                session.SetResult(new(session.Problem.Fingerprint, composed.Image, session.AlphaBaseline, diagnostics, residual, reason));
            }
            else session.SetPaused();
            return residual;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { session.SetCanceled(); throw; }
        catch { session.SetFaulted(); throw; }
    }
}

internal sealed class RunPoissonSolverUseCase(IStepPoissonSolverUseCase step) : IRunPoissonSolverUseCase
{
    public async Task ExecuteAsync(PoissonBlendingSession session, Func<PoissonResidual, Task>? progress,
        Func<bool>? shouldPause, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session); session.SetRunning();
        while (session.SolverState?.StopReason is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (shouldPause?.Invoke() == true) { session.SetPaused(); return; }
            var residual = await step.ExecuteAsync(session, cancellationToken).ConfigureAwait(false);
            var state = session.SolverState ?? throw new InvalidOperationException("求解状态在运行中意外失效。 ");
            if (progress is not null && (residual.Iteration % (session.Options?.PreviewInterval ?? 10) == 0 || state.StopReason is not null))
                await progress(residual).ConfigureAwait(false);
            if (state.StopReason is null) session.SetRunning();
        }
    }
}

internal sealed class ExportPoissonImageUseCase(IImageCodec codec, IAtomicFileWriter writer) : IExportPoissonImageUseCase
{
    public async Task ExecuteAsync(PoissonBlendingSession session, string outputPath, bool alphaBaseline,
        bool allowUnconvergedPreview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!Path.GetExtension(outputPath).Equals(".png", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("融合结果只允许导出 PNG。 ");
        if (session.Result is null) throw new InvalidOperationException("当前没有可导出的完整结果。 ");
        if (session.Result.StopReason != PoissonStopReason.Converged && !allowUnconvergedPreview)
            throw new InvalidOperationException("未收敛预览不能作为正式结果导出。 ");
        if (!alphaBaseline && session.Result.StopReason != PoissonStopReason.Converged && !Path.GetFileNameWithoutExtension(outputPath).EndsWith("-unconverged-preview", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("未收敛另存文件名必须以 -unconverged-preview 结尾。 ");
        var image = alphaBaseline ? session.Result.AlphaBaseline : session.Result.Output;
        var bytes = await codec.EncodeAsync(image, ImageOutputFormat.Png, 100, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(outputPath, bytes, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class ExportPoissonReportUseCase(IPoissonBlendingReportSerializer serializer, IAtomicFileWriter writer) : IExportPoissonReportUseCase
{
    public Task ExecuteAsync(PoissonBlendingReport report, string outputPath, bool csv, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report); ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var expected = csv ? ".csv" : ".json";
        if (!Path.GetExtension(outputPath).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"报告扩展名必须是 {expected}。 ");
        return writer.WriteAsync(outputPath, csv ? serializer.SerializeCsv(report) : serializer.SerializeJson(report), cancellationToken);
    }
}
