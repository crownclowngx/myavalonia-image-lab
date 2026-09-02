using System.Globalization;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.ImageOscilloscope;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.ImageOscilloscope;
using ImageLabPlugin.Domain.Imaging;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.ImageOscilloscope;

/// <summary>图像示波器 Document：管理实例参数、命令、代次、轻量快照和 Bitmap 生命周期。</summary>
/// <remarks>
/// 像素循环、颜色公式、P99.5 和 Scope 坐标均委托给领域/应用服务。完整分析与裁切使用独立 generation；
/// 每个候选提交前检查 Document 未关闭、代次和 Session 引用。新候选失败时保留最后有效 Session 与全部图表。
/// </remarks>
internal sealed partial class ImageOscilloscopeDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private readonly IPrepareImageOscilloscopeSessionUseCase _prepareUseCase;
    private readonly IRecalculateImageOscilloscopeClippingUseCase _clippingUseCase;
    private readonly IProjectImageOscilloscopeDisplayUseCase _displayUseCase;
    private readonly IInspectImageOscilloscopePixelUseCase _inspectUseCase;
    private readonly IImageFileDialog _dialog;
    private readonly IImageCodec _codec;
    private readonly ImageProbeCoordinateMapper _coordinateMapper;
    private readonly IDocumentLifetime _lifetime;
    private ImageOscilloscopeSession? _session;
    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _clippingCancellation;
    private CancellationTokenSource? _displayCancellation;
    private DocumentPresentationState _presentation = new("图像示波器");
    private long _analysisGeneration;
    private long _displayGeneration;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;
    private bool _disposed;
    private double? _pinnedX;
    private double? _pinnedY;

    public ImageOscilloscopeDocument(
        IPrepareImageOscilloscopeSessionUseCase prepareUseCase,
        IRecalculateImageOscilloscopeClippingUseCase clippingUseCase,
        IProjectImageOscilloscopeDisplayUseCase displayUseCase,
        IInspectImageOscilloscopePixelUseCase inspectUseCase,
        IImageFileDialog dialog, IImageCodec codec,
        ImageProbeCoordinateMapper coordinateMapper, VectorscopeReferenceTargetProvider referenceTargetProvider,
        IDocumentLifetime lifetime)
    {
        _prepareUseCase = prepareUseCase;
        _clippingUseCase = clippingUseCase;
        _displayUseCase = displayUseCase;
        _inspectUseCase = inspectUseCase;
        _dialog = dialog;
        _codec = codec;
        _coordinateMapper = coordinateMapper;
        _lifetime = lifetime;
        VectorscopeReferenceTargets = referenceTargetProvider.Create();
    }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private int _shadowThreshold = 5;
    [ObservableProperty] private int _highlightThreshold = 250;
    [ObservableProperty] private string _selectedDensityMode = "对数";
    [ObservableProperty] private string _selectedClippingMode = "亮度";
    [ObservableProperty] private bool _waveformVisible = true;
    [ObservableProperty] private bool _paradeVisible = true;
    [ObservableProperty] private bool _vectorscopeVisible = true;
    [ObservableProperty] private bool _histogramVisible = true;
    [ObservableProperty] private double _viewZoom = 1d;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isUpdatingClipping;
    [ObservableProperty] private string _statusMessage = "请选择 PNG/JPEG 图片并开始分析；所有数值按白底 sRGB/BT.601 解释。";
    [ObservableProperty] private string _analysisSummary = "尚无分析结果。";
    [ObservableProperty] private string _clippingSummary = "默认阈值：阴影 ≤ 5，高光 ≥ 250。";
    [ObservableProperty] private string _probeSummary = "在源图上移动查看像素；单击固定，按钮可清除固定探针。";
    private Bitmap? _sourcePreview;
    private Bitmap? _clippingOverlay;
    private Bitmap? _waveformImage;
    private Bitmap? _paradeImage;
    private Bitmap? _vectorscopeImage;
    [ObservableProperty] private ScopeProbe? _currentProbe;

    public IReadOnlyList<string> DensityModes { get; } = ["对数", "线性"];
    public IReadOnlyList<string> ClippingModes { get; } = ["关闭", "亮度", "RGB 任一通道"];
    public IReadOnlyList<ScopeReferenceTarget> VectorscopeReferenceTargets { get; }
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasAnalysis => _session is not null;
    public bool HasPinnedProbe => _pinnedX.HasValue && _pinnedY.HasValue;
    public ImageOscilloscopeAnalysis? Analysis => _session?.Analysis;
    public bool IsOperationBusy => IsBusy || IsUpdatingClipping;
    public Bitmap? SourcePreview => _sourcePreview;
    public Bitmap? ClippingOverlay => _clippingOverlay;
    public Bitmap? WaveformImage => _waveformImage;
    public Bitmap? ParadeImage => _paradeImage;
    public Bitmap? VectorscopeImage => _vectorscopeImage;

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
            _presentation = new DocumentPresentationState(string.IsNullOrWhiteSpace(activation.Title) ? "图像示波器" : activation.Title);
            PresentationChanged?.Invoke(this, EventArgs.Empty);
            _revision = _acceptedRevision = 0;
        }
        finally { _restoring = false; }
        return ValueTask.CompletedTask;
    }

    [RelayCommand]
    private async Task SelectSourceAsync()
    {
        var path = await _dialog.PickImageAsync(_lifetime.ClosingToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path)) SourcePath = path;
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || !File.Exists(SourcePath))
        { StatusMessage = "请选择存在的 PNG 或 JPEG 图片。"; return; }
        ClippingThresholds thresholds;
        try { thresholds = CurrentThresholds(); }
        catch (Exception exception) { StatusMessage = exception.Message; return; }

        CancelAndDispose(ref _analysisCancellation);
        _analysisCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _analysisCancellation;
        var token = current.Token;
        var generation = ++_analysisGeneration;
        IsBusy = true;
        StatusMessage = "正在解码并一次扫描全部源像素…";
        ImageOscilloscopeSession? candidate = null;
        Bitmap[]? bitmaps = null;
        try
        {
            candidate = await _prepareUseCase.ExecuteAsync(SourcePath, thresholds, token).ConfigureAwait(true);
            bitmaps = await CreateCompleteBitmapSetAsync(candidate, token).ConfigureAwait(true);
            if (!CanCommitAnalysis(generation)) return;
            ReplaceSession(candidate); candidate = null;
            CommitCompleteBitmapSet(bitmaps); bitmaps = null;
            RefreshAnalysisText();
            RestorePinnedProbe();
            StatusMessage = "全图分析完成；切换密度模式不会重新扫描图片。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { if (!_lifetime.IsClosing && generation == _analysisGeneration) StatusMessage = "分析已取消；最后有效结果保持不变。"; }
        catch (Exception exception)
        { if (generation == _analysisGeneration) StatusMessage = $"分析失败：{exception.Message}；最后有效结果保持不变。"; }
        finally
        {
            candidate?.Dispose();
            if (bitmaps is not null) DisposeAll(bitmaps);
            if (ReferenceEquals(current, _analysisCancellation)) IsBusy = false;
        }
    }

    [RelayCommand] private void Cancel() { _analysisCancellation?.Cancel(); _clippingCancellation?.Cancel(); _displayCancellation?.Cancel(); }

    [RelayCommand]
    private void ClearPinnedProbe()
    {
        if (!HasPinnedProbe) return;
        _pinnedX = _pinnedY = null;
        CurrentProbe = null;
        ProbeSummary = "固定探针已清除；移动鼠标可继续预览。";
        OnPropertyChanged(nameof(HasPinnedProbe));
        MarkChanged();
    }

    /// <summary>由 View 转发 Pointer 与控件尺寸；坐标和像素公式仍由专用服务处理。</summary>
    internal void UpdatePointer(double x, double y, double width, double height, bool pin)
    {
        var session = _session;
        if (session is null) return;
        var mapping = _coordinateMapper.Map(x, y, width, height,
            session.Analysis.SourceSize.Width, session.Analysis.SourceSize.Height);
        if (!mapping.IsInside) { if (!HasPinnedProbe) CurrentProbe = null; return; }
        CurrentProbe = _inspectUseCase.Execute(session, mapping.SourceX, mapping.SourceY);
        RefreshProbeText(CurrentProbe);
        if (!pin) return;
        _pinnedX = mapping.NormalizedX;
        _pinnedY = mapping.NormalizedY;
        OnPropertyChanged(nameof(HasPinnedProbe));
        MarkChanged();
    }

    internal void LeaveSourcePreview()
    {
        if (HasPinnedProbe) RestorePinnedProbe();
        else { CurrentProbe = null; ProbeSummary = "鼠标已离开源图；没有固定探针。"; }
    }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = new ImageOscilloscopeSnapshotState(WaveformVisible, ParadeVisible, VectorscopeVisible,
            HistogramVisible, ResolveDensityMode(), ShadowThreshold, HighlightThreshold,
            ResolveClippingMode(), _pinnedX, _pinnedY, ViewZoom);
        var payload = JsonSerializer.SerializeToElement(state);
        return ValueTask.FromResult(new DocumentSaveSnapshot(new DocumentRevision(_revision),
            new DocumentContent(ImageOscilloscopeProtocol.SnapshotSchema, payload)));
    }

    public void AcceptChanges(DocumentRevision savedRevision)
    {
        var dirty = IsDirty;
        if (savedRevision.Value == _revision) _acceptedRevision = _revision;
        if (dirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_analysisGeneration; ++_displayGeneration;
        CancelAndDispose(ref _analysisCancellation); CancelAndDispose(ref _clippingCancellation); CancelAndDispose(ref _displayCancellation);
        ReplaceSession(null);
        ReplaceBitmap(ref _sourcePreview, null, nameof(SourcePreview));
        ReplaceBitmap(ref _clippingOverlay, null, nameof(ClippingOverlay));
        ReplaceBitmap(ref _waveformImage, null, nameof(WaveformImage));
        ReplaceBitmap(ref _paradeImage, null, nameof(ParadeImage));
        ReplaceBitmap(ref _vectorscopeImage, null, nameof(VectorscopeImage));
    }

    partial void OnSourcePathChanged(string value) { if (!_restoring) { StatusMessage = "输入路径已改变；请显式点击“分析”。"; MarkChanged(); } }
    partial void OnSelectedDensityModeChanged(string value) { if (!_restoring) { MarkChanged(); _ = RefreshScopeImagesAsync(); } }
    partial void OnSelectedClippingModeChanged(string value) { if (!_restoring) { MarkChanged(); _ = RefreshOverlayAsync(); } }
    partial void OnShadowThresholdChanged(int value) => ThresholdChanged();
    partial void OnHighlightThresholdChanged(int value) => ThresholdChanged();
    partial void OnWaveformVisibleChanged(bool value) { if (!_restoring) MarkChanged(); }
    partial void OnParadeVisibleChanged(bool value) { if (!_restoring) MarkChanged(); }
    partial void OnVectorscopeVisibleChanged(bool value) { if (!_restoring) MarkChanged(); }
    partial void OnHistogramVisibleChanged(bool value) { if (!_restoring) MarkChanged(); }
    partial void OnViewZoomChanged(double value) { if (!_restoring) { if (!double.IsFinite(value) || value is < 0.5d or > 4d) ViewZoom = 1d; else MarkChanged(); } }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsOperationBusy));
    partial void OnIsUpdatingClippingChanged(bool value) => OnPropertyChanged(nameof(IsOperationBusy));

    private void ThresholdChanged()
    {
        if (_restoring) return;
        MarkChanged();
        if (_session is not null) _ = RecalculateClippingAsync();
    }

    private async Task RecalculateClippingAsync()
    {
        var session = _session;
        if (session is null) return;
        ClippingThresholds thresholds;
        try { thresholds = CurrentThresholds(); }
        catch (Exception exception) { StatusMessage = exception.Message; return; }
        CancelAndDispose(ref _clippingCancellation);
        _clippingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _clippingCancellation;
        var token = current.Token;
        var generation = session.AdvanceClippingGeneration();
        var fingerprint = session.SourceFingerprint;
        IsUpdatingClipping = true;
        try
        {
            await Task.Delay(120, token).ConfigureAwait(true);
            var candidate = await _clippingUseCase.ExecuteAsync(session, thresholds, generation, token).ConfigureAwait(true);
            if (!ReferenceEquals(session, _session) || !session.TryCommitClipping(candidate, generation, fingerprint)) return;
            await RefreshOverlayAsync().ConfigureAwait(true);
            RefreshClippingText();
            StatusMessage = "裁切阈值已更新；主 Scope 与直方图未重新分析。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { if (ReferenceEquals(session, _session)) StatusMessage = $"裁切更新失败：{exception.Message}"; }
        finally { if (ReferenceEquals(current, _clippingCancellation)) IsUpdatingClipping = false; }
    }

    private async Task RefreshScopeImagesAsync()
    {
        var session = _session;
        if (session is null) return;
        CancelAndDispose(ref _displayCancellation);
        _displayCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var token = _displayCancellation.Token;
        var generation = ++_displayGeneration;
        Bitmap[]? values = null;
        try
        {
            var raster = _displayUseCase.Rasterize(
                _displayUseCase.Project(session, ResolveDensityMode(), token), token);
            values = await CreateBitmapsAsync([raster.Waveform, raster.Parade, raster.Vectorscope], token).ConfigureAwait(true);
            if (generation != _displayGeneration || !ReferenceEquals(session, _session) || _lifetime.IsClosing) return;
            ReplaceBitmap(ref _waveformImage, values[0], nameof(WaveformImage)); values[0] = null!;
            ReplaceBitmap(ref _paradeImage, values[1], nameof(ParadeImage)); values[1] = null!;
            ReplaceBitmap(ref _vectorscopeImage, values[2], nameof(VectorscopeImage)); values[2] = null!;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { if (generation == _displayGeneration) StatusMessage = $"显示投影失败：{exception.Message}"; }
        finally { if (values is not null) DisposeAll(values.Where(value => value is not null)); }
    }

    private async Task RefreshOverlayAsync()
    {
        var session = _session;
        if (session is null) return;
        var image = _displayUseCase.CreateClippingOverlay(session, ResolveClippingMode(), _lifetime.ClosingToken);
        var bitmap = await CreateBitmapAsync(image, _lifetime.ClosingToken).ConfigureAwait(true);
        if (!ReferenceEquals(session, _session) || _lifetime.IsClosing) { bitmap.Dispose(); return; }
        ReplaceBitmap(ref _clippingOverlay, bitmap, nameof(ClippingOverlay));
    }

    private async Task<Bitmap[]> CreateCompleteBitmapSetAsync(ImageOscilloscopeSession session, CancellationToken token)
    {
        var densities = _displayUseCase.Project(session, ResolveDensityMode(), token);
        var raster = _displayUseCase.Rasterize(densities, token);
        var overlay = _displayUseCase.CreateClippingOverlay(session, ResolveClippingMode(), token);
        return await CreateBitmapsAsync([session.Preview, overlay, raster.Waveform, raster.Parade, raster.Vectorscope], token).ConfigureAwait(true);
    }

    private async Task<Bitmap[]> CreateBitmapsAsync(IReadOnlyList<PixelImage> images, CancellationToken token)
    {
        var values = new List<Bitmap>(images.Count);
        try { foreach (var image in images) values.Add(await CreateBitmapAsync(image, token).ConfigureAwait(true)); return values.ToArray(); }
        catch { DisposeAll(values); throw; }
    }

    private async Task<Bitmap> CreateBitmapAsync(PixelImage image, CancellationToken token)
    {
        var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, writable: false);
        return new Bitmap(stream);
    }

    private void CommitCompleteBitmapSet(Bitmap[] values)
    {
        ReplaceBitmap(ref _sourcePreview, values[0], nameof(SourcePreview));
        ReplaceBitmap(ref _clippingOverlay, values[1], nameof(ClippingOverlay));
        ReplaceBitmap(ref _waveformImage, values[2], nameof(WaveformImage));
        ReplaceBitmap(ref _paradeImage, values[3], nameof(ParadeImage));
        ReplaceBitmap(ref _vectorscopeImage, values[4], nameof(VectorscopeImage));
    }

    private void RefreshAnalysisText()
    {
        var analysis = _session!.Analysis;
        AnalysisSummary = $"{analysis.SourceSize.Width}×{analysis.SourceSize.Height} / {analysis.PixelCount:N0} 像素；" +
            $"Waveform {analysis.Waveform.Width}×256；平均 Cb/Cr={analysis.MeanCb:F4}/{analysis.MeanCr:F4}；平均色度半径={analysis.MeanChromaRadius:F4}；Hue 有定义 {analysis.HueDefinedCount:N0}。";
        RefreshClippingText();
        OnPropertyChanged(nameof(Analysis)); OnPropertyChanged(nameof(HasAnalysis));
    }

    private void RefreshClippingText()
    {
        var clipping = _session!.CurrentClipping;
        var c = clipping.Counts;
        ClippingSummary = $"阈值 ≤{clipping.Thresholds.Shadow}/≥{clipping.Thresholds.Highlight}；" +
            $"Y 阴影/高光 {c.LumaShadow:N0}/{c.LumaHighlight:N0}；RGB 任一 {c.RgbShadow:N0}/{c.RgbHighlight:N0}；" +
            $"R {c.RedShadow:N0}/{c.RedHighlight:N0}，G {c.GreenShadow:N0}/{c.GreenHighlight:N0}，B {c.BlueShadow:N0}/{c.BlueHighlight:N0}。";
    }

    private void RefreshProbeText(ScopeProbe probe)
    {
        var p = probe.Pixel;
        ProbeSummary = $"源 ({probe.SourceX},{probe.SourceY}) RGBA可见=({p.Red},{p.Green},{p.Blue}) / A={p.Alpha}；" +
            $"Y={p.Luma}，Cb/Cr={p.Cb.ToString("F4", CultureInfo.InvariantCulture)}/{p.Cr.ToString("F4", CultureInfo.InvariantCulture)}，" +
            $"S={p.Saturation.ToString("F4", CultureInfo.InvariantCulture)}，Hue={(p.Hue?.ToString("F2", CultureInfo.InvariantCulture) ?? "N/A")}；" +
            $"Waveform=({probe.Waveform.X},{probe.Waveform.Y})，Vector=({probe.Vectorscope.X},{probe.Vectorscope.Y})。";
    }

    private void RestorePinnedProbe()
    {
        var session = _session;
        if (session is null || _pinnedX is not { } x || _pinnedY is not { } y) return;
        var sourceX = Math.Min(session.Analysis.SourceSize.Width - 1, (int)Math.Floor(Math.Clamp(x, 0d, 0.999999999d) * session.Analysis.SourceSize.Width));
        var sourceY = Math.Min(session.Analysis.SourceSize.Height - 1, (int)Math.Floor(Math.Clamp(y, 0d, 0.999999999d) * session.Analysis.SourceSize.Height));
        CurrentProbe = _inspectUseCase.Execute(session, sourceX, sourceY);
        RefreshProbeText(CurrentProbe);
    }

    private void Restore(DocumentContent content)
    {
        SourcePath = string.Empty;
        if (content.SchemaVersion != ImageOscilloscopeProtocol.SnapshotSchema)
        { StatusMessage = "快照版本不受支持；已使用安全默认值，且不会自动读取图片。"; return; }
        try
        {
            var state = content.Payload.Deserialize<ImageOscilloscopeSnapshotState>();
            if (state is null) return;
            _ = new ClippingThresholds(state.ShadowThreshold, state.HighlightThreshold);
            if (!Enum.IsDefined(state.DensityMode) || !Enum.IsDefined(state.ClippingMode) ||
                !double.IsFinite(state.Zoom) || state.Zoom is < 0.5d or > 4d ||
                !ValidNormalized(state.PinnedX) || !ValidNormalized(state.PinnedY))
                throw new InvalidDataException("快照包含非法枚举、缩放或探针坐标。");
            WaveformVisible = state.WaveformVisible; ParadeVisible = state.ParadeVisible;
            VectorscopeVisible = state.VectorscopeVisible; HistogramVisible = state.HistogramVisible;
            SelectedDensityMode = state.DensityMode == ScopeDensityMode.Logarithmic ? "对数" : "线性";
            ShadowThreshold = state.ShadowThreshold; HighlightThreshold = state.HighlightThreshold;
            SelectedClippingMode = state.ClippingMode switch { ScopeClippingMode.Off => "关闭", ScopeClippingMode.Luma => "亮度", _ => "RGB 任一通道" };
            _pinnedX = state.PinnedX; _pinnedY = state.PinnedY; ViewZoom = state.Zoom;
            OnPropertyChanged(nameof(HasPinnedProbe));
            StatusMessage = "已恢复轻量视图参数；快照不含路径，请重新选择图片分析。";
        }
        catch (Exception exception) { StatusMessage = $"快照无效：{exception.Message}；已保留安全默认状态。"; }
    }

    private ClippingThresholds CurrentThresholds() => new(ShadowThreshold, HighlightThreshold);
    private ScopeDensityMode ResolveDensityMode() => SelectedDensityMode == "线性" ? ScopeDensityMode.Linear : ScopeDensityMode.Logarithmic;
    private ScopeClippingMode ResolveClippingMode() => SelectedClippingMode switch { "关闭" => ScopeClippingMode.Off, "RGB 任一通道" => ScopeClippingMode.RgbAny, _ => ScopeClippingMode.Luma };
    private bool CanCommitAnalysis(long generation) => generation == _analysisGeneration && !_disposed && !_lifetime.IsClosing;
    private static bool ValidNormalized(double? value) => value is null || double.IsFinite(value.Value) && value.Value is >= 0d and < 1d;

    private void ReplaceSession(ImageOscilloscopeSession? value) { var previous = _session; _session = value; previous?.Dispose(); }
    private void ReplaceBitmap(ref Bitmap? field, Bitmap? value, string propertyName) { var previous = field; field = value; OnPropertyChanged(propertyName); if (!ReferenceEquals(previous, value)) previous?.Dispose(); }
    private static void DisposeAll(IEnumerable<Bitmap> values) { foreach (var value in values) value.Dispose(); }
    private static void CancelAndDispose(ref CancellationTokenSource? source) { source?.Cancel(); source?.Dispose(); source = null; }
    private void MarkChanged() { if (_restoring) return; var dirty = IsDirty; _revision++; if (dirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }
}
