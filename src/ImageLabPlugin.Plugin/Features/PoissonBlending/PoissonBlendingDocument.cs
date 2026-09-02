using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.PoissonBlending;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.PoissonBlending;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.PoissonBlending;

/// <summary>“Poisson Blending／梯度域融合”的多实例可持久化 Document。</summary>
/// <remarks>
/// Document 只负责命令、轻量快照、串行闸门、generation 和 Bitmap 生命周期；遮罩栅格化、halo、guidance、
/// 方程和迭代均由下层窄服务完成。任何输入/遮罩/偏移/模式变化都会取消旧操作并使结果过期；只有 generation
/// 与实例仍匹配时才提交 UI。关闭顺序为阻止新命令、取消、释放 Bitmap、Session 与闸门。
/// </remarks>
internal sealed partial class PoissonBlendingDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private const int MaximumSnapshotBytes = 128 * 1024;
    private readonly PoissonBlendingSession _session;
    private readonly IPreparePoissonSessionUseCase _prepare;
    private readonly IEditPoissonMaskUseCase _editMask;
    private readonly IPlacePoissonRegionUseCase _place;
    private readonly IBuildPoissonProblemUseCase _build;
    private readonly IStepPoissonSolverUseCase _step;
    private readonly IRunPoissonSolverUseCase _run;
    private readonly IExportPoissonImageUseCase _exportImage;
    private readonly IExportPoissonReportUseCase _exportReport;
    private readonly PoissonResidualProjector _residualProjector;
    private readonly PoissonFieldProjector _fieldProjector;
    private readonly IImageCodec _codec;
    private readonly IImageFileDialog _imageDialog;
    private readonly IPoissonBlendingFileDialog _poissonDialog;
    private readonly IDocumentLifetime _lifetime;
    private readonly List<PoissonMaskStroke> _strokes = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _operationCancellation;
    private DocumentPresentationState _presentation = new("梯度域融合");
    private long _generation;
    private long _revision;
    private long _acceptedRevision;
    private bool _pauseRequested;
    private bool _restoring;
    private bool _disposed;

    public PoissonBlendingDocument(PoissonBlendingSession session, IPreparePoissonSessionUseCase prepare,
        IEditPoissonMaskUseCase editMask, IPlacePoissonRegionUseCase place, IBuildPoissonProblemUseCase build,
        IStepPoissonSolverUseCase step, IRunPoissonSolverUseCase run, IExportPoissonImageUseCase exportImage,
        IExportPoissonReportUseCase exportReport, PoissonResidualProjector residualProjector, PoissonFieldProjector fieldProjector, IImageCodec codec,
        IImageFileDialog imageDialog, IPoissonBlendingFileDialog poissonDialog, IDocumentLifetime lifetime)
    {
        _session = session; _prepare = prepare; _editMask = editMask; _place = place; _build = build; _step = step;
        _run = run; _exportImage = exportImage; _exportReport = exportReport; _residualProjector = residualProjector; _fieldProjector = fieldProjector;
        _codec = codec; _imageDialog = imageDialog; _poissonDialog = poissonDialog; _lifetime = lifetime;
    }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _targetPath = string.Empty;
    [ObservableProperty] private int _rectangleLeft = 1;
    [ObservableProperty] private int _rectangleTop = 1;
    [ObservableProperty] private int _rectangleWidth = 1;
    [ObservableProperty] private int _rectangleHeight = 1;
    [ObservableProperty] private string _selectedBrush = "添加";
    [ObservableProperty] private int _brushRadius = 8;
    [ObservableProperty] private int _offsetX;
    [ObservableProperty] private int _offsetY;
    [ObservableProperty] private string _selectedMode = "普通克隆";
    [ObservableProperty] private double _rmsTolerance = 1e-6;
    [ObservableProperty] private double _maxAbsTolerance = 1e-5;
    [ObservableProperty] private int _maxIterations = 800;
    [ObservableProperty] private int _previewInterval = 10;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "请选择源图和目标图；收敛不等于主观视觉质量更好。";
    [ObservableProperty] private string _topologySummary = "尚未建立遮罩";
    [ObservableProperty] private string _resourceSummary = "尚未建立问题";
    [ObservableProperty] private string _convergenceSummary = "尚未迭代";
    [ObservableProperty] private Bitmap? _sourcePreview;
    [ObservableProperty] private Bitmap? _targetPreview;
    [ObservableProperty] private Bitmap? _poissonPreview;
    [ObservableProperty] private Bitmap? _alphaPreview;
    [ObservableProperty] private Bitmap? _residualPreview;
    [ObservableProperty] private Bitmap? _guidancePreview;
    [ObservableProperty] private Bitmap? _rhsPreview;
    [ObservableProperty] private IReadOnlyList<PoissonResidual> _residuals = Array.Empty<PoissonResidual>();

    public IReadOnlyList<string> BrushOptions { get; } = ["添加", "擦除"];
    public IReadOnlyList<string> ModeOptions { get; } = ["普通克隆", "混合梯度", "单色融合"];
    public IReadOnlyList<int> PreviewIntervalOptions { get; } = [1, 5, 10, 25, 50];
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public PoissonMaskTopology? Topology => _session.Topology;
    public ImageOffset PlacementOffset => new(OffsetX, OffsetY);
    public int StrokeCount => _strokes.Count;
    public event EventHandler? PresentationChanged;
    public event EventHandler? IsDirtyChanged;

    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation); cancellationToken.ThrowIfCancellationRequested(); _restoring = true;
        try
        {
            if (activation is RestoreDocumentActivation restore) Restore(restore.RestoredContent);
            _presentation = new(string.IsNullOrWhiteSpace(activation.Title) ? "梯度域融合" : activation.Title);
            PresentationChanged?.Invoke(this, EventArgs.Empty); _revision = _acceptedRevision = 0;
        }
        finally { _restoring = false; }
        return ValueTask.CompletedTask;
    }

    [RelayCommand] private async Task SelectSourceAsync() { var path = await _imageDialog.PickImageAsync(_lifetime.ClosingToken); if (path is not null) SourcePath = path; }
    [RelayCommand] private async Task SelectTargetAsync() { var path = await _imageDialog.PickImageAsync(_lifetime.ClosingToken); if (path is not null) TargetPath = path; }

    [RelayCommand]
    private Task SwapImagesAsync() => RunGuardedAsync("正在交换源图与目标图并清除旧问题…", async token =>
    {
        if (_session.SourceImage is null || _session.TargetImage is null) throw new InvalidOperationException("请先载入两张图片。 ");
        var oldSource = _session.SourceImage; var oldTarget = _session.TargetImage; var oldSourcePath = SourcePath; var oldTargetPath = TargetPath;
        _session.Initialize(oldTargetPath, oldTarget, oldSourcePath, oldSource);
        var source = await CreateBitmapAsync(oldTarget, token).ConfigureAwait(false); var target = await CreateBitmapAsync(oldSource, token).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _restoring = true; try { SourcePath = oldTargetPath; TargetPath = oldSourcePath; } finally { _restoring = false; }
            ReplaceSource(source); ReplaceTarget(target); _strokes.Clear(); RectangleLeft = RectangleTop = 1; RectangleWidth = RectangleHeight = 1;
            OffsetX = OffsetY = 0; TopologySummary = "尚未建立遮罩"; ClearDerived(); StatusMessage = "源图与目标图已交换；旧遮罩、放置、问题和结果已清除。"; NotifyFacts(); MarkChanged();
        });
    });

    [RelayCommand]
    private async Task LoadImagesAsync() => await RunGuardedAsync("正在解码两张图片…", async token =>
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || string.IsNullOrWhiteSpace(TargetPath)) throw new InvalidOperationException("请先选择源图和目标图。 ");
        await _prepare.ExecuteAsync(_session, SourcePath, TargetPath, token).ConfigureAwait(false);
        var source = await CreateBitmapAsync(_session.SourceImage!, token).ConfigureAwait(false);
        var target = await CreateBitmapAsync(_session.TargetImage!, token).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ReplaceSource(source); ReplaceTarget(target);
            _strokes.Clear(); RectangleLeft = 1; RectangleTop = 1;
            RectangleWidth = Math.Max(1, Math.Min(64, _session.SourceImage!.Size.Width - 2));
            RectangleHeight = Math.Max(1, Math.Min(64, _session.SourceImage.Size.Height - 2));
            OffsetX = 0; OffsetY = 0; ClearDerived(); StatusMessage = "两图已载入；请建立遮罩并预检放置。"; NotifyFacts(); MarkChanged();
        });
    });

    /// <summary>画布在指针释放时提交归一化笔划；Document 只保存意图并调用遮罩用例重放。</summary>
    internal void AddStroke(IReadOnlyList<PoissonNormalizedPoint> points)
    {
        if (_session.SourceImage is null || IsBusy || points.Count == 0) return;
        if (_strokes.Count >= PoissonMaskRasterizer.MaximumStrokes) { StatusMessage = "笔划已达到 512 条上限。"; return; }
        var radius = Math.Clamp(BrushRadius / (double)Math.Min(_session.SourceImage.Size.Width, _session.SourceImage.Size.Height), 0.001d, 0.25d);
        _strokes.Add(new PoissonMaskStroke(SelectedBrush == "擦除" ? PoissonMaskTool.Erase : PoissonMaskTool.Add,
            radius, points.ToArray(), _strokes.Count).Validate());
        ApplyMask();
    }

    [RelayCommand]
    private void ApplyMask()
    {
        try
        {
            if (_session.SourceImage is null) throw new InvalidOperationException("请先载入两图。 ");
            var rectangle = new PoissonRectangle(RectangleLeft, RectangleTop, RectangleWidth, RectangleHeight);
            var topology = _editMask.Apply(_session, new(rectangle, _strokes.ToArray()));
            TopologySummary = $"未知量 {topology.UnknownCount:N0}；分量 {topology.ComponentCount}；孔洞 {topology.HoleCount}；边界 {topology.BoundaryCount}";
            ClearDerived(); StatusMessage = topology.UnknownCount == 0 ? "遮罩为空。" : "遮罩已建立；请预检目标偏移。"; NotifyFacts(); MarkChanged();
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand]
    private void ClearMask()
    { _strokes.Clear(); RectangleWidth = RectangleHeight = 0; ApplyMask(); }

    [RelayCommand]
    private void Reset()
    {
        if (IsBusy) { Cancel(); return; }
        _session.ResetSelection(); _strokes.Clear(); RectangleLeft = RectangleTop = 1; RectangleWidth = RectangleHeight = 1;
        OffsetX = OffsetY = 0; TopologySummary = "尚未建立遮罩"; ClearDerived(); StatusMessage = "已保留两张已解码图片并清除选择、问题和结果；不会自动重建。"; NotifyFacts(); MarkChanged();
    }

    [RelayCommand]
    private void ValidatePlacement()
    {
        try
        {
            var validation = _place.Apply(_session, new(OffsetX, OffsetY)); ClearDerived();
            StatusMessage = validation.IsValid ? "放置、1 像素 halo 与 Alpha 预检通过。" : string.Join("；", validation.Issues.Select(item => item.Message).Distinct());
            NotifyFacts(); MarkChanged();
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand]
    private Task BuildProblemAsync() => RunGuardedAsync("正在预检预算并构造紧凑方程…", async token =>
    {
        var options = ResolveOptions();
        var problem = await Task.Run(() => _build.Execute(_session, options, token), token).ConfigureAwait(false);
        var guidanceImage = await Task.Run(() => _fieldProjector.ProjectGuidance(_session.SourceImage!, _session.TargetImage!,
            _session.Mask!, _session.Offset, problem.Mode), token).ConfigureAwait(false);
        var rhsImage = await Task.Run(() => _fieldProjector.ProjectRhs(problem), token).ConfigureAwait(false);
        var guidance = await CreateBitmapAsync(guidanceImage, token).ConfigureAwait(false);
        var rhs = await CreateBitmapAsync(rhsImage, token).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ReplaceGuidance(guidance); ReplaceRhs(rhs);
            ResourceSummary = $"unknown {problem.UnknownCount:N0}；{problem.ChannelCount} 通道；更新预算 {problem.ResourceEstimate.ScalarUpdates:N0}；峰值 {problem.ResourceEstimate.EstimatedPeakBytes / 1024d / 1024d:F1} MiB";
            Residuals = _session.SolverState!.History.ToArray(); UpdateConvergence(); StatusMessage = "问题已建立；Build 未执行 sweep。"; NotifyFacts(); MarkChanged();
        });
    });

    [RelayCommand]
    private Task StepAsync() => RunGuardedAsync("正在执行一个完整红黑 sweep…", async token =>
    {
        var residual = await _step.ExecuteAsync(_session, token).ConfigureAwait(false);
        await CommitProgressAsync(residual, token).ConfigureAwait(false);
    });

    [RelayCommand]
    private async Task RunAsync()
    {
        _pauseRequested = false;
        await RunGuardedAsync("正在运行；预览间隔不改变数值结果…", token => _run.ExecuteAsync(_session,
            residual => CommitProgressAsync(residual, token), () => _pauseRequested, token));
    }

    [RelayCommand] private void Pause() { _pauseRequested = true; StatusMessage = "已请求暂停；不会启动下一 sweep。"; }
    [RelayCommand] private void Cancel() { _operationCancellation?.Cancel(); StatusMessage = "已请求取消；半 sweep 和迟到结果不会提交。"; }

    [RelayCommand] private Task ExportPoissonAsync() => ExportImageAsync(alpha: false);
    [RelayCommand] private Task ExportAlphaAsync() => ExportImageAsync(alpha: true);
    [RelayCommand] private Task ExportJsonAsync() => ExportReportAsync(csv: false);
    [RelayCommand] private Task ExportCsvAsync() => ExportReportAsync(csv: true);

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 快照只保存显示名和选择意图，不保存绝对路径、像素、mask raster、RHS、解、残差或 Bitmap。
        var snapshot = new Snapshot(Path.GetFileName(SourcePath), Path.GetFileName(TargetPath), RectangleLeft, RectangleTop,
            RectangleWidth, RectangleHeight, _strokes.ToArray(), OffsetX, OffsetY, SelectedMode, RmsTolerance,
            MaxAbsTolerance, MaxIterations, PreviewInterval, PoissonProtocols.SnapshotSchema, PoissonProtocols.Numeric);
        var payload = JsonSerializer.SerializeToElement(snapshot); var bytes = Encoding.UTF8.GetByteCount(payload.GetRawText());
        if (bytes > MaximumSnapshotBytes) throw new InvalidOperationException($"快照 {bytes:N0} 字节超过 128 KiB；请简化笔划。 ");
        return ValueTask.FromResult(new DocumentSaveSnapshot(new DocumentRevision(_revision), new DocumentContent(SnapshotSchema, payload)));
    }

    public void AcceptChanges(DocumentRevision savedRevision)
    { var dirty = IsDirty; if (savedRevision.Value == _revision) _acceptedRevision = _revision; if (dirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_generation; CancelAndDispose();
        ReplaceSource(null); ReplaceTarget(null); ReplacePoisson(null); ReplaceAlpha(null);
        ReplaceResidual(null); ReplaceGuidance(null); ReplaceRhs(null); _session.Dispose(); _gate.Dispose();
    }

    partial void OnSourcePathChanged(string value) { if (!_restoring) MarkChanged(); }
    partial void OnTargetPathChanged(string value) { if (!_restoring) MarkChanged(); }
    partial void OnSelectedModeChanged(string value) { if (!_restoring) InvalidateDerived("模式已改变；请重新建立问题。 "); }
    partial void OnOffsetXChanged(int value) { if (!_restoring) InvalidateDerived("偏移已改变；请重新预检。 "); }
    partial void OnOffsetYChanged(int value) { if (!_restoring) InvalidateDerived("偏移已改变；请重新预检。 "); }
    partial void OnRmsToleranceChanged(double value) { if (!_restoring) InvalidateDerived("停止容差已改变；请重新建立运行状态。 "); }
    partial void OnMaxAbsToleranceChanged(double value) { if (!_restoring) InvalidateDerived("停止容差已改变；请重新建立运行状态。 "); }
    partial void OnMaxIterationsChanged(int value) { if (!_restoring) InvalidateDerived("最大迭代已改变；请重新建立运行状态。 "); }
    partial void OnPreviewIntervalChanged(int value) { if (!_restoring) MarkChanged(); }

    private async Task CommitProgressAsync(PoissonResidual residual, CancellationToken token)
    {
        Bitmap? poisson = null, alpha = null, heat = null;
        if (_session.CurrentSolution is not null)
        {
            poisson = await CreateBitmapAsync(_session.CurrentSolution, token).ConfigureAwait(false);
            alpha = await CreateBitmapAsync(_session.AlphaBaseline!, token).ConfigureAwait(false);
        }
        if (_session.Problem is not null && _session.SolverState is not null)
            heat = await CreateBitmapAsync(_residualProjector.Project(_session.Problem, _session.SolverState), token).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ReplacePoisson(poisson); ReplaceAlpha(alpha); ReplaceResidual(heat); Residuals = _session.SolverState!.History.ToArray();
            UpdateConvergence(); StatusMessage = _session.Result?.StopReason switch
            { PoissonStopReason.Converged => "双残差阈值均满足，结果已收敛。", PoissonStopReason.IterationLimit => "达到迭代上限；当前仅为未收敛预览。", _ => $"已完成 sweep {residual.Iteration}。" };
        });
    }

    private async Task RunGuardedAsync(string status, Func<CancellationToken, Task> operation)
    {
        if (_disposed) return; CancelAndDispose(); _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _operationCancellation; var token = current.Token; var generation = ++_generation; IsBusy = true; StatusMessage = status; var entered = false;
        try { await _gate.WaitAsync(token); entered = true; await operation(token); if (generation != _generation) return; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { if (!_lifetime.IsClosing && generation == _generation) StatusMessage = "操作已取消；未提交迟到结果。"; }
        catch (Exception exception) { if (generation == _generation) StatusMessage = exception.Message; }
        finally { if (entered) _gate.Release(); if (ReferenceEquals(current, _operationCancellation)) IsBusy = false; }
    }

    private async Task ExportImageAsync(bool alpha)
    {
        var path = alpha ? await _poissonDialog.PickPoissonAlphaPngAsync("poisson-alpha-baseline.png", _lifetime.ClosingToken)
            : await _poissonDialog.PickPoissonResultPngAsync("poisson-blending-result.png", _lifetime.ClosingToken);
        if (path is null) return;
        try { await _exportImage.ExecuteAsync(_session, path, alpha, false, _lifetime.ClosingToken); StatusMessage = "完整 PNG 已原子导出。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    private async Task ExportReportAsync(bool csv)
    {
        try
        {
            var path = csv ? await _poissonDialog.PickPoissonReportCsvAsync("poisson-blending-report.csv", _lifetime.ClosingToken)
                : await _poissonDialog.PickPoissonReportJsonAsync("poisson-blending-report.json", _lifetime.ClosingToken);
            if (path is null) return; await _exportReport.ExecuteAsync(_session.CreateReport(), path, csv, _lifetime.ClosingToken);
            StatusMessage = "报告已导出；不含绝对路径、像素、遮罩栅格、RHS、解或迭代帧。";
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    private PoissonBlendOptions ResolveOptions() => new PoissonBlendOptions(SelectedMode switch
    { "混合梯度" => PoissonBlendMode.MixedGradient, "单色融合" => PoissonBlendMode.Monochrome, _ => PoissonBlendMode.NormalClone },
        RmsTolerance, MaxAbsTolerance, MaxIterations, PreviewInterval).Validate();

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema) { StatusMessage = $"不支持快照 schema {content.SchemaVersion}；已使用安全默认值。"; return; }
        var value = content.Payload.Deserialize<Snapshot>(); if (value is null) return;
        SourcePath = string.Empty; TargetPath = string.Empty; RectangleLeft = Math.Max(0, value.RectangleLeft); RectangleTop = Math.Max(0, value.RectangleTop);
        RectangleWidth = Math.Max(0, value.RectangleWidth); RectangleHeight = Math.Max(0, value.RectangleHeight); OffsetX = value.OffsetX; OffsetY = value.OffsetY;
        SelectedMode = ModeOptions.Contains(value.Mode) ? value.Mode : "普通克隆"; RmsTolerance = value.RmsTolerance; MaxAbsTolerance = value.MaxAbsTolerance;
        MaxIterations = Math.Clamp(value.MaxIterations, 1, 2_000); PreviewInterval = PreviewIntervalOptions.Contains(value.PreviewInterval) ? value.PreviewInterval : 10;
        _strokes.Clear(); if (value.Strokes is { Length: <= PoissonMaskRasterizer.MaximumStrokes })
            _strokes.AddRange(value.Strokes.Where(item => { try { item.Validate(); return true; } catch { return false; } }));
        StatusMessage = $"已恢复 {value.SourceDisplayName ?? "源图"}/{value.TargetDisplayName ?? "目标图"} 的轻量意图；请显式重新选择图片，不会自动 IO 或求解。";
    }

    private async Task<Bitmap> CreateBitmapAsync(PixelImage image, CancellationToken token)
    { var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token); using var stream = new MemoryStream(bytes, false); return new Bitmap(stream); }
    private void UpdateConvergence()
    { var r = _session.SolverState?.History.LastOrDefault(); ConvergenceSummary = r is null ? "尚未迭代" : $"迭代 {r.Iteration}；RMS {r.Rms:E3}；MaxAbs {r.MaxAbs:E3}；相对 {r.RelativeRms:E3}"; }
    private void ClearDerived()
    { ReplacePoisson(null); ReplaceAlpha(null); ReplaceResidual(null); ReplaceGuidance(null); ReplaceRhs(null); Residuals = []; ResourceSummary = "尚未建立问题"; ConvergenceSummary = "尚未迭代"; }
    private void InvalidateDerived(string message) { _operationCancellation?.Cancel(); ClearDerived(); StatusMessage = message; MarkChanged(); NotifyFacts(); }
    private void NotifyFacts() { OnPropertyChanged(nameof(Topology)); OnPropertyChanged(nameof(PlacementOffset)); OnPropertyChanged(nameof(StrokeCount)); }
    private void MarkChanged() { if (_restoring) return; var dirty = IsDirty; _revision++; if (dirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }
    private void CancelAndDispose() { _operationCancellation?.Cancel(); _operationCancellation?.Dispose(); _operationCancellation = null; }
    private void ReplaceSource(Bitmap? value) { var old = SourcePreview; SourcePreview = value; if (!ReferenceEquals(old, value)) old?.Dispose(); }
    private void ReplaceTarget(Bitmap? value) { var old = TargetPreview; TargetPreview = value; if (!ReferenceEquals(old, value)) old?.Dispose(); }
    private void ReplacePoisson(Bitmap? value) { var old = PoissonPreview; PoissonPreview = value; if (!ReferenceEquals(old, value)) old?.Dispose(); }
    private void ReplaceAlpha(Bitmap? value) { var old = AlphaPreview; AlphaPreview = value; if (!ReferenceEquals(old, value)) old?.Dispose(); }
    private void ReplaceResidual(Bitmap? value) { var old = ResidualPreview; ResidualPreview = value; if (!ReferenceEquals(old, value)) old?.Dispose(); }
    private void ReplaceGuidance(Bitmap? value) { var old = GuidancePreview; GuidancePreview = value; if (!ReferenceEquals(old, value)) old?.Dispose(); }
    private void ReplaceRhs(Bitmap? value) { var old = RhsPreview; RhsPreview = value; if (!ReferenceEquals(old, value)) old?.Dispose(); }

    private sealed record Snapshot(string? SourceDisplayName, string? TargetDisplayName, int RectangleLeft, int RectangleTop,
        int RectangleWidth, int RectangleHeight, PoissonMaskStroke[]? Strokes, int OffsetX, int OffsetY, string Mode,
        double RmsTolerance, double MaxAbsTolerance, int MaxIterations, int PreviewInterval, string SnapshotProtocol, string NumericProtocol);
}
