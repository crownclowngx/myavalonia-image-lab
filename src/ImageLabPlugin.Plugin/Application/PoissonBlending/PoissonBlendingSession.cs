using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.PoissonBlending;

namespace ImageLabPlugin.Application.PoissonBlending;

/// <summary>
/// 一个 Persistable Document Scope 独占的 Poisson 大对象所有权边界。Session 非线程安全，由 Document 的串行闸门保护；
/// 它保存图片、遮罩、问题、当前解和有限残差，不保存 Bitmap、文件对话框、取消源、DPI 或完整迭代帧。
/// 任一输入语义变化都会递增 Generation 并清除下游状态，杜绝旧问题与新输入混用。
/// </summary>
internal sealed class PoissonBlendingSession : IDisposable
{
    private bool _disposed;
    public string SourcePath { get; private set; } = string.Empty;
    public string TargetPath { get; private set; } = string.Empty;
    public string SourceFingerprint { get; private set; } = string.Empty;
    public string TargetFingerprint { get; private set; } = string.Empty;
    public PixelImage? SourceImage { get; private set; }
    public PixelImage? TargetImage { get; private set; }
    public PoissonMaskDefinition? MaskDefinition { get; private set; }
    public PoissonBinaryMask? Mask { get; private set; }
    public PoissonMaskTopology? Topology { get; private set; }
    public ImageOffset Offset { get; private set; }
    public PoissonPlacementValidation? Placement { get; private set; }
    public PoissonBlendOptions? Options { get; private set; }
    public PoissonProblem? Problem { get; private set; }
    public PoissonSolverState? SolverState { get; private set; }
    public PixelImage? AlphaBaseline { get; private set; }
    public PixelImage? CurrentSolution { get; private set; }
    public PoissonBlendResult? Result { get; private set; }
    public PoissonSessionState State { get; private set; }
    public long Generation { get; private set; }

    public void Initialize(string sourcePath, PixelImage source, string targetPath, PixelImage target)
    {
        ThrowIfDisposed(); ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath); ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        SourcePath = sourcePath; TargetPath = targetPath; SourceImage = source; TargetImage = target;
        SourceFingerprint = PoissonFingerprint.ForImage(source); TargetFingerprint = PoissonFingerprint.ForImage(target);
        Generation++; ClearSelection(); State = PoissonSessionState.ImagesReady;
    }

    public void SetMask(PoissonMaskDefinition definition, PoissonBinaryMask mask, PoissonMaskTopology topology)
    {
        ThrowIfDisposed(); if (SourceImage is null) throw new InvalidOperationException("请先载入源图和目标图。 ");
        MaskDefinition = definition; Mask = mask; Topology = topology; Generation++; ClearProblem();
        State = topology.UnknownCount == 0 ? PoissonSessionState.ImagesReady : PoissonSessionState.MaskReady;
    }

    /// <summary>保留已解码两图并清除选择、问题和结果；Reset 不触发 IO 或自动重建。</summary>
    public void ResetSelection()
    {
        ThrowIfDisposed(); if (SourceImage is null || TargetImage is null) return;
        Generation++; ClearSelection(); State = PoissonSessionState.ImagesReady;
    }

    public void SetPlacement(ImageOffset offset, PoissonPlacementValidation validation)
    {
        ThrowIfDisposed(); if (Mask is null) throw new InvalidOperationException("请先建立非空遮罩。 ");
        Offset = offset; Placement = validation; Generation++; ClearProblem();
        State = validation.IsValid ? PoissonSessionState.PlacementReady : PoissonSessionState.MaskReady;
    }

    public void SetProblem(PoissonBlendOptions options, PoissonProblem problem, PoissonSolverState state, PixelImage alpha)
    {
        ThrowIfDisposed(); Options = options; Problem = problem; SolverState = state; AlphaBaseline = alpha; CurrentSolution = null; Result = null;
        State = state.StopReason == PoissonStopReason.Converged ? PoissonSessionState.Converged : PoissonSessionState.ProblemReady;
    }

    public void SetRunning() { ThrowIfDisposed(); State = PoissonSessionState.Running; }
    public void SetPaused() { ThrowIfDisposed(); if (State != PoissonSessionState.Converged) State = PoissonSessionState.Paused; }
    public void SetCanceled() { ThrowIfDisposed(); State = PoissonSessionState.Canceled; }
    public void SetFaulted() { if (!_disposed) State = PoissonSessionState.Faulted; }
    public void SetCurrentSolution(PixelImage image) { ThrowIfDisposed(); CurrentSolution = image; }

    public void SetResult(PoissonBlendResult result)
    {
        ThrowIfDisposed(); if (Problem is null || result.ProblemFingerprint != Problem.Fingerprint) throw new InvalidOperationException("结果已经过期。 ");
        Result = result;
        State = result.StopReason == PoissonStopReason.Converged ? PoissonSessionState.Converged : PoissonSessionState.Paused;
    }

    public PoissonBlendingReport CreateReport()
    {
        ThrowIfDisposed();
        if (SourceImage is null || TargetImage is null || Problem is null || SolverState is null || Result is null || Options is null)
            throw new InvalidOperationException("只有已经合成的当前结果才能建立报告。 ");
        return new(SourceFingerprint, TargetFingerprint, SourceImage.Size, TargetImage.Size, Problem.Mode, Offset,
            Problem.Topology, Options, Problem.ResourceEstimate, SolverState.History.ToArray(),
            Result.StopReason, Result.Diagnostics, DateTimeOffset.UtcNow,
            Result.Diagnostics.ClampStatistics.ClippedPixelCount > 0 ? ["发生 sRGB 色域裁剪；裁剪后的结果不再严格满足未约束方程。"] : []);
    }

    public void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(PoissonBlendingSession)); }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Generation++;
        SourceImage = null; TargetImage = null; MaskDefinition = null; Mask = null; Topology = null; Placement = null;
        Options = null; Problem = null; SolverState = null; AlphaBaseline = null; CurrentSolution = null; Result = null; State = PoissonSessionState.Disposed;
    }

    private void ClearSelection()
    { MaskDefinition = null; Mask = null; Topology = null; Offset = default; Placement = null; ClearProblem(); }
    private void ClearProblem()
    { Options = null; Problem = null; SolverState = null; AlphaBaseline = null; CurrentSolution = null; Result = null; }
}
