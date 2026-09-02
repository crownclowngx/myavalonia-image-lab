using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.MagnitudePhaseSwap;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.MagnitudePhaseSwap;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.MagnitudePhaseSwap;

/// <summary>幅相交换多实例 Document：只管理参数、命令、generation、Bitmap 与生命周期。</summary>
/// <remarks>
/// 规范化、FFT、混合、IFFT、投影、指标和 JSON 全在 Domain/Application/Infrastructure。Document 只从
/// 强类型预设构造合法配方，并以 Session generation 拒绝迟到候选；每张 Bitmap 由当前实例独占，替换和关闭
/// 均立即释放。这样 UI 状态、数值算法和外部文件边界各自只有一个变化原因，保持 SOLID 的朴素落地。
/// </remarks>
internal sealed partial class MagnitudePhaseSwapDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private readonly IPrepareMagnitudePhasePairUseCase _prepare;
    private readonly IRenderMagnitudePhaseExperimentUseCase _render;
    private readonly IInspectMagnitudePhasePointUseCase _inspectPoint;
    private readonly IExportMagnitudePhaseImageUseCase _exportImage;
    private readonly IImportMagnitudePhaseRecipeUseCase _importRecipe;
    private readonly IExportMagnitudePhaseRecipeUseCase _exportRecipe;
    private readonly IExportMagnitudePhaseReportUseCase _exportReport;
    private readonly IMagnitudePhaseSnapshotSerializer _snapshotSerializer;
    private readonly IMagnitudePhaseFileDialog _dialog;
    private readonly IImageCodec _codec;
    private readonly IDocumentLifetime _lifetime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _operationCancellation;
    private MagnitudePhaseSession? _session;
    private MagnitudePhaseRenderResult? _result;
    private string? _expectedFingerprintA;
    private string? _expectedFingerprintB;
    private DocumentPresentationState _presentation = new("幅度与相位交换");
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;
    private bool _disposed;

    public MagnitudePhaseSwapDocument(IPrepareMagnitudePhasePairUseCase prepare,
        IRenderMagnitudePhaseExperimentUseCase render, IInspectMagnitudePhasePointUseCase inspectPoint,
        IExportMagnitudePhaseImageUseCase exportImage,
        IImportMagnitudePhaseRecipeUseCase importRecipe, IExportMagnitudePhaseRecipeUseCase exportRecipe,
        IExportMagnitudePhaseReportUseCase exportReport, IMagnitudePhaseSnapshotSerializer snapshotSerializer,
        IMagnitudePhaseFileDialog dialog, IImageCodec codec, IDocumentLifetime lifetime)
    {
        _prepare = prepare; _render = render; _inspectPoint = inspectPoint; _exportImage = exportImage; _importRecipe = importRecipe;
        _exportRecipe = exportRecipe; _exportReport = exportReport; _snapshotSerializer = snapshotSerializer;
        _dialog = dialog; _codec = codec; _lifetime = lifetime;
    }

    public ObservableCollection<int> CanvasSizes { get; } = [256, 512, 1024];
    public ObservableCollection<string> Presets { get; } =
    [
        "A 幅度 + B 相位", "B 幅度 + A 相位", "A 幅度-only", "B 幅度-only",
        "A 相位-only", "B 相位-only", "幅度 A→B（相位 A）", "幅度 A→B（相位 B）",
        "相位 A→B（幅度 A）", "相位 A→B（幅度 B）"
    ];

    [ObservableProperty] private string _pathA = string.Empty;
    [ObservableProperty] private string _pathB = string.Empty;
    [ObservableProperty] private int _canvasSize = 512;
    [ObservableProperty] private string _selectedPreset = "A 幅度 + B 相位";
    [ObservableProperty] private double _amount = .5d;
    [ObservableProperty] private string _selectedPage = "联动总览";
    [ObservableProperty] private int _selectedPageIndex;
    [ObservableProperty] private bool _synchronizedZoom = true;
    [ObservableProperty] private bool _metricsVisible = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "请选择图像 A 和 B，然后准备共同规范画布。";
    [ObservableProperty] private string _metricsSummary = "尚无当前结果。";
    [ObservableProperty] private string _diagnosticLabel = string.Empty;
    [ObservableProperty] private string _probeSummary = "在任一频谱上移动指针以查看 A/B/Result 同频点。";
    private Bitmap? _canvasAPreview;
    private Bitmap? _canvasBPreview;
    private Bitmap? _magnitudeAPreview;
    private Bitmap? _magnitudeBPreview;
    private Bitmap? _phaseAPreview;
    private Bitmap? _phaseBPreview;
    private Bitmap? _resultPreview;
    private Bitmap? _resultMagnitudePreview;
    private Bitmap? _resultPhasePreview;

    public Bitmap? CanvasAPreview => _canvasAPreview;
    public Bitmap? CanvasBPreview => _canvasBPreview;
    public Bitmap? MagnitudeAPreview => _magnitudeAPreview;
    public Bitmap? MagnitudeBPreview => _magnitudeBPreview;
    public Bitmap? PhaseAPreview => _phaseAPreview;
    public Bitmap? PhaseBPreview => _phaseBPreview;
    public Bitmap? ResultPreview => _resultPreview;
    public Bitmap? ResultMagnitudePreview => _resultMagnitudePreview;
    public Bitmap? ResultPhasePreview => _resultPhasePreview;
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasInputs => _session is not null;
    public bool HasResult => _result is not null;
    public bool CanExport => _result is not null && ReferenceEquals(_session?.CurrentResult, _result);
    public bool UsesInterpolation => SelectedPreset.StartsWith("幅度 A→B", StringComparison.Ordinal) ||
                                     SelectedPreset.StartsWith("相位 A→B", StringComparison.Ordinal);

    public event EventHandler? PresentationChanged;
    public event EventHandler? IsDirtyChanged;

    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation); cancellationToken.ThrowIfCancellationRequested();
        _restoring = true;
        try
        {
            if (activation is RestoreDocumentActivation restore) Restore(restore.RestoredContent);
            _presentation = new DocumentPresentationState(string.IsNullOrWhiteSpace(activation.Title)
                ? "幅度与相位交换" : activation.Title);
            _revision = _acceptedRevision = 0;
            PresentationChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _restoring = false; }
        return ValueTask.CompletedTask;
    }

    [RelayCommand] private async Task SelectAAsync() { var path = await _dialog.PickMagnitudePhaseInputAsync("A", _lifetime.ClosingToken); if (path is not null) PathA = path; }
    [RelayCommand] private async Task SelectBAsync() { var path = await _dialog.PickMagnitudePhaseInputAsync("B", _lifetime.ClosingToken); if (path is not null) PathB = path; }

    [RelayCommand]
    private void SwapInputs()
    {
        (PathA, PathB) = (PathB, PathA);
        (_expectedFingerprintA, _expectedFingerprintB) = (_expectedFingerprintB, _expectedFingerprintA);
        InvalidateInputs("A/B 角色已交换；请重新准备。旧频谱不会跨 generation 复用。");
    }

    [RelayCommand]
    private Task PrepareAsync() => RunGuardedAsync("正在白底合成、建立规范画布并各执行一次 FFT…", async token =>
    {
        if (string.IsNullOrWhiteSpace(PathA) || string.IsNullOrWhiteSpace(PathB))
            throw new InvalidOperationException("请先选择图像 A 和 B。");
        var candidate = await _prepare.ExecuteAsync(new PrepareMagnitudePhasePairRequest(PathA, PathB, CanvasSize), token).ConfigureAwait(false);
        if ((_expectedFingerprintA is not null && candidate.FingerprintA != _expectedFingerprintA) ||
            (_expectedFingerprintB is not null && candidate.FingerprintB != _expectedFingerprintB))
        {
            candidate.Dispose();
            throw new InvalidDataException("当前 A/B 规范内容指纹与导入配方不匹配；未提交频谱。");
        }
        var bitmaps = await CreateSourceBitmapsAsync(candidate, token).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested || _disposed) { candidate.Dispose(); DisposeAll(bitmaps); return; }
            ReplaceSession(candidate); CommitSourceBitmaps(bitmaps);
            _expectedFingerprintA = _expectedFingerprintB = null;
            StatusMessage = $"双输入已准备：{CanvasSize}×{CanvasSize}；A 内容 {candidate.CanvasA.Content.CoverageRatio(CanvasSize):P1}，B 内容 {candidate.CanvasB.Content.CoverageRatio(CanvasSize):P1}。";
            OnPropertyChanged(nameof(HasInputs));
        });
    });

    [RelayCommand]
    private Task RunExperimentAsync() => RunGuardedAsync("正在组合共轭安全频谱、执行 IFFT 与指标扫描…", async token =>
    {
        var session = _session ?? throw new InvalidOperationException("请先准备双输入。");
        var recipe = CreateRecipe();
        var generation = session.AdvanceGeneration();
        var candidate = await _render.ExecuteAsync(session, recipe, generation, token).ConfigureAwait(false);
        var bitmaps = await CreateResultBitmapsAsync(candidate, token).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested || !ReferenceEquals(session, _session) ||
                !session.TryCommit(candidate, generation, recipe.Fingerprint())) { DisposeAll(bitmaps); return; }
            _result = candidate; CommitResultBitmaps(bitmaps);
            DiagnosticLabel = candidate.DiagnosticLabel ?? string.Empty;
            MetricsSummary = Describe(candidate.Diagnostics);
            StatusMessage = $"结果已提交：配方 {candidate.RecipeFingerprint}；耗时 {candidate.Elapsed.TotalMilliseconds:F0} ms。";
            OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(CanExport));
        });
    });

    [RelayCommand] private void Cancel() { _operationCancellation?.Cancel(); StatusMessage = "已请求取消；最后有效结果保持不变。"; }

    internal void UpdateProbe(double normalizedX, double normalizedY)
    {
        if (_session is null || _result is null) return;
        var x = Math.Clamp((int)Math.Floor(normalizedX * CanvasSize), 0, CanvasSize - 1);
        var y = Math.Clamp((int)Math.Floor(normalizedY * CanvasSize), 0, CanvasSize - 1);
        var point = _inspectPoint.Execute(_session, _result.Recipe, x, y);
        ProbeSummary = $"显示 ({point.DisplayX},{point.DisplayY}) / k=({point.CenteredKx},{point.CenteredKy})" +
            $"{(point.IsSelfConjugate ? "，自共轭" : string.Empty)}；" +
            $"A M={point.MagnitudeA:E3}, φ={Phase(point.PhaseA)}；B M={point.MagnitudeB:E3}, φ={Phase(point.PhaseB)}；" +
            $"Result M={point.ResultMagnitude:E3}, φ={Phase(point.ResultPhase)}";
    }

    [RelayCommand]
    private async Task ImportRecipeAsync()
    {
        var path = await _dialog.PickMagnitudePhaseRecipeInputAsync(_lifetime.ClosingToken); if (path is null) return;
        await RunGuardedAsync("正在严格导入配方…", async token =>
        {
            var imported = await _importRecipe.ExecuteAsync(path, token).ConfigureAwait(false);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyRecipe(imported.Recipe);
                _expectedFingerprintA = imported.FingerprintA; _expectedFingerprintB = imported.FingerprintB;
                InvalidateInputs($"配方已导入（期望 A {imported.FingerprintA} / B {imported.FingerprintB}）；请显式重新选择并核对输入。");
            });
        });
    }

    [RelayCommand]
    private async Task ExportRecipeAsync()
    {
        if (_session is null) { StatusMessage = "请先准备输入。"; return; }
        var path = await _dialog.PickMagnitudePhaseRecipeOutputAsync("magnitude-phase-recipe.json", _lifetime.ClosingToken); if (path is null) return;
        try { await _exportRecipe.ExecuteAsync(CreateRecipe(), _session, path, _lifetime.ClosingToken); StatusMessage = "配方已原子导出，不含路径或像素。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand]
    private async Task ExportImageAsync()
    {
        if (_session is null || _result is null) { StatusMessage = "没有当前结果。"; return; }
        var path = await _dialog.PickMagnitudePhaseResultPngAsync("magnitude-phase-result.png", _lifetime.ClosingToken); if (path is null) return;
        try { await _exportImage.ExecuteAsync(_session, _result, path, _lifetime.ClosingToken); StatusMessage = "PNG 已完成内存与真实目标回读。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand] private Task ExportReportJsonAsync() => ExportReportAsync(false);
    [RelayCommand] private Task ExportReportCsvAsync() => ExportReportAsync(true);

    private async Task ExportReportAsync(bool csv)
    {
        if (_session is null || _result is null) { StatusMessage = "没有当前诊断结果。"; return; }
        var path = csv ? await _dialog.PickMagnitudePhaseReportCsvAsync("magnitude-phase-report.csv", _lifetime.ClosingToken)
            : await _dialog.PickMagnitudePhaseReportJsonAsync("magnitude-phase-report.json", _lifetime.ClosingToken);
        if (path is null) return;
        var report = new MagnitudePhaseReport(MagnitudePhaseProtocol.Report, MagnitudePhaseProtocol.Schema,
            _session.FingerprintA, _session.FingerprintB, _result.RecipeFingerprint, _result.Recipe,
            _result.Diagnostics, (long)_result.Elapsed.TotalMilliseconds, "1.0.0",
            "空间相似性和供体误差是描述性指标，不证明相位或幅度对结构的普遍因果贡献。");
        try { await _exportReport.ExecuteAsync(report, _session, path, csv, _lifetime.ClosingToken); StatusMessage = "脱敏报告已原子导出。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = new MagnitudePhaseSnapshotState(Path.GetFileName(PathA), Path.GetFileName(PathB), CanvasSize,
            SelectedPreset, UsesInterpolation ? Amount : 0d, SelectedPage, SynchronizedZoom, MetricsVisible,
            MagnitudePhaseProtocol.SnapshotSchema);
        return ValueTask.FromResult(new DocumentSaveSnapshot(new DocumentRevision(_revision),
            new DocumentContent(MagnitudePhaseProtocol.SnapshotSchema, _snapshotSerializer.Serialize(state))));
    }

    public void AcceptChanges(DocumentRevision savedRevision)
    {
        var dirty = IsDirty; if (savedRevision.Value == _revision) _acceptedRevision = _revision;
        if (dirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; CancelOperation(); _session?.Dispose(); _session = null; _gate.Dispose();
        ClearSourceBitmaps(); ClearResultBitmaps();
    }

    partial void OnPathAChanged(string value) { if (!_restoring) InvalidateInputs("图像 A 已改变；请重新准备。"); }
    partial void OnPathBChanged(string value) { if (!_restoring) InvalidateInputs("图像 B 已改变；请重新准备。"); }
    partial void OnCanvasSizeChanged(int value) { if (!_restoring) InvalidateInputs("规范画布已改变；请重新准备。"); }
    partial void OnSelectedPresetChanged(string value) { OnPropertyChanged(nameof(UsesInterpolation)); if (!_restoring) InvalidateRecipe(); }
    partial void OnAmountChanged(double value) { if (!_restoring && UsesInterpolation) InvalidateRecipe(); }
    partial void OnSelectedPageChanged(string value) { if (!_restoring) MarkChanged(); }
    partial void OnSelectedPageIndexChanged(int value)
    {
        if (!_restoring) SelectedPage = value == 1 ? "相位与无数据" : "联动总览";
    }
    partial void OnSynchronizedZoomChanged(bool value) { if (!_restoring) MarkChanged(); }
    partial void OnMetricsVisibleChanged(bool value) { if (!_restoring) MarkChanged(); }

    private MagnitudePhaseRecipe CreateRecipe() => SelectedPreset switch
    {
        "A 幅度 + B 相位" => new(CanvasSize, MagnitudeComponentMode.SourceA, 0d, PhaseComponentMode.SourceB, 0d, MagnitudePhaseProjectionKind.PhysicalClamp),
        "B 幅度 + A 相位" => new(CanvasSize, MagnitudeComponentMode.SourceB, 0d, PhaseComponentMode.SourceA, 0d, MagnitudePhaseProjectionKind.PhysicalClamp),
        "A 幅度-only" => new(CanvasSize, MagnitudeComponentMode.SourceA, 0d, PhaseComponentMode.Zero, 0d, MagnitudePhaseProjectionKind.PhysicalClamp),
        "B 幅度-only" => new(CanvasSize, MagnitudeComponentMode.SourceB, 0d, PhaseComponentMode.Zero, 0d, MagnitudePhaseProjectionKind.PhysicalClamp),
        "A 相位-only" => new(CanvasSize, MagnitudeComponentMode.UnitNonZero, 0d, PhaseComponentMode.SourceA, 0d, MagnitudePhaseProjectionKind.SignedScientific),
        "B 相位-only" => new(CanvasSize, MagnitudeComponentMode.UnitNonZero, 0d, PhaseComponentMode.SourceB, 0d, MagnitudePhaseProjectionKind.SignedScientific),
        "幅度 A→B（相位 A）" => new(CanvasSize, MagnitudeComponentMode.LinearAtoB, Amount, PhaseComponentMode.SourceA, 0d, MagnitudePhaseProjectionKind.PhysicalClamp),
        "幅度 A→B（相位 B）" => new(CanvasSize, MagnitudeComponentMode.LinearAtoB, Amount, PhaseComponentMode.SourceB, 0d, MagnitudePhaseProjectionKind.PhysicalClamp),
        "相位 A→B（幅度 A）" => new(CanvasSize, MagnitudeComponentMode.SourceA, 0d, PhaseComponentMode.ShortestArcAtoB, Amount, MagnitudePhaseProjectionKind.PhysicalClamp),
        "相位 A→B（幅度 B）" => new(CanvasSize, MagnitudeComponentMode.SourceB, 0d, PhaseComponentMode.ShortestArcAtoB, Amount, MagnitudePhaseProjectionKind.PhysicalClamp),
        _ => throw new InvalidOperationException("请选择受支持的实验预设。")
    };

    private void ApplyRecipe(MagnitudePhaseRecipe recipe)
    {
        _restoring = true;
        try
        {
            CanvasSize = recipe.CanvasSize; Amount = recipe.MagnitudeMode == MagnitudeComponentMode.LinearAtoB
                ? recipe.MagnitudeAmount : recipe.PhaseAmount;
            SelectedPreset = (recipe.MagnitudeMode, recipe.PhaseMode) switch
            {
                (MagnitudeComponentMode.SourceA, PhaseComponentMode.SourceB) => "A 幅度 + B 相位",
                (MagnitudeComponentMode.SourceB, PhaseComponentMode.SourceA) => "B 幅度 + A 相位",
                (MagnitudeComponentMode.SourceA, PhaseComponentMode.Zero) => "A 幅度-only",
                (MagnitudeComponentMode.SourceB, PhaseComponentMode.Zero) => "B 幅度-only",
                (MagnitudeComponentMode.UnitNonZero, PhaseComponentMode.SourceA) => "A 相位-only",
                (MagnitudeComponentMode.UnitNonZero, PhaseComponentMode.SourceB) => "B 相位-only",
                (MagnitudeComponentMode.LinearAtoB, PhaseComponentMode.SourceA) => "幅度 A→B（相位 A）",
                (MagnitudeComponentMode.LinearAtoB, PhaseComponentMode.SourceB) => "幅度 A→B（相位 B）",
                (MagnitudeComponentMode.SourceA, PhaseComponentMode.ShortestArcAtoB) => "相位 A→B（幅度 A）",
                (MagnitudeComponentMode.SourceB, PhaseComponentMode.ShortestArcAtoB) => "相位 A→B（幅度 B）",
                _ => throw new InvalidDataException("配方不能映射到 V1 预设。")
            };
        }
        finally { _restoring = false; }
        MarkChanged();
    }

    private async Task RunGuardedAsync(string status, Func<CancellationToken, Task> operation)
    {
        if (_disposed) return; CancelOperation();
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _operationCancellation; var token = current.Token; var entered = false;
        IsBusy = true; StatusMessage = status;
        try { await _gate.WaitAsync(token); entered = true; await operation(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { if (!_lifetime.IsClosing) StatusMessage = "操作已取消；未提交部分或迟到结果。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { if (entered) _gate.Release(); if (ReferenceEquals(current, _operationCancellation)) IsBusy = false; }
    }

    private void InvalidateInputs(string status)
    {
        CancelOperation(); _session?.Dispose(); _session = null; _result = null;
        ClearSourceBitmaps(); ClearResultBitmaps(); MetricsSummary = "尚无当前结果。"; DiagnosticLabel = string.Empty;
        StatusMessage = status; OnPropertyChanged(nameof(HasInputs)); OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(CanExport)); MarkChanged();
    }

    private void InvalidateRecipe()
    {
        CancelOperation(); _session?.AdvanceGeneration(); _result = null; ClearResultBitmaps();
        _expectedFingerprintA = _expectedFingerprintB = null;
        StatusMessage = "实验配方已改变；旧结果已过期。"; MetricsSummary = "结果过期，请重新运行。";
        OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(CanExport)); MarkChanged();
    }

    private void ReplaceSession(MagnitudePhaseSession value)
    {
        var old = _session; _session = value; old?.Dispose(); _result = null; ClearResultBitmaps();
        OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(CanExport));
    }

    private void Restore(DocumentContent content)
    {
        PathA = PathB = string.Empty;
        if (content.SchemaVersion != MagnitudePhaseProtocol.SnapshotSchema) { StatusMessage = "快照版本不受支持；已使用安全默认值。"; return; }
        var state = _snapshotSerializer.Deserialize(content.Payload); if (state is null) return;
        try
        {
            MagnitudePhaseCanvasSize.Validate(state.CanvasSize);
            if (!Presets.Contains(state.Preset)) throw new InvalidDataException("快照预设无效。");
            if (!double.IsFinite(state.Amount) || state.Amount is < 0d or > 1d) throw new InvalidDataException("快照插值参数无效。");
            if (state.SelectedPage is not ("联动总览" or "相位与无数据")) throw new InvalidDataException("快照页面无效。");
            CanvasSize = state.CanvasSize; SelectedPreset = state.Preset; Amount = state.Amount;
            SelectedPage = state.SelectedPage; SelectedPageIndex = state.SelectedPage == "相位与无数据" ? 1 : 0;
            SynchronizedZoom = state.SynchronizedZoom; MetricsVisible = state.MetricsVisible;
            StatusMessage = $"已恢复 {state.DisplayNameA ?? "A"}/{state.DisplayNameB ?? "B"} 的轻量参数；请重新选择输入，不会自动读取或执行 FFT。";
        }
        catch (Exception exception) { StatusMessage = $"快照参数无效：{exception.Message}"; }
    }

    private async Task<Bitmap[]> CreateSourceBitmapsAsync(MagnitudePhaseSession session, CancellationToken token) =>
    [
        await CreateBitmapAsync(session.PreviewA, token), await CreateBitmapAsync(session.PreviewB, token),
        await CreateBitmapAsync(session.MagnitudeA, token), await CreateBitmapAsync(session.MagnitudeB, token),
        await CreateBitmapAsync(session.PhaseA, token), await CreateBitmapAsync(session.PhaseB, token)
    ];

    private async Task<Bitmap[]> CreateResultBitmapsAsync(MagnitudePhaseRenderResult result, CancellationToken token) =>
    [ await CreateBitmapAsync(result.ResultImage, token), await CreateBitmapAsync(result.ResultMagnitude, token), await CreateBitmapAsync(result.ResultPhase, token) ];

    private async Task<Bitmap> CreateBitmapAsync(PixelImage image, CancellationToken token)
    {
        var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, writable: false); return new Bitmap(stream);
    }

    private void CommitSourceBitmaps(Bitmap[] values)
    {
        ReplaceBitmap(ref _canvasAPreview, values[0], nameof(CanvasAPreview)); ReplaceBitmap(ref _canvasBPreview, values[1], nameof(CanvasBPreview));
        ReplaceBitmap(ref _magnitudeAPreview, values[2], nameof(MagnitudeAPreview)); ReplaceBitmap(ref _magnitudeBPreview, values[3], nameof(MagnitudeBPreview));
        ReplaceBitmap(ref _phaseAPreview, values[4], nameof(PhaseAPreview)); ReplaceBitmap(ref _phaseBPreview, values[5], nameof(PhaseBPreview));
    }

    private void CommitResultBitmaps(Bitmap[] values)
    {
        ReplaceBitmap(ref _resultPreview, values[0], nameof(ResultPreview)); ReplaceBitmap(ref _resultMagnitudePreview, values[1], nameof(ResultMagnitudePreview));
        ReplaceBitmap(ref _resultPhasePreview, values[2], nameof(ResultPhasePreview));
    }

    private static string Describe(MagnitudePhaseDiagnosticsResult d) =>
        $"幅度供体相对误差 {d.Mix.RelativeMagnitudeError:E2}；相位供体加权误差 {d.Mix.WeightedPhaseErrorRadians:E2} rad；" +
        $"共轭 {d.Mix.MaximumConjugateError:E2}；虚部 {d.MaximumImaginaryResidual:E2}（相对 {d.RelativeImaginaryResidual:E2}）；" +
        $"未定义相位 {d.Mix.UndefinedPhaseCount}，借用能量 {d.Mix.BorrowedPhaseEnergyRatio:P3}；" +
        $"NCC A/B {Format(d.Spatial.NccA)}/{Format(d.Spatial.NccB)}；梯度 A/B {Format(d.Spatial.GradientCorrelationA)}/{Format(d.Spatial.GradientCorrelationB)}；" +
        $"PSNR A/B {Format(d.Spatial.PsnrA)}/{Format(d.Spatial.PsnrB)}；SSIM A/B {Format(d.Spatial.SsimA)}/{Format(d.Spatial.SsimB)}；" +
        $"裁切低/高 {d.Projection.ClippedLowCount}/{d.Projection.ClippedHighCount}。";

    private static string Format(MagnitudePhaseMetric metric) => metric.Status == MagnitudePhaseMetricStatus.Available
        ? metric.Value!.Value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
        : metric.Status == MagnitudePhaseMetricStatus.ExactMatch ? "Exact" : "N/A";

    private static string Phase(double? value) => value.HasValue
        ? value.Value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) : "N/A";

    private void ClearSourceBitmaps()
    {
        ReplaceBitmap(ref _canvasAPreview, null, nameof(CanvasAPreview)); ReplaceBitmap(ref _canvasBPreview, null, nameof(CanvasBPreview));
        ReplaceBitmap(ref _magnitudeAPreview, null, nameof(MagnitudeAPreview)); ReplaceBitmap(ref _magnitudeBPreview, null, nameof(MagnitudeBPreview));
        ReplaceBitmap(ref _phaseAPreview, null, nameof(PhaseAPreview)); ReplaceBitmap(ref _phaseBPreview, null, nameof(PhaseBPreview));
    }

    private void ClearResultBitmaps()
    {
        ReplaceBitmap(ref _resultPreview, null, nameof(ResultPreview)); ReplaceBitmap(ref _resultMagnitudePreview, null, nameof(ResultMagnitudePreview));
        ReplaceBitmap(ref _resultPhasePreview, null, nameof(ResultPhasePreview)); DiagnosticLabel = string.Empty;
    }

    private void ReplaceBitmap(ref Bitmap? field, Bitmap? value, string propertyName) { var old = field; field = value; OnPropertyChanged(propertyName); if (!ReferenceEquals(old, value)) old?.Dispose(); }
    private static void DisposeAll(IEnumerable<Bitmap> values) { foreach (var value in values) value.Dispose(); }
    private void CancelOperation() { _operationCancellation?.Cancel(); _operationCancellation?.Dispose(); _operationCancellation = null; }
    private void MarkChanged() { if (_restoring) return; var dirty = IsDirty; _revision++; if (dirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }
}
