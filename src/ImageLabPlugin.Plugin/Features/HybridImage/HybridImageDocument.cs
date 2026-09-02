using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.HybridImage;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.HybridImage;
using ImageLabPlugin.Domain.Shared.Imaging;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.HybridImage;

/// <summary>Hybrid Image 多实例 Document：只管理交互状态、代次、Bitmap 与生命周期。</summary>
/// <remarks>
/// 控制点求解、warp、Gaussian、FFT 和量化全部位于 Domain/Application。Document 的串行闸门配合
/// Session generation 拒绝迟到候选；Bitmap 只有当前实例拥有，替换、关闭或失败候选都会立即释放。
/// </remarks>
internal sealed partial class HybridImageDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private readonly IPrepareHybridInputsUseCase _prepare;
    private readonly ISolveHybridAlignmentUseCase _solve;
    private readonly IRenderHybridPreviewUseCase _renderPreview;
    private readonly IRenderHybridFullSizeUseCase _renderFull;
    private readonly IExportHybridImageUseCase _exportImage;
    private readonly IImportHybridRecipeUseCase _importRecipe;
    private readonly IExportHybridRecipeUseCase _exportRecipe;
    private readonly IExportHybridReportUseCase _exportReport;
    private readonly IHybridImageSnapshotSerializer _snapshotSerializer;
    private readonly IHybridImageFileDialog _dialog;
    private readonly IImageCodec _codec;
    private readonly HybridImageDiagnostics _diagnostics;
    private readonly IDocumentLifetime _lifetime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _operationCancellation;
    private HybridImageSession? _session;
    private HybridImageRecipe? _recipe;
    private HybridRenderResult? _result;
    private DocumentPresentationState _presentation = new("混合图像");
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;
    private bool _disposed;

    public HybridImageDocument(
        IPrepareHybridInputsUseCase prepare,
        ISolveHybridAlignmentUseCase solve,
        IRenderHybridPreviewUseCase renderPreview,
        IRenderHybridFullSizeUseCase renderFull,
        IExportHybridImageUseCase exportImage,
        IImportHybridRecipeUseCase importRecipe,
        IExportHybridRecipeUseCase exportRecipe,
        IExportHybridReportUseCase exportReport,
        IHybridImageSnapshotSerializer snapshotSerializer,
        IHybridImageFileDialog dialog,
        IImageCodec codec,
        HybridImageDiagnostics diagnostics,
        IDocumentLifetime lifetime)
    {
        _prepare = prepare; _solve = solve; _renderPreview = renderPreview; _renderFull = renderFull;
        _exportImage = exportImage; _importRecipe = importRecipe; _exportRecipe = exportRecipe;
        _exportReport = exportReport; _snapshotSerializer = snapshotSerializer; _dialog = dialog;
        _codec = codec; _diagnostics = diagnostics; _lifetime = lifetime;
        AddDefaultPoints();
    }

    public ObservableCollection<HybridAlignmentPointRow> ControlPoints { get; } = [];
    [ObservableProperty] private string _pathA = string.Empty;
    [ObservableProperty] private string _pathB = string.Empty;
    [ObservableProperty] private double _lowSigmaPixels = 8d;
    [ObservableProperty] private double _highSigmaPixels = 6d;
    [ObservableProperty] private double _lowGain = 1d;
    [ObservableProperty] private double _highGain = 1d;
    [ObservableProperty] private double _cropLeft = .05d;
    [ObservableProperty] private double _cropTop = .05d;
    [ObservableProperty] private double _cropRight = .95d;
    [ObservableProperty] private double _cropBottom = .95d;
    [ObservableProperty] private string _selectedTab = "四尺度";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "请选择 A（远看低频主体）与 B（近看高频主体）。";
    [ObservableProperty] private string _alignmentSummary = "尚未求解";
    [ObservableProperty] private string _resultSummary = "尚未渲染";
    [ObservableProperty] private double _lowCutoffRadius;
    [ObservableProperty] private double _highCutoffRadius;
    private Bitmap? _sourceAPreview;
    private Bitmap? _sourceBPreview;
    private Bitmap? _edgeOverlayPreview;
    private Bitmap? _lowComponentPreview;
    private Bitmap? _highComponentPreview;
    private Bitmap? _scale1Preview;
    private Bitmap? _scale2Preview;
    private Bitmap? _scale4Preview;
    private Bitmap? _scale8Preview;
    private Bitmap? _spectrumPreview;

    public Bitmap? SourceAPreview => _sourceAPreview;
    public Bitmap? SourceBPreview => _sourceBPreview;
    public Bitmap? EdgeOverlayPreview => _edgeOverlayPreview;
    public Bitmap? LowComponentPreview => _lowComponentPreview;
    public Bitmap? HighComponentPreview => _highComponentPreview;
    public Bitmap? Scale1Preview => _scale1Preview;
    public Bitmap? Scale2Preview => _scale2Preview;
    public Bitmap? Scale4Preview => _scale4Preview;
    public Bitmap? Scale8Preview => _scale8Preview;
    public Bitmap? SpectrumPreview => _spectrumPreview;

    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasInputs => _session is not null;
    public bool HasAlignment => _session?.Alignment is not null;
    public bool HasResult => _result is not null;
    public bool CanExportImage => _result is { IsFullSize: true } && ReferenceEquals(_session?.LastFullSize, _result);
    public double LowFiftyPercentCutoff => GaussianPlaneFilter.FiftyPercentCutoff(LowSigmaPixels);
    public double HighFiftyPercentCutoff => GaussianPlaneFilter.FiftyPercentCutoff(HighSigmaPixels);

    public event EventHandler? PresentationChanged;
    public event EventHandler? IsDirtyChanged;

    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation);
        cancellationToken.ThrowIfCancellationRequested();
        _restoring = true;
        try
        {
            if (activation is RestoreDocumentActivation restore) Restore(restore.RestoredContent);
            _presentation = new DocumentPresentationState(string.IsNullOrWhiteSpace(activation.Title) ? "混合图像" : activation.Title);
            _revision = _acceptedRevision = 0;
            PresentationChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _restoring = false; }
        return ValueTask.CompletedTask;
    }

    [RelayCommand] private async Task SelectAAsync() { var path = await _dialog.PickHybridInputAsync("A（低频主体）", _lifetime.ClosingToken); if (path is not null) PathA = path; }
    [RelayCommand] private async Task SelectBAsync() { var path = await _dialog.PickHybridInputAsync("B（高频主体）", _lifetime.ClosingToken); if (path is not null) PathB = path; }

    [RelayCommand]
    private Task PrepareAsync() => RunGuardedAsync("正在各解码一次 A/B 并建立亮度代理…", async token =>
    {
        if (string.IsNullOrWhiteSpace(PathA) || string.IsNullOrWhiteSpace(PathB))
            throw new InvalidOperationException("请先选择图像 A 和 B。");
        var candidate = await _prepare.ExecuteAsync(new PrepareHybridInputsRequest(PathA, PathB), token).ConfigureAwait(false);
        var sourceA = await CreateBitmapAsync(candidate.ProxyA, token).ConfigureAwait(false);
        var sourceB = await CreateBitmapAsync(candidate.ProxyB, token).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            // 文件端口若忽略取消，路径变化或关闭后仍可能迟到；提交前再次检查 token 与实例生命期。
            if (token.IsCancellationRequested || _disposed)
            {
                candidate.Dispose(); sourceA.Dispose(); sourceB.Dispose(); return;
            }
            ReplaceSession(candidate); ReplaceBitmap(ref _sourceAPreview, sourceA, nameof(SourceAPreview));
            ReplaceBitmap(ref _sourceBPreview, sourceB, nameof(SourceBPreview));
            StatusMessage = $"输入已准备：A {candidate.SourceA.Size.Width}×{candidate.SourceA.Size.Height}；B {candidate.SourceB.Size.Width}×{candidate.SourceB.Size.Height}。";
            OnPropertyChanged(nameof(HasInputs));
        });
    });

    [RelayCommand]
    private Task SolveAlignmentAsync() => RunGuardedAsync("正在求解 B→A 相似变换与有效交集…", async token =>
    {
        var session = _session ?? throw new InvalidOperationException("请先准备双输入。");
        var state = await _solve.ExecuteAsync(session, new SolveHybridAlignmentRequest(CreatePoints()), token).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var normalized = HybridNormalizedCrop.FromPixels(state.MaximumCrop, session.ProxyA.Size);
            _restoring = true;
            try { CropLeft = normalized.Left; CropTop = normalized.Top; CropRight = normalized.Right; CropBottom = normalized.Bottom; }
            finally { _restoring = false; }
            AlignmentSummary = state.Solution.ResidualStatus == HybridResidualStatus.NotIndependentlyValidated
                ? $"缩放 {state.Solution.Transform.Scale:F4}；旋转 {state.Solution.Transform.RotationDegrees:F2}°；两点无法独立验证残差"
                : $"缩放 {state.Solution.Transform.Scale:F4}；旋转 {state.Solution.Transform.RotationDegrees:F2}°；RMS {state.Solution.RmsResidualPixels:F3}px；覆盖 {state.CoverageRatio:P1}";
            StatusMessage = "对齐已求解；请检查红青边缘和裁切，再生成代理。";
            OnPropertyChanged(nameof(HasAlignment)); MarkChanged();
        });
    });

    [RelayCommand] private Task RenderPreviewAsync() => RenderAsync(fullSize: false);
    [RelayCommand] private Task RenderFullSizeAsync() => RenderAsync(fullSize: true);

    private Task RenderAsync(bool fullSize) => RunGuardedAsync(fullSize ? "正在显式生成完整尺寸结果…" : "正在生成代理、四尺度与共享量程频谱…", async token =>
    {
        var session = _session ?? throw new InvalidOperationException("请先准备双输入。");
        if (session.Alignment is null) throw new InvalidOperationException("请先求解对齐。");
        var recipe = CreateRecipe();
        var generation = session.AdvanceGeneration();
        var candidate = fullSize
            ? await _renderFull.ExecuteAsync(session, recipe, generation, token).ConfigureAwait(false)
            : await _renderPreview.ExecuteAsync(session, recipe, generation, token).ConfigureAwait(false);
        var bitmaps = await CreateResultBitmapsAsync(candidate, token).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            // generation 之外再校验 Session 引用与取消状态，覆盖“底层服务忽略取消后迟到”的测试替身。
            if (token.IsCancellationRequested || !ReferenceEquals(session, _session) ||
                !session.TryCommit(candidate, generation, recipe.Fingerprint()))
            { DisposeAll(bitmaps); return; }
            _recipe = recipe; _result = candidate; CommitBitmaps(bitmaps);
            LowCutoffRadius = candidate.Cutoff.LowDisplayRadiusPixels;
            HighCutoffRadius = candidate.Cutoff.HighDisplayRadiusPixels;
            ResultSummary = $"{(candidate.IsFullSize ? "完整尺寸" : "代理")} {candidate.Crop.Width}×{candidate.Crop.Height}；raw [{candidate.Composition.Statistics.Minimum:F4},{candidate.Composition.Statistics.Maximum:F4}]；裁切 {candidate.Composition.Statistics.ClippedRatio:P2}";
            StatusMessage = $"渲染完成，配方指纹 {candidate.RecipeFingerprint}。四尺度来自同一未量化 raw。";
            OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(CanExportImage));
        });
    });

    [RelayCommand] private void Cancel() { _operationCancellation?.Cancel(); StatusMessage = "已请求取消；最后有效结果保持不变。"; }

    [RelayCommand]
    private void AddPoint()
    {
        if (ControlPoints.Count >= 8) { StatusMessage = "V1 最多 8 对控制点。"; return; }
        var row = new HybridAlignmentPointRow(ControlPoints.Count == 0 ? 1 : ControlPoints.Max(static item => item.Id) + 1, .5, .5, .5, .5);
        Subscribe(row); ControlPoints.Add(row); InvalidateMath("已添加控制点；请填写分散坐标。");
    }

    [RelayCommand]
    private void RemovePoint()
    {
        if (ControlPoints.Count <= 2) { StatusMessage = "V1 至少保留 2 对控制点。"; return; }
        var row = ControlPoints[^1]; row.PropertyChanged -= PointChanged; ControlPoints.RemoveAt(ControlPoints.Count - 1);
        InvalidateMath("已删除最后一对控制点。");
    }

    [RelayCommand]
    private void SwapInputs()
    {
        (PathA, PathB) = (PathB, PathA);
        foreach (var row in ControlPoints) row.Swap();
        InvalidateInputs("已交换 A/B 角色；请重新准备并求解 B→A。");
    }

    [RelayCommand]
    private async Task ImportRecipeAsync()
    {
        var path = await _dialog.PickHybridRecipeInputAsync(_lifetime.ClosingToken); if (path is null) return;
        await RunGuardedAsync("正在严格导入配方…", async token =>
        {
            var value = await _importRecipe.ExecuteAsync(path, token).ConfigureAwait(false);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyRecipe(value.Recipe);
                InvalidateInputs($"配方已导入（期望 A {value.FingerprintA} / B {value.FingerprintB}）；请重新选择并匹配输入。");
            });
        });
    }

    [RelayCommand]
    private async Task ExportRecipeAsync()
    {
        if (_session is null) { StatusMessage = "请先准备输入。"; return; }
        HybridImageRecipe recipe;
        try { recipe = CreateRecipe(); } catch (Exception exception) { StatusMessage = exception.Message; return; }
        var path = await _dialog.PickHybridRecipeOutputAsync("hybrid-image-recipe.json", _lifetime.ClosingToken); if (path is null) return;
        try { await _exportRecipe.ExecuteAsync(recipe, _session, path, _lifetime.ClosingToken); StatusMessage = "配方已原子导出，不含路径或像素。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand]
    private async Task ExportImageAsync()
    {
        if (_session is null || _recipe is null || _result is null) { StatusMessage = "没有当前完整尺寸结果。"; return; }
        var path = await _dialog.PickHybridResultPngAsync("hybrid-image.png", _lifetime.ClosingToken); if (path is null) return;
        try { await _exportImage.ExecuteAsync(_session, _result, _recipe, path, _lifetime.ClosingToken); StatusMessage = "PNG 已完成内存与真实目标回读。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand] private Task ExportReportJsonAsync() => ExportReportCoreAsync(false);
    [RelayCommand] private Task ExportReportCsvAsync() => ExportReportCoreAsync(true);

    private async Task ExportReportCoreAsync(bool csv)
    {
        if (_session is null || _recipe is null || _result is null) { StatusMessage = "没有当前诊断结果。"; return; }
        var path = csv ? await _dialog.PickHybridReportCsvAsync("hybrid-image-report.csv", _lifetime.ClosingToken)
            : await _dialog.PickHybridReportJsonAsync("hybrid-image-report.json", _lifetime.ClosingToken);
        if (path is null) return;
        var report = CreateReport(_session, _recipe, _result);
        try { await _exportReport.ExecuteAsync(report, path, csv, _lifetime.ClosingToken); StatusMessage = "脱敏报告已原子导出。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var crop = SafeCrop();
        var snapshot = new HybridImageSnapshotState(Path.GetFileName(PathA), Path.GetFileName(PathB),
            CreatePoints(), crop, LowSigmaPixels, HighSigmaPixels, LowGain, HighGain,
            SelectedTab, HybridImageProtocol.SnapshotSchema);
        return ValueTask.FromResult(new DocumentSaveSnapshot(new DocumentRevision(_revision),
            new DocumentContent(HybridImageProtocol.SnapshotSchema, _snapshotSerializer.Serialize(snapshot))));
    }

    public void AcceptChanges(DocumentRevision savedRevision)
    {
        var dirty = IsDirty; if (savedRevision.Value == _revision) _acceptedRevision = _revision;
        if (dirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; CancelOperation(); _session?.Dispose(); _session = null; _gate.Dispose();
        foreach (var row in ControlPoints) row.PropertyChanged -= PointChanged;
        ReplaceBitmap(ref _sourceAPreview, null, nameof(SourceAPreview)); ReplaceBitmap(ref _sourceBPreview, null, nameof(SourceBPreview));
        ReplaceBitmap(ref _edgeOverlayPreview, null, nameof(EdgeOverlayPreview)); ReplaceBitmap(ref _lowComponentPreview, null, nameof(LowComponentPreview));
        ReplaceBitmap(ref _highComponentPreview, null, nameof(HighComponentPreview)); ReplaceBitmap(ref _scale1Preview, null, nameof(Scale1Preview));
        ReplaceBitmap(ref _scale2Preview, null, nameof(Scale2Preview)); ReplaceBitmap(ref _scale4Preview, null, nameof(Scale4Preview));
        ReplaceBitmap(ref _scale8Preview, null, nameof(Scale8Preview)); ReplaceBitmap(ref _spectrumPreview, null, nameof(SpectrumPreview));
    }

    partial void OnPathAChanged(string value) { if (!_restoring) InvalidateInputs("图像 A 已改变；请重新准备。"); }
    partial void OnPathBChanged(string value) { if (!_restoring) InvalidateInputs("图像 B 已改变；请重新准备。"); }
    partial void OnLowSigmaPixelsChanged(double value) { OnPropertyChanged(nameof(LowFiftyPercentCutoff)); ParameterChanged(); }
    partial void OnHighSigmaPixelsChanged(double value) { OnPropertyChanged(nameof(HighFiftyPercentCutoff)); ParameterChanged(); }
    partial void OnLowGainChanged(double value) => ParameterChanged();
    partial void OnHighGainChanged(double value) => ParameterChanged();
    partial void OnCropLeftChanged(double value) => ParameterChanged();
    partial void OnCropTopChanged(double value) => ParameterChanged();
    partial void OnCropRightChanged(double value) => ParameterChanged();
    partial void OnCropBottomChanged(double value) => ParameterChanged();
    partial void OnSelectedTabChanged(string value) { if (!_restoring) MarkChanged(); }

    private void ParameterChanged() { if (!_restoring) InvalidateMath("参数已改变；旧结果已过期，请重新生成。"); }
    private void PointChanged(object? sender, PropertyChangedEventArgs e) { if (!_restoring) InvalidateMath("控制点已改变；请重新求解对齐。"); }

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
        CancelOperation(); _session?.Dispose(); _session = null; _recipe = null; _result = null;
        ClearResultBitmaps(); AlignmentSummary = "尚未求解"; ResultSummary = "尚未渲染"; StatusMessage = status;
        OnPropertyChanged(nameof(HasInputs)); OnPropertyChanged(nameof(HasAlignment)); OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(CanExportImage)); MarkChanged();
    }

    private void InvalidateMath(string status)
    {
        CancelOperation(); _session?.AdvanceGeneration(); _recipe = null; _result = null; ClearResultBitmaps();
        ResultSummary = "结果已过期"; StatusMessage = status; OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(CanExportImage)); MarkChanged();
    }

    private void ReplaceSession(HybridImageSession candidate)
    {
        var old = _session; _session = candidate; old?.Dispose(); _recipe = null; _result = null; ClearResultBitmaps();
        AlignmentSummary = "尚未求解"; ResultSummary = "尚未渲染";
        OnPropertyChanged(nameof(HasAlignment)); OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(CanExportImage));
    }

    private IReadOnlyList<HybridAlignmentPointPair> CreatePoints() => ControlPoints.Select(static row => row.ToDomain()).ToArray();
    private HybridNormalizedCrop SafeCrop() { try { return new HybridNormalizedCrop(CropLeft, CropTop, CropRight, CropBottom); } catch { return new HybridNormalizedCrop(.05, .05, .95, .95); } }
    private HybridImageRecipe CreateRecipe() => new(CreatePoints(), new HybridNormalizedCrop(CropLeft, CropTop, CropRight, CropBottom), LowSigmaPixels, HighSigmaPixels, LowGain, HighGain);

    private static HybridImageReport CreateReport(HybridImageSession session, HybridImageRecipe recipe, HybridRenderResult result) =>
        new(HybridImageProtocol.Report, HybridImageProtocol.Schema, session.FingerprintA, session.FingerprintB,
            session.SourceA.Size, session.SourceB.Size, result.RecipeFingerprint, result.Diagnostics, result.Crop,
            result.Composition.Statistics, result.Scales.Select(static scale => scale.Image.Size).ToArray(),
            recipe.LowSigmaPixels, recipe.HighSigmaPixels, GaussianPlaneFilter.FiftyPercentCutoff(recipe.LowSigmaPixels),
            GaussianPlaneFilter.FiftyPercentCutoff(recipe.HighSigmaPixels), recipe.LowGain, recipe.HighGain,
            (long)result.Elapsed.TotalMilliseconds, "1.0.0",
            "远看由确定性缩小近似；残差和红青边缘不是自动配准或主观可见性保证。");

    private void AddDefaultPoints()
    {
        AddRow(new HybridAlignmentPointRow(1, .25, .3, .25, .3));
        AddRow(new HybridAlignmentPointRow(2, .75, .3, .75, .3));
        AddRow(new HybridAlignmentPointRow(3, .5, .75, .5, .75));
    }

    private void AddRow(HybridAlignmentPointRow row) { Subscribe(row); ControlPoints.Add(row); }
    private void Subscribe(HybridAlignmentPointRow row) => row.PropertyChanged += PointChanged;

    private void ApplyRecipe(HybridImageRecipe recipe)
    {
        _restoring = true;
        try
        {
            foreach (var row in ControlPoints) row.PropertyChanged -= PointChanged;
            ControlPoints.Clear();
            foreach (var point in recipe.Points) AddRow(new HybridAlignmentPointRow(point.Id,
                point.PointA.X, point.PointA.Y, point.PointB.X, point.PointB.Y));
            CropLeft = recipe.Crop.Left; CropTop = recipe.Crop.Top; CropRight = recipe.Crop.Right; CropBottom = recipe.Crop.Bottom;
            LowSigmaPixels = recipe.LowSigmaPixels; HighSigmaPixels = recipe.HighSigmaPixels;
            LowGain = recipe.LowGain; HighGain = recipe.HighGain;
        }
        finally { _restoring = false; }
        MarkChanged();
    }

    private void Restore(DocumentContent content)
    {
        PathA = PathB = string.Empty;
        if (content.SchemaVersion != HybridImageProtocol.SnapshotSchema) { StatusMessage = "快照版本不受支持；已使用安全默认值。"; return; }
        var state = _snapshotSerializer.Deserialize(content.Payload); if (state is null) return;
        try
        {
            ApplyRecipe(new HybridImageRecipe(state.Points, state.Crop, state.LowSigmaPixels,
                state.HighSigmaPixels, state.LowGain, state.HighGain));
            SelectedTab = state.SelectedTab;
            StatusMessage = $"已恢复 {state.DisplayNameA ?? "A"}/{state.DisplayNameB ?? "B"} 的轻量参数；请重新选择输入，不会自动读取或渲染。";
        }
        catch (Exception exception) { StatusMessage = $"快照参数无效：{exception.Message}"; }
    }

    private async Task<Bitmap> CreateBitmapAsync(PixelImage image, CancellationToken token)
    {
        var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, writable: false); return new Bitmap(stream);
    }

    private async Task<Bitmap[]> CreateResultBitmapsAsync(HybridRenderResult result, CancellationToken token) =>
    [
        await CreateBitmapAsync(result.EdgeOverlay, token),
        await CreateBitmapAsync(HybridImageComposer.Quantize(result.Composition.LowA, token), token),
        await CreateBitmapAsync(_diagnostics.CreateSignedComponentPreview(result.Composition.HighB, 2d, token), token),
        await CreateBitmapAsync(result.Scales.Single(static item => item.Divisor == 1).Image, token),
        await CreateBitmapAsync(result.Scales.Single(static item => item.Divisor == 2).Image, token),
        await CreateBitmapAsync(result.Scales.Single(static item => item.Divisor == 4).Image, token),
        await CreateBitmapAsync(result.Scales.Single(static item => item.Divisor == 8).Image, token),
        await CreateBitmapAsync(result.Spectra.Project(HybridSpectrumKind.Raw, token), token)
    ];

    private void CommitBitmaps(Bitmap[] values)
    {
        ReplaceBitmap(ref _edgeOverlayPreview, values[0], nameof(EdgeOverlayPreview)); ReplaceBitmap(ref _lowComponentPreview, values[1], nameof(LowComponentPreview));
        ReplaceBitmap(ref _highComponentPreview, values[2], nameof(HighComponentPreview)); ReplaceBitmap(ref _scale1Preview, values[3], nameof(Scale1Preview));
        ReplaceBitmap(ref _scale2Preview, values[4], nameof(Scale2Preview)); ReplaceBitmap(ref _scale4Preview, values[5], nameof(Scale4Preview));
        ReplaceBitmap(ref _scale8Preview, values[6], nameof(Scale8Preview)); ReplaceBitmap(ref _spectrumPreview, values[7], nameof(SpectrumPreview));
    }

    private void ClearResultBitmaps()
    {
        ReplaceBitmap(ref _edgeOverlayPreview, null, nameof(EdgeOverlayPreview)); ReplaceBitmap(ref _lowComponentPreview, null, nameof(LowComponentPreview));
        ReplaceBitmap(ref _highComponentPreview, null, nameof(HighComponentPreview)); ReplaceBitmap(ref _scale1Preview, null, nameof(Scale1Preview));
        ReplaceBitmap(ref _scale2Preview, null, nameof(Scale2Preview)); ReplaceBitmap(ref _scale4Preview, null, nameof(Scale4Preview));
        ReplaceBitmap(ref _scale8Preview, null, nameof(Scale8Preview)); ReplaceBitmap(ref _spectrumPreview, null, nameof(SpectrumPreview));
        LowCutoffRadius = HighCutoffRadius = 0d;
    }

    private void ReplaceBitmap(ref Bitmap? field, Bitmap? value, string propertyName) { var old = field; field = value; OnPropertyChanged(propertyName); if (!ReferenceEquals(old, value)) old?.Dispose(); }
    private static void DisposeAll(IEnumerable<Bitmap> bitmaps) { foreach (var bitmap in bitmaps) bitmap.Dispose(); }
    private void CancelOperation() { _operationCancellation?.Cancel(); _operationCancellation?.Dispose(); _operationCancellation = null; }
    private void MarkChanged() { if (_restoring) return; var dirty = IsDirty; _revision++; if (dirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }
}
