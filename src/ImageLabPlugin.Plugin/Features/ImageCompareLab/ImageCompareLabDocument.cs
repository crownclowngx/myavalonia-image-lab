using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.ImageComparison;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.ImageCompareLab;

/// <summary>“图像比较实验室”Document：拥有一对图片、一份比较 Session 和轻量可恢复交互配方。</summary>
/// <remarks>
/// Document 只协调窄应用用例、Bitmap 展示资源、generation 与 UI 状态；完整像素扫描、图片解码、差异投影和
/// JSON 写入均不在此类实现。路径变化立即切断旧摘要的导出资格。即使底层替身忽略取消，generation、路径与
/// Session 身份三重检查也会拒绝迟到结果。快照不保存像素、Bitmap、指标或直方图，恢复后不会自动读取文件。
/// </remarks>
internal sealed partial class ImageCompareLabDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private readonly IPrepareImageComparisonUseCase _prepareUseCase;
    private readonly IProjectImageDifferenceUseCase _projectUseCase;
    private readonly IInspectImagePairUseCase _inspectUseCase;
    private readonly IExportComparisonSummaryUseCase _exportUseCase;
    private readonly IImageFileDialog _imageDialog;
    private readonly IComparisonReportFileDialog _reportDialog;
    private readonly ITextClipboard _clipboard;
    private readonly IImageCodec _codec;
    private readonly IDocumentLifetime _lifetime;
    private readonly Timer _blinkTimer;
    private readonly SynchronizationContext? _uiContext;
    private bool _isBlinkTimerRunning;
    private DocumentPresentationState _presentation = new("图像比较实验室");
    private ImageComparisonSession? _session;
    private ImageComparisonReport? _report;
    private CancellationTokenSource? _comparisonCancellation;
    private CancellationTokenSource? _projectionCancellation;
    private long _generation;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;
    private bool _disposed;

    public ImageCompareLabDocument(
        IPrepareImageComparisonUseCase prepareUseCase,
        IProjectImageDifferenceUseCase projectUseCase,
        IInspectImagePairUseCase inspectUseCase,
        IExportComparisonSummaryUseCase exportUseCase,
        IImageFileDialog imageDialog,
        IComparisonReportFileDialog reportDialog,
        ITextClipboard clipboard,
        IImageCodec codec,
        IDocumentLifetime lifetime)
    {
        _prepareUseCase = prepareUseCase;
        _projectUseCase = projectUseCase;
        _inspectUseCase = inspectUseCase;
        _exportUseCase = exportUseCase;
        _imageDialog = imageDialog;
        _reportDialog = reportDialog;
        _clipboard = clipboard;
        _codec = codec;
        _lifetime = lifetime;
        _uiContext = SynchronizationContext.Current;
        _blinkTimer = new Timer(_ =>
        {
            if (_uiContext is null) TickBlink();
            else _uiContext.Post(static state => ((ImageCompareLabDocument)state!).TickBlink(), this);
        }, null, Timeout.Infinite, Timeout.Infinite);
    }

    [ObservableProperty] private string _referencePath = string.Empty;
    [ObservableProperty] private string _candidatePath = string.Empty;
    [ObservableProperty] private string _selectedMode = "并排";
    [ObservableProperty] private double _splitRatio = 0.5d;
    [ObservableProperty] private double _overlayOpacity = 0.5d;
    [ObservableProperty] private int _blinkIntervalMilliseconds = 500;
    [ObservableProperty] private bool _isBlinkPaused = true;
    [ObservableProperty] private bool _showCandidateBlinkFrame;
    [ObservableProperty] private int _differenceAmplification = 4;
    [ObservableProperty] private string _selectedHeatmapSource = "MaxRGB";
    [ObservableProperty] private string _selectedHistogramChannel = "Y";
    [ObservableProperty] private bool _useLogarithmicHistogram;
    [ObservableProperty] private double _zoom;
    [ObservableProperty] private double _viewportCenterX = 0.5d;
    [ObservableProperty] private double _viewportCenterY = 0.5d;
    [ObservableProperty] private bool _showCrosshair = true;
    [ObservableProperty] private int _selectedSourceX;
    [ObservableProperty] private int _selectedSourceY;
    [ObservableProperty] private int _selectedProxyX;
    [ObservableProperty] private int _selectedProxyY;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "请选择参考图和待比较图。";
    [ObservableProperty] private string _sizeSummary = "尚未比较";
    [ObservableProperty] private string _metricsSummary = "比较完成后显示 PSNR、全局 SSIM-Y 与误差统计。";
    [ObservableProperty] private string _pixelSummary = "在视口悬停，或输入原图 x/y 后检查像素。";
    [ObservableProperty] private string _projectionSummary = "RGB 差异倍率 ×4；热力图使用固定量纲。";
    [ObservableProperty] private IReadOnlyList<long> _referenceHistogramBins = Array.Empty<long>();
    [ObservableProperty] private IReadOnlyList<long> _candidateHistogramBins = Array.Empty<long>();
    [ObservableProperty] private Bitmap? _referencePreview;
    [ObservableProperty] private Bitmap? _candidatePreview;
    [ObservableProperty] private Bitmap? _projectionPreview;

    public IReadOnlyList<string> ModeOptions { get; } = ["并排", "分割", "叠加", "闪烁", "RGB 差异", "热力图"];
    public IReadOnlyList<int> AmplificationOptions { get; } = [1, 2, 4, 8, 16, 32];
    public IReadOnlyList<string> HeatmapSourceOptions { get; } = ["MaxRGB", "Y"];
    public IReadOnlyList<string> HistogramChannelOptions { get; } = ["R", "G", "B", "Y", "Cb", "Cr"];
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasSession => _session is not null;
    public bool HasSummary => _report is not null;
    public bool IsBlinkTimerRunning => _isBlinkTimerRunning;
    public ComparisonDisplayMode DisplayMode => SelectedMode switch
    {
        "分割" => ComparisonDisplayMode.Split,
        "叠加" => ComparisonDisplayMode.Overlay,
        "闪烁" => ComparisonDisplayMode.Blink,
        "RGB 差异" => ComparisonDisplayMode.Difference,
        "热力图" => ComparisonDisplayMode.Heatmap,
        _ => ComparisonDisplayMode.SideBySide
    };

    public event EventHandler? PresentationChanged;
    public event EventHandler? IsDirtyChanged;

    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation); cancellationToken.ThrowIfCancellationRequested();
        _restoring = true;
        try
        {
            if (activation is RestoreDocumentActivation restore) Restore(restore.RestoredContent);
            _presentation = new DocumentPresentationState(string.IsNullOrWhiteSpace(activation.Title) ? "图像比较实验室" : activation.Title);
            PresentationChanged?.Invoke(this, EventArgs.Empty);
            _revision = _acceptedRevision = 0;
        }
        finally { _restoring = false; }
        return ValueTask.CompletedTask;
    }

    [RelayCommand]
    private async Task SelectReferenceAsync()
    {
        var path = await _imageDialog.PickImageAsync(_lifetime.ClosingToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path)) ReferencePath = path;
    }

    [RelayCommand]
    private async Task SelectCandidateAsync()
    {
        var path = await _imageDialog.PickImageAsync(_lifetime.ClosingToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path)) CandidatePath = path;
    }

    [RelayCommand]
    private async Task SwapAsync()
    {
        var reference = ReferencePath; var candidate = CandidatePath;
        ReferencePath = candidate; CandidatePath = reference;
        if (!string.IsNullOrWhiteSpace(ReferencePath) && !string.IsNullOrWhiteSpace(CandidatePath))
            await CompareAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        if (string.IsNullOrWhiteSpace(ReferencePath) || string.IsNullOrWhiteSpace(CandidatePath))
        { StatusMessage = "请先选择参考图和待比较图。"; return; }
        CancelAndDispose(ref _comparisonCancellation); CancelAndDispose(ref _projectionCancellation);
        _comparisonCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _comparisonCancellation; var token = current.Token; var generation = ++_generation;
        var referencePath = ReferencePath; var candidatePath = CandidatePath;
        IsBusy = true; StatusMessage = "正在顺序解码并执行全分辨率比较…";
        try
        {
            var result = await _prepareUseCase.ExecuteAsync(new ImageComparisonRequest(referencePath, candidatePath), token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            if (!CanCommit(generation, referencePath, candidatePath)) { result.Session?.Dispose(); return; }
            await ReplaceResultAsync(result, referencePath, candidatePath, generation, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { if (!_lifetime.IsClosing && generation == _generation) StatusMessage = "比较已取消。"; }
        catch (Exception exception) { if (generation == _generation) StatusMessage = $"比较失败：{exception.Message}"; }
        finally { if (ReferenceEquals(_comparisonCancellation, current)) IsBusy = false; }
    }

    [RelayCommand] private void Cancel() { _comparisonCancellation?.Cancel(); _projectionCancellation?.Cancel(); }

    [RelayCommand]
    private async Task CopySummaryAsync()
    {
        var report = _report; if (report is null) { StatusMessage = "没有属于当前图片的可复制摘要。"; return; }
        try
        {
            var copied = await _clipboard.TrySetTextAsync(_exportUseCase.CreateHumanReadableText(report), _lifetime.ClosingToken).ConfigureAwait(true);
            StatusMessage = copied ? "比较摘要已复制。" : "剪贴板不可用；有效比较结果仍已保留，请重试。";
        }
        catch (Exception exception) { StatusMessage = $"复制失败：{exception.Message}"; }
    }

    [RelayCommand]
    private async Task ExportSummaryAsync()
    {
        var report = _report; if (report is null) { StatusMessage = "没有属于当前图片的可导出摘要。"; return; }
        var suggested = $"{Path.GetFileNameWithoutExtension(ReferencePath)}-vs-{Path.GetFileNameWithoutExtension(CandidatePath)}.image-compare.json";
        var path = await _reportDialog.PickSummaryOutputAsync(suggested, _lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            await _exportUseCase.ExecuteAsync(report, path, _lifetime.ClosingToken).ConfigureAwait(false);
            StatusMessage = $"已原子导出比较摘要：{path}";
        }
        catch (Exception exception) { StatusMessage = $"导出失败：{exception.Message}"; }
    }

    [RelayCommand] private void FitViewport() => Zoom = 0d;
    [RelayCommand] private void ActualPixels() => Zoom = 1d;
    [RelayCommand] private void ZoomIn() => Zoom = Math.Clamp(Zoom <= 0d ? 1d : Zoom * 2d, 0.25d, 16d);
    [RelayCommand] private void ZoomOut() => Zoom = Math.Clamp(Zoom <= 0d ? 0.5d : Zoom / 2d, 0.25d, 16d);

    [RelayCommand]
    private void InspectPixel()
    {
        var session = _session; if (session is null) return;
        var x = Math.Clamp(SelectedSourceX, 0, session.ReferenceImage.Size.Width - 1);
        var y = Math.Clamp(SelectedSourceY, 0, session.ReferenceImage.Size.Height - 1);
        ApplyPixelReport(_inspectUseCase.Execute(session, new ImagePoint(x, y)));
    }

    internal void InspectProxyAt(ImagePoint proxyPoint)
    {
        var session = _session; if (session is null) return;
        var x = Math.Clamp((int)((proxyPoint.X + 0.5d) * session.ReferenceImage.Size.Width / session.ReferenceProxy.Size.Width), 0, session.ReferenceImage.Size.Width - 1);
        var y = Math.Clamp((int)((proxyPoint.Y + 0.5d) * session.ReferenceImage.Size.Height / session.ReferenceProxy.Size.Height), 0, session.ReferenceImage.Size.Height - 1);
        ApplyPixelReport(_inspectUseCase.Execute(session, new ImagePoint(x, y)));
    }

    internal void TickBlink()
    {
        if (DisplayMode != ComparisonDisplayMode.Blink || IsBlinkPaused || _session is null) return;
        ShowCandidateBlinkFrame = !ShowCandidateBlinkFrame;
    }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.SerializeToElement(new Snapshot(
            ReferencePath, CandidatePath, SelectedMode, SplitRatio, OverlayOpacity, BlinkIntervalMilliseconds,
            DifferenceAmplification, SelectedHeatmapSource, SelectedHistogramChannel, UseLogarithmicHistogram,
            Zoom, ViewportCenterX, ViewportCenterY, ShowCrosshair));
        return ValueTask.FromResult(new DocumentSaveSnapshot(new DocumentRevision(_revision), new DocumentContent(SnapshotSchema, payload)));
    }

    public void AcceptChanges(DocumentRevision savedRevision)
    {
        var wasDirty = IsDirty; if (savedRevision.Value == _revision) _acceptedRevision = _revision;
        if (wasDirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_generation;
        StopBlinkTimer(); _blinkTimer.Dispose(); CancelAndDispose(ref _comparisonCancellation); CancelAndDispose(ref _projectionCancellation);
        _session?.Dispose(); _session = null; _report = null;
        ReplaceReferenceBitmap(null); ReplaceCandidateBitmap(null); ReplaceProjectionBitmap(null);
    }

    partial void OnReferencePathChanged(string value) { if (!_restoring) { InvalidateComparison("参考图已改变，请重新比较。"); MarkChanged(); } }
    partial void OnCandidatePathChanged(string value) { if (!_restoring) { InvalidateComparison("待比较图已改变，请重新比较。"); MarkChanged(); } }
    partial void OnSelectedModeChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayMode)); if (_restoring) return; MarkChanged(); UpdateBlinkTimer();
        if (DisplayMode is ComparisonDisplayMode.Difference or ComparisonDisplayMode.Heatmap) QueueProjection();
    }
    partial void OnSplitRatioChanged(double value) { if (!_restoring) MarkChanged(); }
    partial void OnOverlayOpacityChanged(double value) { if (!_restoring) MarkChanged(); }
    partial void OnBlinkIntervalMillisecondsChanged(int value)
    { if (!_restoring) { MarkChanged(); UpdateBlinkTimer(); } }
    partial void OnIsBlinkPausedChanged(bool value) => UpdateBlinkTimer();
    partial void OnDifferenceAmplificationChanged(int value) { if (!_restoring) { MarkChanged(); QueueProjection(); } }
    partial void OnSelectedHeatmapSourceChanged(string value) { if (!_restoring) { MarkChanged(); QueueProjection(); } }
    partial void OnSelectedHistogramChannelChanged(string value) { RefreshHistogram(); if (!_restoring) MarkChanged(); }
    partial void OnUseLogarithmicHistogramChanged(bool value) { if (!_restoring) MarkChanged(); }
    partial void OnZoomChanged(double value) { if (!_restoring) MarkChanged(); }
    partial void OnViewportCenterXChanged(double value) { if (!_restoring) MarkChanged(); }
    partial void OnViewportCenterYChanged(double value) { if (!_restoring) MarkChanged(); }
    partial void OnShowCrosshairChanged(bool value) { if (!_restoring) MarkChanged(); }

    private async Task ReplaceResultAsync(ImageComparisonResult result, string referencePath, string candidatePath, long generation, CancellationToken token)
    {
        Bitmap? referenceBitmap = null;
        Bitmap? candidateBitmap = null;
        var sessionTransferred = false;
        try
        {
            referenceBitmap = await CreateBitmapAsync(result.ReferencePreview, token).ConfigureAwait(true);
            candidateBitmap = await CreateBitmapAsync(result.CandidatePreview, token).ConfigureAwait(true);
            if (!CanCommit(generation, referencePath, candidatePath)) return;
            _session?.Dispose(); _session = result.Session; sessionTransferred = result.Session is not null;
            ReplaceReferenceBitmap(referenceBitmap); referenceBitmap = null;
            ReplaceCandidateBitmap(candidateBitmap); candidateBitmap = null;
            ReplaceProjectionBitmap(null);
            _report = new ImageComparisonReport(1, Path.GetFileName(referencePath), Path.GetFileName(candidatePath), DateTimeOffset.UtcNow, result.Summary);
            OnPropertyChanged(nameof(HasSession)); OnPropertyChanged(nameof(HasSummary));
            if (result.Mismatch is not null)
            {
                SelectedMode = "并排";
                SizeSummary = result.Mismatch.ToUserMessage(); MetricsSummary = "未计算指标与直方图。";
                ReferenceHistogramBins = CandidateHistogramBins = Array.Empty<long>(); StatusMessage = SizeSummary; UpdateBlinkTimer(); return;
            }

            var session = result.Session!; var metrics = session.Summary.Metrics!;
            SizeSummary = $"原图 {session.ReferenceImage.Size.Width}×{session.ReferenceImage.Size.Height}；显示代理 {session.ReferenceProxy.Size.Width}×{session.ReferenceProxy.Size.Height}";
            MetricsSummary = $"PSNR-Y {FormatPsnr(metrics.PsnrLumaDb)}；PSNR-RGB {FormatPsnr(metrics.PsnrRgbDb)}；全局 SSIM-Y {metrics.GlobalSsimLuma:F8}\n" +
                $"RGB MSE {metrics.MeanSquaredErrorRgb:F6}；MAE {metrics.MeanAbsoluteErrorRgb:F6}；RMSE {metrics.RootMeanSquareErrorRgb:F6}；最大 {metrics.MaximumAbsoluteErrorRgb}\n" +
                $"RGB 变化 {metrics.ChangedPixelCountRgb:N0}（{metrics.ChangedPixelRatioRgb:P4}）；Alpha 变化 {metrics.ChangedPixelCountAlpha:N0}（{metrics.ChangedPixelRatioAlpha:P4}）";
            RefreshHistogram(); UpdateBlinkTimer(); StatusMessage = "比较完成；指标与直方图来自完整解码像素，视觉视口使用有界代理。";
            await RefreshProjectionAsync(generation, session).ConfigureAwait(true);
        }
        finally
        {
            referenceBitmap?.Dispose(); candidateBitmap?.Dispose();
            if (!sessionTransferred) result.Session?.Dispose();
        }
    }

    private void QueueProjection()
    {
        if (_session is null) return; var generation = _generation; var session = _session;
        _ = RefreshProjectionAsync(generation, session, debounce: true);
    }

    private async Task RefreshProjectionAsync(long generation, ImageComparisonSession session, bool debounce = false)
    {
        CancelAndDispose(ref _projectionCancellation);
        _projectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _projectionCancellation; var token = current.Token;
        try
        {
            if (debounce) await Task.Delay(120, token).ConfigureAwait(true);
            var options = new DifferenceProjectionOptions(
                DisplayMode == ComparisonDisplayMode.Heatmap ? DifferenceProjectionKind.Heatmap : DifferenceProjectionKind.Rgb,
                DifferenceAmplification,
                SelectedHeatmapSource == "Y" ? HeatmapScalarSource.Luma : HeatmapScalarSource.MaximumRgb);
            var result = await Task.Run(() => _projectUseCase.Execute(session, options, token), token).ConfigureAwait(true);
            var bitmap = await CreateBitmapAsync(result.Image, token).ConfigureAwait(true);
            if (generation != _generation || !ReferenceEquals(session, _session) || _lifetime.IsClosing)
            { bitmap.Dispose(); return; }
            ReplaceProjectionBitmap(bitmap);
            if (_report is not null)
            {
                _report = _report with
                {
                    Projection = new ImageComparisonProjectionReport(
                        options.Kind,
                        options.Amplification,
                        options.Kind == DifferenceProjectionKind.Heatmap ? options.HeatmapSource : null,
                        result.SaturatedProxyPixelCount)
                };
            }
            ProjectionSummary = $"{(options.Kind == DifferenceProjectionKind.Rgb ? "RGB 绝对差异" : $"{SelectedHeatmapSource} 固定量纲热力图")} ×{options.Amplification}；代理饱和 {result.SaturatedProxyPixelCount:N0} 像素。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { if (generation == _generation) StatusMessage = $"投影失败：{exception.Message}"; }
    }

    private async Task<Bitmap> CreateBitmapAsync(PixelImage image, CancellationToken token)
    {
        var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, writable: false); return new Bitmap(stream);
    }

    private void ApplyPixelReport(ImagePairPixelReport report)
    {
        SelectedSourceX = report.Point.X; SelectedSourceY = report.Point.Y;
        var session = _session!;
        SelectedProxyX = Math.Clamp((int)((report.Point.X + 0.5d) * session.ReferenceProxy.Size.Width / session.ReferenceImage.Size.Width), 0, session.ReferenceProxy.Size.Width - 1);
        SelectedProxyY = Math.Clamp((int)((report.Point.Y + 0.5d) * session.ReferenceProxy.Size.Height / session.ReferenceImage.Size.Height), 0, session.ReferenceProxy.Size.Height - 1);
        PixelSummary = $"原图 ({report.Point.X},{report.Point.Y})\n参考 RGBA=({report.Reference.R},{report.Reference.G},{report.Reference.B},{report.Reference.A})，Y={report.ReferenceLuma:F3}\n" +
            $"待比较 RGBA=({report.Candidate.R},{report.Candidate.G},{report.Candidate.B},{report.Candidate.A})，Y={report.CandidateLuma:F3}\n" +
            $"Δ(Candidate-Reference)=({report.DeltaRed:+#;-#;0},{report.DeltaGreen:+#;-#;0},{report.DeltaBlue:+#;-#;0},{report.DeltaAlpha:+#;-#;0})，ΔY={report.DeltaLuma:+0.###;-0.###;0}；最大 RGB |Δ|={report.MaximumRgbDifference}" +
            (report.IsAlphaOnlyChange ? "；仅 Alpha 变化" : string.Empty);
    }

    private void RefreshHistogram()
    {
        var histograms = _session?.Summary.Histograms;
        if (histograms is null) { ReferenceHistogramBins = CandidateHistogramBins = Array.Empty<long>(); return; }
        var channel = SelectedHistogramChannel switch
        { "R" => ImageChannel.Red, "G" => ImageChannel.Green, "B" => ImageChannel.Blue, "Cb" => ImageChannel.ChromaBlue, "Cr" => ImageChannel.ChromaRed, _ => ImageChannel.Luma };
        ReferenceHistogramBins = histograms.Reference.GetBins(channel);
        CandidateHistogramBins = histograms.Candidate.GetBins(channel);
    }

    private void UpdateBlinkTimer()
    {
        var shouldRun = DisplayMode == ComparisonDisplayMode.Blink && !IsBlinkPaused && _session is not null && !_lifetime.IsClosing;
        if (shouldRun)
        {
            var interval = Math.Clamp(BlinkIntervalMilliseconds, 250, 2000);
            _blinkTimer.Change(interval, interval); _isBlinkTimerRunning = true;
        }
        else StopBlinkTimer();
    }

    private void InvalidateComparison(string status)
    {
        ++_generation; StopBlinkTimer(); CancelAndDispose(ref _comparisonCancellation); CancelAndDispose(ref _projectionCancellation);
        _session?.Dispose(); _session = null; _report = null;
        ReplaceReferenceBitmap(null); ReplaceCandidateBitmap(null); ReplaceProjectionBitmap(null);
        ReferenceHistogramBins = CandidateHistogramBins = Array.Empty<long>(); SizeSummary = "尚未比较";
        MetricsSummary = "比较完成后显示 PSNR、全局 SSIM-Y 与误差统计。"; PixelSummary = "在视口悬停，或输入原图 x/y 后检查像素。";
        StatusMessage = status; OnPropertyChanged(nameof(HasSession)); OnPropertyChanged(nameof(HasSummary));
    }

    private bool CanCommit(long generation, string referencePath, string candidatePath) =>
        generation == _generation && ReferencePath == referencePath && CandidatePath == candidatePath && !_lifetime.IsClosing && !_disposed;

    private void MarkChanged()
    {
        if (_restoring) return; var wasDirty = IsDirty; _revision++; if (!wasDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema) { StatusMessage = $"不支持 schema {content.SchemaVersion}，已使用安全默认值。"; return; }
        var value = content.Payload.Deserialize<Snapshot>(); if (value is null) return;
        ReferencePath = value.ReferencePath ?? string.Empty; CandidatePath = value.CandidatePath ?? string.Empty;
        SelectedMode = ModeOptions.Contains(value.Mode) ? value.Mode : "并排";
        SplitRatio = Math.Clamp(value.SplitRatio, 0d, 1d); OverlayOpacity = Math.Clamp(value.OverlayOpacity, 0d, 1d);
        BlinkIntervalMilliseconds = Math.Clamp(value.BlinkIntervalMilliseconds, 250, 2000);
        DifferenceAmplification = AmplificationOptions.Contains(value.DifferenceAmplification) ? value.DifferenceAmplification : 4;
        SelectedHeatmapSource = HeatmapSourceOptions.Contains(value.HeatmapSource) ? value.HeatmapSource : "MaxRGB";
        SelectedHistogramChannel = HistogramChannelOptions.Contains(value.HistogramChannel) ? value.HistogramChannel : "Y";
        UseLogarithmicHistogram = value.UseLogarithmicHistogram;
        Zoom = value.Zoom == 0d || (value.Zoom >= 0.25d && value.Zoom <= 16d) ? value.Zoom : 0d;
        ViewportCenterX = Math.Clamp(value.CenterX, 0d, 1d); ViewportCenterY = Math.Clamp(value.CenterY, 0d, 1d); ShowCrosshair = value.ShowCrosshair;
        StatusMessage = "已恢复路径和参数；为避免恢复布局时读取大文件，请显式点击“比较”。";
    }

    private static string FormatPsnr(double value) => double.IsPositiveInfinity(value) ? "∞" : $"{value:F4} dB";
    private void StopBlinkTimer()
    { _blinkTimer.Change(Timeout.Infinite, Timeout.Infinite); _isBlinkTimerRunning = false; ShowCandidateBlinkFrame = false; }
    private void ReplaceReferenceBitmap(Bitmap? replacement)
    { var previous = ReferencePreview; ReferencePreview = replacement; previous?.Dispose(); }
    private void ReplaceCandidateBitmap(Bitmap? replacement)
    { var previous = CandidatePreview; CandidatePreview = replacement; previous?.Dispose(); }
    private void ReplaceProjectionBitmap(Bitmap? replacement)
    { var previous = ProjectionPreview; ProjectionPreview = replacement; previous?.Dispose(); }
    private static void CancelAndDispose(ref CancellationTokenSource? source)
    { source?.Cancel(); source?.Dispose(); source = null; }

    private sealed record Snapshot(
        string? ReferencePath, string? CandidatePath, string Mode, double SplitRatio, double OverlayOpacity,
        int BlinkIntervalMilliseconds, int DifferenceAmplification, string HeatmapSource, string HistogramChannel,
        bool UseLogarithmicHistogram, double Zoom, double CenterX, double CenterY, bool ShowCrosshair);
}
