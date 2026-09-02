using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.SeamCarving;

namespace ImageLabPlugin.Application.SeamCarving;

/// <summary>一个 Document Scope 独占的 Seam Carving 状态与大数组所有权边界。</summary>
/// <remarks>
/// Session 非线程安全，只能由所属 Document 串行使用。它最多保留输入、当前工作图、当前蒙版、下一缝、
/// 当前插入批次和参考结果，不保存所有 RGBA 历史帧。重置从不可变输入克隆重建，释放后清空引用并拒绝访问。
/// Avalonia Bitmap、Dispatcher 和取消源不属于这里，仍由 Document 管理。
/// </remarks>
internal sealed class SeamCarvingSession : IDisposable
{
    private readonly List<SeamStepRecord> _records = [];
    private readonly List<SeamInsertionPath> _appliedInsertionPaths = [];
    private bool _disposed;

    public string SourcePath { get; private set; } = string.Empty;
    public string InputFingerprint { get; private set; } = string.Empty;
    public PixelImage? InputImage { get; private set; }
    public SeamMask? InputMask { get; private set; }
    public PixelImage? CurrentImage { get; private set; }
    public SeamMask? CurrentMask { get; private set; }
    public IReadOnlyList<SeamBrushStroke> Strokes { get; private set; } = [];
    public SeamResizePlan? Plan { get; private set; }
    public SeamStepPreview? Preview { get; private set; }
    public SeamInsertionBatch? InsertionBatch { get; private set; }
    public int InsertionBatchIndex { get; private set; }
    public IReadOnlyList<SeamInsertionPath> AppliedInsertionPaths => _appliedInsertionPaths;
    public int StepIndex { get; private set; }
    public IReadOnlyList<SeamStepRecord> Records => _records;
    public SeamComparison? Comparison { get; private set; }
    public SeamPlaybackState State { get; private set; } = SeamPlaybackState.Empty;
    public long Revision { get; private set; }
    public bool HasCompletedResult => State == SeamPlaybackState.Completed && Plan is not null &&
        CurrentImage?.Size == Plan.Request.TargetSize;

    public void Initialize(string sourcePath, PixelImage image)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(image);
        SourcePath = sourcePath;
        InputImage = image;
        InputFingerprint = SeamFingerprint.ForImage(image);
        Strokes = [];
        InputMask = new SeamMask(image.Size);
        CurrentMask = InputMask.Clone();
        ResetExecutionCore();
        State = SeamPlaybackState.Ready;
        Revision++;
    }

    public void SetMask(IReadOnlyList<SeamBrushStroke> strokes, SeamMask mask)
    {
        ThrowIfDisposed(); EnsureLoaded();
        if (mask.Size != InputImage!.Size) throw new ArgumentException("蒙版尺寸必须匹配输入图。", nameof(mask));
        Strokes = strokes.ToArray();
        InputMask = mask.Clone();
        CurrentMask = mask.Clone();
        CurrentImage = InputImage.Clone();
        Plan = null; Preview = null; Comparison = null; InsertionBatch = null; InsertionBatchIndex = 0;
        _appliedInsertionPaths.Clear(); _records.Clear(); StepIndex = 0;
        State = SeamPlaybackState.Stale;
        Revision++;
    }

    public void SetPlan(SeamResizePlan plan)
    {
        ThrowIfDisposed(); EnsureLoaded(); ArgumentNullException.ThrowIfNull(plan);
        if (plan.InputSize != InputImage!.Size || !StringComparer.Ordinal.Equals(plan.InputFingerprint, InputFingerprint))
            throw new InvalidOperationException("计划不属于当前输入图片。");
        if (!StringComparer.Ordinal.Equals(plan.MaskFingerprint, SeamFingerprint.ForMask(InputMask!)))
            throw new InvalidOperationException("计划不属于当前蒙版。");
        CurrentImage = InputImage.Clone();
        CurrentMask = InputMask!.Clone();
        Plan = plan; Preview = null; Comparison = null; StepIndex = 0;
        InsertionBatch = null; InsertionBatchIndex = 0; _appliedInsertionPaths.Clear(); _records.Clear();
        State = plan.Steps.Count == 0 ? SeamPlaybackState.Completed : SeamPlaybackState.Paused;
        Revision++;
    }

    public void SetPreview(SeamStepPreview? preview)
    {
        ThrowIfDisposed();
        Preview = preview;
    }

    public void SetInsertionBatch(SeamInsertionBatch batch)
    {
        ThrowIfDisposed();
        InsertionBatch = batch;
        InsertionBatchIndex = 0;
        _appliedInsertionPaths.Clear();
    }

    public SeamInsertionPath GetCurrentInsertionPath()
    {
        ThrowIfDisposed();
        if (InsertionBatch is null || InsertionBatchIndex >= InsertionBatch.Paths.Count)
            throw new InvalidOperationException("当前没有可消费的插入批次路径。");
        return InsertionBatch.Paths[InsertionBatchIndex];
    }

    public void CommitStep(PixelImage image, SeamMask mask, SeamStepRecord record,
        SeamInsertionPath? insertedPath = null)
    {
        ThrowIfDisposed();
        CurrentImage = image;
        CurrentMask = mask;
        _records.Add(record);
        if (insertedPath is not null)
        {
            _appliedInsertionPaths.Add(insertedPath);
            InsertionBatchIndex++;
            if (InsertionBatchIndex >= (InsertionBatch?.Paths.Count ?? 0))
            { InsertionBatch = null; InsertionBatchIndex = 0; _appliedInsertionPaths.Clear(); }
        }
        else
        { InsertionBatch = null; InsertionBatchIndex = 0; _appliedInsertionPaths.Clear(); }
        StepIndex++;
        Preview = null;
        Comparison = null;
        State = StepIndex >= (Plan?.Steps.Count ?? 0) ? SeamPlaybackState.Completed : SeamPlaybackState.Paused;
    }

    public void SetState(SeamPlaybackState state) { ThrowIfDisposed(); State = state; }
    public void SetComparison(SeamComparison value) { ThrowIfDisposed(); Comparison = value; }

    public void Reset()
    {
        ThrowIfDisposed(); EnsureLoaded();
        CurrentImage = InputImage!.Clone();
        InputMask = new SeamMask(InputImage.Size);
        CurrentMask = InputMask.Clone();
        if (Strokes.Count != 0) throw new InvalidOperationException("带笔划重置必须通过蒙版用例重放，以免丢失用户意图。");
        ResetExecutionCore(); State = SeamPlaybackState.Ready; Revision++;
    }

    public void ResetWithMask(SeamMask mask)
    {
        ThrowIfDisposed(); EnsureLoaded();
        InputMask = mask.Clone(); CurrentImage = InputImage!.Clone(); CurrentMask = mask.Clone();
        ResetExecutionCore(); State = SeamPlaybackState.Ready; Revision++;
    }

    public SeamCarvingReport CreateReport()
    {
        ThrowIfDisposed(); EnsureLoaded();
        if (Plan is null) throw new InvalidOperationException("尚未建立计划，不能生成报告。");
        return new(InputFingerprint, InputImage!.Size, Plan.Request.TargetSize, Plan.Request.AxisOrder,
            Plan.Request.ReferenceAlgorithm, State, Plan.ResourceEstimate, _records.ToArray(),
            InputMask!.CountValues(), Comparison?.SeamVsReference, DateTimeOffset.UtcNow,
            ["内容感知不等于语义理解；保护/删除是有限能量偏置。", "算法间差异指标不是审美或质量排名。"]) ;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SourcePath = string.Empty; InputFingerprint = string.Empty; InputImage = null; CurrentImage = null;
        InputMask = null; CurrentMask = null; Strokes = []; Plan = null; Preview = null; InsertionBatch = null; Comparison = null;
        _records.Clear(); _appliedInsertionPaths.Clear(); State = SeamPlaybackState.Empty;
    }

    private void ResetExecutionCore()
    {
        CurrentImage = InputImage!.Clone(); Plan = null; Preview = null; InsertionBatch = null;
        InsertionBatchIndex = 0; _appliedInsertionPaths.Clear(); _records.Clear(); Comparison = null; StepIndex = 0;
    }

    private void EnsureLoaded()
    {
        if (InputImage is null || CurrentImage is null || CurrentMask is null)
            throw new InvalidOperationException("请先载入图片。");
    }
    public void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
