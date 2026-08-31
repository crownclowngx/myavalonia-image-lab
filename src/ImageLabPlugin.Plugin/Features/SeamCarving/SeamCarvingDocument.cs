using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.SeamCarving;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.SeamCarving;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.SeamCarving;

/// <summary>“内容感知缩放”的多实例可持久化 Document。</summary>
/// <remarks>
/// Document 只协调窄用例、命令、轻量快照、generation、取消源和 Avalonia Bitmap。Sobel、动态规划、
/// 缝变形、参考缩放与报告都位于下层服务。新操作递增 generation；后台结果提交前核对 generation，
/// 关闭时先取消再释放 Bitmap/Session，避免迟到任务更新已关闭视图。
/// </remarks>
internal sealed partial class SeamCarvingDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private const int MaximumSnapshotBytes = 128 * 1024;
    private readonly SeamCarvingSession _session;
    private readonly IPrepareSeamCarvingSessionUseCase _prepare;
    private readonly IEditSeamMaskUseCase _editMask;
    private readonly IPlanSeamResizeUseCase _plan;
    private readonly IPreviewNextSeamUseCase _preview;
    private readonly IApplySeamStepUseCase _apply;
    private readonly IRunSeamPlaybackUseCase _playback;
    private readonly ICompareSeamResizeUseCase _compare;
    private readonly IExportSeamResultUseCase _exportResult;
    private readonly IExportSeamReportUseCase _exportReport;
    private readonly SeamEnergyPreviewProjector _energyProjector;
    private readonly SeamMaskPreviewProjector _maskProjector;
    private readonly IImageFileDialog _imageDialog;
    private readonly ISeamCarvingFileDialog _seamDialog;
    private readonly IImageCodec _codec;
    private readonly IDocumentLifetime _lifetime;
    private readonly List<SeamBrushStroke> _strokes = [];
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private DocumentPresentationState _presentation = new("内容感知缩放");
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _operationCancellation;
    private long _loadGeneration;
    private long _operationGeneration;
    private long _revision;
    private long _acceptedRevision;
    private bool _pauseRequested;
    private bool _restoring;
    private bool _disposed;

    public SeamCarvingDocument(
        SeamCarvingSession session,
        IPrepareSeamCarvingSessionUseCase prepare,
        IEditSeamMaskUseCase editMask,
        IPlanSeamResizeUseCase plan,
        IPreviewNextSeamUseCase preview,
        IApplySeamStepUseCase apply,
        IRunSeamPlaybackUseCase playback,
        ICompareSeamResizeUseCase compare,
        IExportSeamResultUseCase exportResult,
        IExportSeamReportUseCase exportReport,
        SeamEnergyPreviewProjector energyProjector,
        SeamMaskPreviewProjector maskProjector,
        IImageFileDialog imageDialog,
        ISeamCarvingFileDialog seamDialog,
        IImageCodec codec,
        IDocumentLifetime lifetime)
    {
        _session = session; _prepare = prepare; _editMask = editMask; _plan = plan; _preview = preview;
        _apply = apply; _playback = playback; _compare = compare; _exportResult = exportResult;
        _exportReport = exportReport; _energyProjector = energyProjector; _maskProjector = maskProjector; _imageDialog = imageDialog;
        _seamDialog = seamDialog; _codec = codec; _lifetime = lifetime;
    }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private int _targetWidth = 1;
    [ObservableProperty] private int _targetHeight = 1;
    [ObservableProperty] private string _selectedAxisOrder = "自动";
    [ObservableProperty] private string _selectedReferenceAlgorithm = "双线性";
    [ObservableProperty] private string _selectedBrush = "保护";
    [ObservableProperty] private int _brushRadius = 16;
    [ObservableProperty] private string _selectedEnergyDisplay = "线性";
    [ObservableProperty] private bool _showEffectiveEnergy = true;
    [ObservableProperty] private bool _showMaskOverlay = true;
    [ObservableProperty] private int _playbackDelayMilliseconds = 100;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "请选择一张图片；内容感知不等于语义理解。";
    [ObservableProperty] private string _resourceSummary = "尚未建立计划";
    [ObservableProperty] private string _stepSummary = "尚无步骤";
    [ObservableProperty] private string _comparisonSummary = "尚未比较；算法间差异不是质量排名。";
    [ObservableProperty] private Bitmap? _currentPreview;
    [ObservableProperty] private Bitmap? _energyPreview;
    [ObservableProperty] private Bitmap? _referencePreview;
    [ObservableProperty] private Bitmap? _differencePreview;
    [ObservableProperty] private IReadOnlyList<int> _seamCoordinates = Array.Empty<int>();
    [ObservableProperty] private SeamOrientation _seamOrientation;
    [ObservableProperty] private SeamOperation _seamOperation;

    public IReadOnlyList<string> AxisOrderOptions { get; } = ["自动", "宽优先", "高优先"];
    public IReadOnlyList<string> ReferenceAlgorithmOptions { get; } = ["双线性", "Catmull–Rom 双三次"];
    public IReadOnlyList<string> BrushOptions { get; } = ["保护", "优先删除", "擦除"];
    public IReadOnlyList<string> EnergyDisplayOptions { get; } = ["线性", "对数"];
    public IReadOnlyList<int> PlaybackDelayOptions { get; } = [50, 100, 250, 500];
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasSession => _session.InputImage is not null;
    public bool HasPlan => _session.Plan is not null;
    public bool HasCompletedResult => _session.HasCompletedResult;
    public int StrokeCount => _strokes.Count;
    public event EventHandler? PresentationChanged;
    public event EventHandler? IsDirtyChanged;

    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation); cancellationToken.ThrowIfCancellationRequested();
        _restoring = true;
        try
        {
            if (activation is RestoreDocumentActivation restore) Restore(restore.RestoredContent);
            _presentation = new(string.IsNullOrWhiteSpace(activation.Title) ? "内容感知缩放" : activation.Title);
            PresentationChanged?.Invoke(this, EventArgs.Empty); _revision = _acceptedRevision = 0;
        }
        finally { _restoring = false; }
        return ValueTask.CompletedTask;
    }

    [RelayCommand]
    private async Task SelectImageAsync()
    {
        var path = await _imageDialog.PickImageAsync(_lifetime.ClosingToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path)) SourcePath = path;
    }

    [RelayCommand]
    private async Task LoadImageAsync()
    {
        if (string.IsNullOrWhiteSpace(SourcePath)) { StatusMessage = "请先选择图片。"; return; }
        CancelAndDispose(ref _operationCancellation); ++_operationGeneration;
        CancelAndDispose(ref _loadCancellation);
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _loadCancellation; var token = current.Token; var generation = ++_loadGeneration;
        IsBusy = true; StatusMessage = "正在解码并校验 200 万像素工作预算…"; var entered = false;
        try
        {
            await _sessionGate.WaitAsync(token).ConfigureAwait(true); entered = true;
            await _prepare.ExecuteAsync(_session, SourcePath, token).ConfigureAwait(true);
            var bitmap = await CreateBitmapAsync(_session.InputImage!, token).ConfigureAwait(true);
            if (!CanCommitLoad(generation)) { bitmap.Dispose(); return; }
            _strokes.Clear(); TargetWidth = _session.InputImage!.Size.Width; TargetHeight = _session.InputImage.Size.Height;
            ReplaceCurrentPreview(bitmap); ReplaceEnergyPreview(null); ReplaceReferencePreview(null); ReplaceDifferencePreview(null);
            SeamCoordinates = []; StepSummary = "图片已载入；请设置目标尺寸并可选绘制区域。";
            ResourceSummary = $"输入 {_session.InputImage.Size.Width}×{_session.InputImage.Size.Height}，{_session.InputImage.Size.PixelCount:N0} 像素";
            StatusMessage = "载入完成；绘图不会自动执行算法。"; NotifyCapabilities(); MarkChanged();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { if (!_lifetime.IsClosing && generation == _loadGeneration) StatusMessage = "载入已取消。"; }
        catch (Exception exception) { if (generation == _loadGeneration) StatusMessage = exception.Message; }
        finally { if (entered) _sessionGate.Release(); if (ReferenceEquals(current, _loadCancellation)) IsBusy = false; }
    }

    /// <summary>由画布在指针释放时提交一条有界归一化笔划。</summary>
    internal void AddStroke(IReadOnlyList<SeamNormalizedPoint> points)
    {
        if (IsBusy) { StatusMessage = "当前步骤尚未结束，请先暂停或取消后再绘制。"; return; }
        if (_session.InputImage is null) { StatusMessage = "请先载入图片再绘制区域。"; return; }
        if (_strokes.Count >= SeamMaskRasterizer.MaximumStrokes)
        { StatusMessage = $"笔划已达到 {SeamMaskRasterizer.MaximumStrokes} 上限，请先清空或合并绘制。"; return; }
        try
        {
            var tool = SelectedBrush switch
            { "优先删除" => SeamBrushTool.PreferRemoval, "擦除" => SeamBrushTool.Erase, _ => SeamBrushTool.Protect };
            var normalizedRadius = Math.Clamp(BrushRadius /
                (double)Math.Min(_session.InputImage.Size.Width, _session.InputImage.Size.Height), 0.001d, 0.25d);
            // 先完整验证再加入集合；失败笔划不能污染后续重放或快照。
            var stroke = new SeamBrushStroke(tool, normalizedRadius, points.ToArray(), _strokes.Count).Validate();
            _strokes.Add(stroke);
            _editMask.Apply(_session, _strokes);
            _ = RefreshCurrentPreviewAsync(); ClearDerivedPreviews(); StatusMessage = $"已重放 {_strokes.Count} 条笔划；旧计划和结果已过期。";
            NotifyCapabilities(); MarkChanged();
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand]
    private void ClearStrokes()
    {
        if (IsBusy) { StatusMessage = "当前步骤尚未结束，请先暂停或取消后再清空笔划。"; return; }
        if (_session.InputImage is null) return;
        _strokes.Clear(); _editMask.Apply(_session, _strokes); _session.SetState(SeamPlaybackState.Ready);
        _ = RefreshCurrentPreviewAsync(); ClearDerivedPreviews(); StatusMessage = "区域笔划已清空；旧计划和结果已过期。"; MarkChanged();
    }

    [RelayCommand]
    private void PlanResize()
    {
        if (IsBusy) { StatusMessage = "当前步骤尚未结束，请先暂停或取消后再建立计划。"; return; }
        try
        {
            if (_session.InputImage is null) { StatusMessage = "请先载入图片。"; return; }
            var request = new SeamResizeRequest(new ImageSize(TargetWidth, TargetHeight), ResolveAxisOrder(), ResolveReferenceAlgorithm());
            var plan = _plan.Execute(_session, request);
            var estimate = plan.ResourceEstimate;
            ResourceSummary = $"{estimate.TotalSeams} 缝；估算访问 {estimate.EstimatedCellVisits:N0}；峰值 {estimate.EstimatedPeakBytes / 1024d / 1024d:F1} MiB";
            StepSummary = plan.Steps.Count == 0 ? "目标未变化：结果为输入的独立克隆。" : $"计划 0/{plan.Steps.Count}；等待预览下一缝。";
            SeamCoordinates = []; ReplaceEnergyPreview(null); ReplaceReferencePreview(null); ReplaceDifferencePreview(null);
            StatusMessage = "计划已通过资源门禁；可以预览、单步或播放。"; NotifyCapabilities(); MarkChanged();
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand]
    private async Task PreviewNextAsync() => await RunOperationAsync("正在计算 Sobel 能量与下一条确定性最小缝…",
        token => _preview.ExecuteAsync(_session, token), async value =>
        {
            if (value is null) { StatusMessage = "计划已完成，没有下一条缝。"; return; }
            var energyImage = _energyProjector.Project(value.Energy, ShowEffectiveEnergy,
                SelectedEnergyDisplay == "对数" ? EnergyDisplayMode.Logarithmic : EnergyDisplayMode.Linear);
            ReplaceEnergyPreview(await CreateBitmapAsync(energyImage, _lifetime.ClosingToken).ConfigureAwait(true));
            SeamCoordinates = value.Path.Coordinates.ToArray(); SeamOrientation = value.Path.Orientation; SeamOperation = value.Operation;
            StepSummary = $"{value.StepNumber}/{value.TotalSteps} {Describe(value.Path.Orientation)}{Describe(value.Operation)}；" +
                $"基础 {value.Path.BaseEnergy:F4}，有效 {value.Path.EffectiveEnergy:F4}，保护命中 {value.Path.ProtectHits}，优先删除命中 {value.Path.PreferRemovalHits}";
            StatusMessage = "下一缝仅预览，工作图尚未修改。";
        }).ConfigureAwait(true);

    [RelayCommand]
    private async Task StepAsync() => await RunOperationAsync("正在应用且只应用一条已验证缝…",
        token => _apply.ExecuteAsync(_session, token), async value =>
        {
            if (value is null) { StatusMessage = "没有可应用的步骤。"; return; }
            ReplaceCurrentPreview(await CreateCurrentBitmapAsync(_lifetime.ClosingToken).ConfigureAwait(true));
            SeamCoordinates = []; ReplaceEnergyPreview(null); StepSummary = $"已完成 {value.StepNumber}/{_session.Plan!.Steps.Count}；当前 {_session.CurrentImage!.Size.Width}×{_session.CurrentImage.Size.Height}";
            StatusMessage = _session.HasCompletedResult ? "计划完成；可生成普通缩放对照或导出。" : "单步完成；可继续预览。";
            NotifyCapabilities();
        }).ConfigureAwait(true);

    [RelayCommand]
    private async Task PlayAsync()
    {
        _pauseRequested = false;
        await RunOperationAsync("正在播放；速度只控制提交节拍，不改变算法路径…",
            async token =>
            {
                await _playback.ExecuteAsync(_session, async record =>
                {
                    var bitmap = await CreateCurrentBitmapAsync(token).ConfigureAwait(false);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ReplaceCurrentPreview(bitmap);
                        SeamCoordinates = []; ReplaceEnergyPreview(null);
                        StepSummary = $"已完成 {record.StepNumber}/{_session.Plan!.Steps.Count}；当前 {_session.CurrentImage!.Size.Width}×{_session.CurrentImage.Size.Height}";
                    });
                    await Task.Delay(PlaybackDelayMilliseconds, token).ConfigureAwait(false);
                }, () => _pauseRequested, token).ConfigureAwait(false);
                return true;
            }, _ =>
            {
                StatusMessage = _session.HasCompletedResult ? "播放完成；可比较或导出。" : "已暂停，不会启动下一步骤。";
                NotifyCapabilities(); return Task.CompletedTask;
            }).ConfigureAwait(true);
    }

    [RelayCommand] private void Pause() { _pauseRequested = true; StatusMessage = "已请求暂停；当前小阶段完成后生效。"; }
    [RelayCommand] private void Cancel() { _operationCancellation?.Cancel(); StatusMessage = "已请求取消；迟到结果不会提交。"; }

    [RelayCommand]
    private async Task ResetAsync()
    {
        if (_session.InputImage is null) return;
        if (IsBusy) { Cancel(); StatusMessage = "已取消当前操作；请在取消完成后再次点击重置。"; return; }
        _editMask.Apply(_session, _strokes); _session.SetState(SeamPlaybackState.Ready);
        ReplaceCurrentPreview(await CreateCurrentBitmapAsync(_lifetime.ClosingToken).ConfigureAwait(true));
        ClearDerivedPreviews(); StepSummary = "已重置到输入和当前笔划；未自动重跑计划。";
        StatusMessage = "重置完成。"; NotifyCapabilities();
    }

    [RelayCommand]
    private async Task CompareAsync() => await RunOperationAsync("正在生成规则网格参考结果和算法间差异…",
        token => _compare.ExecuteAsync(_session, token), async value =>
        {
            ReplaceReferencePreview(await CreateBitmapAsync(value.ReferenceImage, _lifetime.ClosingToken).ConfigureAwait(true));
            ReplaceDifferencePreview(await CreateBitmapAsync(value.DifferenceImage, _lifetime.ClosingToken).ConfigureAwait(true));
            var metric = value.SeamVsReference;
            ComparisonSummary = $"seamVsReference：MAE RGB {metric.MeanAbsoluteErrorRgb:F3}；RMSE {metric.RootMeanSquareErrorRgb:F3}；" +
                $"PSNR-RGB {FormatPsnr(metric.PsnrRgbDb)}；SSIM-Y {metric.GlobalSsimLuma:F4}。仅表示算法间差异。";
            StatusMessage = "普通缩放对照完成；这些指标不是质量排名。";
        }).ConfigureAwait(true);

    [RelayCommand]
    private async Task ExportPngAsync()
    {
        var path = await _seamDialog.PickSeamResultPngAsync("seam-carving-result.png", _lifetime.ClosingToken).ConfigureAwait(true);
        if (path is null) return;
        try { await _exportResult.ExecuteAsync(_session, path, _lifetime.ClosingToken).ConfigureAwait(true); StatusMessage = "完整 PNG 已原子导出，未覆盖源文件。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand] private Task ExportJsonAsync() => ExportReportAsync(csv: false);
    [RelayCommand] private Task ExportCsvAsync() => ExportReportAsync(csv: true);

    private async Task ExportReportAsync(bool csv)
    {
        try
        {
            var report = _session.CreateReport();
            var path = csv
                ? await _seamDialog.PickSeamReportCsvAsync("seam-carving-report.csv", _lifetime.ClosingToken).ConfigureAwait(true)
                : await _seamDialog.PickSeamReportJsonAsync("seam-carving-report.json", _lifetime.ClosingToken).ConfigureAwait(true);
            if (path is null) return;
            await _exportReport.ExecuteAsync(report, path, csv, _lifetime.ClosingToken).ConfigureAwait(true);
            StatusMessage = "实验报告已原子导出；不含绝对路径、像素、能量矩阵或蒙版栅格。";
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_strokes.Count > SeamMaskRasterizer.MaximumStrokes) throw new InvalidOperationException("笔划数超过快照上限。");
        var snapshot = new Snapshot(SourcePath, TargetWidth, TargetHeight, SelectedAxisOrder,
            SelectedReferenceAlgorithm, SelectedBrush, BrushRadius, SelectedEnergyDisplay, ShowEffectiveEnergy,
            PlaybackDelayMilliseconds, ShowMaskOverlay, _strokes.ToArray(), SeamCarvingProtocols.SnapshotSchema,
            SeamCarvingProtocols.Energy, SeamCarvingProtocols.Budget);
        var payload = JsonSerializer.SerializeToElement(snapshot);
        var bytes = Encoding.UTF8.GetByteCount(payload.GetRawText());
        if (bytes > MaximumSnapshotBytes)
            throw new InvalidOperationException($"快照 {bytes:N0} 字节超过 {MaximumSnapshotBytes:N0} 上限；请清理或简化笔划。");
        return ValueTask.FromResult(new DocumentSaveSnapshot(new DocumentRevision(_revision),
            new DocumentContent(SnapshotSchema, payload)));
    }

    public void AcceptChanges(DocumentRevision savedRevision)
    {
        var dirty = IsDirty; if (savedRevision.Value == _revision) _acceptedRevision = _revision;
        if (dirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_loadGeneration; ++_operationGeneration;
        CancelAndDispose(ref _loadCancellation); CancelAndDispose(ref _operationCancellation);
        ReplaceCurrentPreview(null); ReplaceEnergyPreview(null); ReplaceReferencePreview(null); ReplaceDifferencePreview(null);
        _session.Dispose();
    }

    partial void OnSourcePathChanged(string value) { if (!_restoring) MarkChanged(); }
    partial void OnTargetWidthChanged(int value) { if (!_restoring) InvalidatePlan(); }
    partial void OnTargetHeightChanged(int value) { if (!_restoring) InvalidatePlan(); }
    partial void OnSelectedAxisOrderChanged(string value) { if (!_restoring) InvalidatePlan(); }
    partial void OnSelectedReferenceAlgorithmChanged(string value) { if (!_restoring) InvalidatePlan(); }
    partial void OnSelectedEnergyDisplayChanged(string value) { if (!_restoring) RefreshEnergyDisplay(); }
    partial void OnShowEffectiveEnergyChanged(bool value) { if (!_restoring) RefreshEnergyDisplay(); }
    partial void OnShowMaskOverlayChanged(bool value)
    { if (!_restoring) { if (!IsBusy) _ = RefreshCurrentPreviewAsync(); MarkChanged(); } }
    partial void OnSelectedBrushChanged(string value) { if (!_restoring) MarkChanged(); }
    partial void OnBrushRadiusChanged(int value)
    { if (value is < 1 or > 256) { BrushRadius = Math.Clamp(value, 1, 256); return; } if (!_restoring) MarkChanged(); }
    partial void OnPlaybackDelayMillisecondsChanged(int value)
    { if (!PlaybackDelayOptions.Contains(value)) { PlaybackDelayMilliseconds = 100; return; } if (!_restoring) MarkChanged(); }

    private async Task RunOperationAsync<T>(string status, Func<CancellationToken, Task<T>> operation, Func<T, Task> commit)
    {
        CancelAndDispose(ref _operationCancellation);
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _operationCancellation; var token = current.Token; var generation = ++_operationGeneration;
        IsBusy = true; StatusMessage = status; var entered = false;
        try
        {
            await _sessionGate.WaitAsync(token).ConfigureAwait(true); entered = true;
            var value = await operation(token).ConfigureAwait(true);
            if (generation == _operationGeneration && !_disposed && !_lifetime.IsClosing) await commit(value).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { if (!_lifetime.IsClosing && generation == _operationGeneration) StatusMessage = "操作已取消；未提交迟到结果。"; }
        catch (Exception exception) { if (generation == _operationGeneration) StatusMessage = exception.Message; }
        finally { if (entered) _sessionGate.Release(); if (ReferenceEquals(current, _operationCancellation)) IsBusy = false; }
    }

    private void InvalidatePlan()
    {
        if (IsBusy) { _operationCancellation?.Cancel(); StatusMessage = "参数已改变，正在取消旧操作；请稍后重新建立计划。"; MarkChanged(); return; }
        if (_session.InputImage is not null && _session.Plan is not null)
        { _session.SetState(SeamPlaybackState.Stale); ClearDerivedPreviews(); StatusMessage = "参数已改变；旧计划和结果过期，请重新建立计划。"; NotifyCapabilities(); }
        MarkChanged();
    }

    private void RefreshEnergyDisplay()
    {
        if (_session.Preview is null) { MarkChanged(); return; }
        _ = RefreshEnergyDisplayAsync(); MarkChanged();
    }

    private async Task RefreshEnergyDisplayAsync()
    {
        try
        {
            var image = _energyProjector.Project(_session.Preview!.Energy, ShowEffectiveEnergy,
                SelectedEnergyDisplay == "对数" ? EnergyDisplayMode.Logarithmic : EnergyDisplayMode.Linear);
            ReplaceEnergyPreview(await CreateBitmapAsync(image, _lifetime.ClosingToken).ConfigureAwait(true));
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    private void ClearDerivedPreviews()
    {
        SeamCoordinates = []; ReplaceEnergyPreview(null); ReplaceReferencePreview(null); ReplaceDifferencePreview(null);
        ComparisonSummary = "尚未比较；算法间差异不是质量排名。";
    }

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema) { StatusMessage = $"不支持 schema {content.SchemaVersion}，已使用安全默认值。"; return; }
        var value = content.Payload.Deserialize<Snapshot>(); if (value is null) return;
        SourcePath = value.SourcePath ?? string.Empty; TargetWidth = Math.Max(1, value.TargetWidth); TargetHeight = Math.Max(1, value.TargetHeight);
        SelectedAxisOrder = AxisOrderOptions.Contains(value.AxisOrder) ? value.AxisOrder : "自动";
        SelectedReferenceAlgorithm = ReferenceAlgorithmOptions.Contains(value.ReferenceAlgorithm) ? value.ReferenceAlgorithm : "双线性";
        SelectedBrush = BrushOptions.Contains(value.Brush) ? value.Brush : "保护"; BrushRadius = Math.Clamp(value.BrushRadius, 1, 256);
        SelectedEnergyDisplay = EnergyDisplayOptions.Contains(value.EnergyDisplay) ? value.EnergyDisplay : "线性";
        ShowEffectiveEnergy = value.ShowEffectiveEnergy;
        PlaybackDelayMilliseconds = PlaybackDelayOptions.Contains(value.PlaybackDelayMilliseconds) ? value.PlaybackDelayMilliseconds : 100;
        ShowMaskOverlay = value.ShowMaskOverlay;
        _strokes.Clear();
        if (value.Strokes is { Length: <= SeamMaskRasterizer.MaximumStrokes })
            _strokes.AddRange(value.Strokes.Where(item => { try { item.Validate(); return true; } catch { return false; } }));
        StatusMessage = "已恢复路径文本、参数和有界笔划；不会自动读取图片、栅格化蒙版或运行算法。";
    }

    private async Task<Bitmap> CreateBitmapAsync(PixelImage image, CancellationToken token)
    {
        var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, writable: false); return new Bitmap(stream);
    }

    private Task<Bitmap> CreateCurrentBitmapAsync(CancellationToken token)
    {
        var image = _session.CurrentImage ?? throw new InvalidOperationException("当前没有工作图。");
        // 完成结果始终显示干净像素；运行期间可用纹理叠加观察同步变形后的三态蒙版。
        var preview = ShowMaskOverlay && !_session.HasCompletedResult && _session.CurrentMask is not null
            ? _maskProjector.Project(image, _session.CurrentMask, token) : image;
        return CreateBitmapAsync(preview, token);
    }

    private async Task RefreshCurrentPreviewAsync()
    {
        if (_session.CurrentImage is null) return;
        try { ReplaceCurrentPreview(await CreateCurrentBitmapAsync(_lifetime.ClosingToken).ConfigureAwait(true)); }
        catch (OperationCanceledException) when (_lifetime.ClosingToken.IsCancellationRequested) { }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    private SeamAxisOrder ResolveAxisOrder() => SelectedAxisOrder switch
    { "宽优先" => SeamAxisOrder.WidthFirst, "高优先" => SeamAxisOrder.HeightFirst, _ => SeamAxisOrder.Auto };
    private ReferenceResizeAlgorithm ResolveReferenceAlgorithm() =>
        SelectedReferenceAlgorithm == "Catmull–Rom 双三次" ? ReferenceResizeAlgorithm.BicubicCatmullRom : ReferenceResizeAlgorithm.Bilinear;
    private static string Describe(SeamOrientation value) => value == SeamOrientation.Vertical ? "垂直" : "水平";
    private static string Describe(SeamOperation value) => value == SeamOperation.Remove ? "删除" : "插入";
    private static string FormatPsnr(double value) => double.IsPositiveInfinity(value) ? "∞" : $"{value:F2} dB";
    private bool CanCommitLoad(long generation) => generation == _loadGeneration && !_disposed && !_lifetime.IsClosing;
    private void ReplaceCurrentPreview(Bitmap? value) { var old = CurrentPreview; CurrentPreview = value; old?.Dispose(); }
    private void ReplaceEnergyPreview(Bitmap? value) { var old = EnergyPreview; EnergyPreview = value; old?.Dispose(); }
    private void ReplaceReferencePreview(Bitmap? value) { var old = ReferencePreview; ReferencePreview = value; old?.Dispose(); }
    private void ReplaceDifferencePreview(Bitmap? value) { var old = DifferencePreview; DifferencePreview = value; old?.Dispose(); }
    private static void CancelAndDispose(ref CancellationTokenSource? value) { value?.Cancel(); value?.Dispose(); value = null; }
    private void NotifyCapabilities()
    { OnPropertyChanged(nameof(HasSession)); OnPropertyChanged(nameof(HasPlan)); OnPropertyChanged(nameof(HasCompletedResult)); OnPropertyChanged(nameof(StrokeCount)); }
    private void MarkChanged()
    { if (_restoring) return; var dirty = IsDirty; _revision++; if (dirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }

    private sealed record Snapshot(string? SourcePath, int TargetWidth, int TargetHeight, string AxisOrder,
        string ReferenceAlgorithm, string Brush, int BrushRadius, string EnergyDisplay, bool ShowEffectiveEnergy,
        int PlaybackDelayMilliseconds, bool ShowMaskOverlay, SeamBrushStroke[]? Strokes, string SnapshotProtocol,
        string EnergyProtocol, string BudgetProtocol);
}
