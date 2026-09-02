using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.Wavelets;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Wavelets;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.WaveletLab;

/// <summary>“小波实验室”多实例 Document：只拥有界面状态、Session、异步代次与 Bitmap 生命周期。</summary>
/// <remarks>
/// DWT、阈值、报告和水印循环均位于 Domain/Application/Infrastructure。参数变化会先推进 generation、
/// 取消旧任务，再清除完整尺寸结果和报告；异步提交还会核对 Session 引用与配方指纹，防止迟到结果覆盖
/// 新实验。快照只保存路径和轻量参数，恢复时不自动读取文件或执行计算。
/// </remarks>
internal sealed partial class WaveletLabDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private readonly IPrepareWaveletSessionUseCase _prepare;
    private readonly IDecomposeWaveletUseCase _decompose;
    private readonly IDenoiseWaveletUseCase _denoise;
    private readonly IReconstructWaveletLevelUseCase _reconstructLevel;
    private readonly IRunWaveletQualityScanUseCase _scan;
    private readonly IRunWatermarkCarrierBenchmarkUseCase _benchmark;
    private readonly IExportWaveletImageUseCase _exportImage;
    private readonly IExportWaveletReportUseCase _exportReport;
    private readonly IImageFileDialog _imageDialog;
    private readonly IWaveletReportFileDialog _reportDialog;
    private readonly IImageCodec _codec;
    private readonly IDocumentLifetime _lifetime;
    private DocumentPresentationState _presentation = new("小波实验室");
    private WaveletSession? _session;
    private WaveletAnalysisResult? _analysis;
    private WaveletDenoiseResult? _fullResult;
    private WaveletScanResult? _scanResult;
    private WatermarkCarrierBenchmarkReport? _benchmarkResult;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _benchmarkCancellation;
    private CancellationTokenSource? _exportCancellation;
    private long _generation;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;
    private bool _disposed;

    public WaveletLabDocument(
        IPrepareWaveletSessionUseCase prepare,
        IDecomposeWaveletUseCase decompose,
        IDenoiseWaveletUseCase denoise,
        IReconstructWaveletLevelUseCase reconstructLevel,
        IRunWaveletQualityScanUseCase scan,
        IRunWatermarkCarrierBenchmarkUseCase benchmark,
        IExportWaveletImageUseCase exportImage,
        IExportWaveletReportUseCase exportReport,
        IImageFileDialog imageDialog,
        IWaveletReportFileDialog reportDialog,
        IImageCodec codec,
        IDocumentLifetime lifetime)
    {
        _prepare = prepare; _decompose = decompose; _denoise = denoise; _reconstructLevel = reconstructLevel; _scan = scan;
        _benchmark = benchmark; _exportImage = exportImage; _exportReport = exportReport;
        _imageDialog = imageDialog; _reportDialog = reportDialog; _codec = codec; _lifetime = lifetime;
    }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _referencePath = string.Empty;
    [ObservableProperty] private string _selectedTransform = nameof(WaveletTransformId.Haar);
    [ObservableProperty] private string _selectedChannel = nameof(ImageChannel.Luma);
    [ObservableProperty] private int _levels = 2;
    [ObservableProperty] private string _selectedThresholdMode = nameof(WaveletThresholdMode.Soft);
    [ObservableProperty] private string _selectedThresholdSource = nameof(WaveletThresholdSource.Manual);
    [ObservableProperty] private double _threshold = 12d;
    [ObservableProperty] private bool _targetLh = true;
    [ObservableProperty] private bool _targetHl = true;
    [ObservableProperty] private bool _targetHh = true;
    [ObservableProperty] private int _currentLevel = 1;
    [ObservableProperty] private string _selectedSubband = nameof(WaveletSubband.DiagonalDetail);
    [ObservableProperty] private string _selectedProjection = nameof(WaveletProjectionMode.Symmetric);
    [ObservableProperty] private int _analysisMaximumEdge = 1024;
    [ObservableProperty] private string _payloadText = "Wavelet Lab";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isScanBusy;
    [ObservableProperty] private bool _isBenchmarkBusy;
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private string _statusMessage = "请选择 PNG/JPEG；分析默认使用 Y 通道、Haar、2 层和最大边 1024 的代理。";
    [ObservableProperty] private string _analysisSummary = "尚未分解";
    [ObservableProperty] private string _denoiseSummary = "尚未生成去噪结果";
    [ObservableProperty] private string _scanSummary = "尚未运行有限扫描";
    [ObservableProperty] private string _benchmarkSummary = "尚未运行 DCT/DWT 公平比较";
    [ObservableProperty] private IReadOnlyList<double> _scanPlotValues = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<string> _scanCaseRows = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> _benchmarkCaseRows = Array.Empty<string>();
    [ObservableProperty] private Bitmap? _sourcePreview;
    [ObservableProperty] private Bitmap? _subbandPreview;
    [ObservableProperty] private Bitmap? _resultPreview;

    public IReadOnlyList<string> TransformOptions { get; } = Enum.GetNames<WaveletTransformId>();
    public IReadOnlyList<string> ChannelOptions { get; } = Enum.GetNames<ImageChannel>();
    public IReadOnlyList<string> ThresholdModeOptions { get; } = Enum.GetNames<WaveletThresholdMode>();
    public IReadOnlyList<string> ThresholdSourceOptions { get; } = Enum.GetNames<WaveletThresholdSource>();
    public IReadOnlyList<string> SubbandOptions { get; } = Enum.GetNames<WaveletSubband>();
    public IReadOnlyList<string> ProjectionOptions { get; } = Enum.GetNames<WaveletProjectionMode>();
    public IReadOnlyList<int> AnalysisEdgeOptions { get; } = [512, 1024, 2048];
    public string HelpSummary => WaveletLabHelpCatalog.Summary;
    public bool HasSession => _session is not null;
    public bool HasAnalysis => _analysis is not null;
    public bool CanExportImage => _fullResult is not null && TryBuildRecipe(out var recipe, out _) &&
        StringComparer.Ordinal.Equals(_fullResult.RecipeFingerprint, recipe.Fingerprint());
    public bool CanExportReport => _fullResult is not null || _scanResult is not null || _benchmarkResult is not null;
    public bool IsOperationBusy => IsBusy || IsScanBusy || IsBenchmarkBusy || IsExporting;
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public event EventHandler? PresentationChanged;
    public event EventHandler? IsDirtyChanged;

    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation); cancellationToken.ThrowIfCancellationRequested();
        _restoring = true;
        try
        {
            if (activation is RestoreDocumentActivation restore) Restore(restore.RestoredContent);
            _presentation = new(string.IsNullOrWhiteSpace(activation.Title) ? "小波实验室" : activation.Title);
            _revision = _acceptedRevision = 0; PresentationChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _restoring = false; }
        return ValueTask.CompletedTask;
    }

    [RelayCommand]
    private async Task SelectSourceAsync()
    {
        var path = await _imageDialog.PickImageAsync(_lifetime.ClosingToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path)) SourcePath = path;
    }

    [RelayCommand]
    private async Task SelectReferenceAsync()
    {
        var path = await _imageDialog.PickImageAsync(_lifetime.ClosingToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path)) ReferencePath = path;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || !File.Exists(SourcePath)) { StatusMessage = "请选择存在的 PNG 或 JPEG 图片。"; return; }
        if (!string.IsNullOrWhiteSpace(ReferencePath) && !File.Exists(ReferencePath)) { StatusMessage = "参考图路径不存在。"; return; }
        CancelAndDispose(ref _loadCancellation);
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _loadCancellation; var token = current.Token; var generation = ++_generation; IsBusy = true;
        try
        {
            StatusMessage = "正在解码源图并建立抗混叠分析代理…";
            var session = await _prepare.ExecuteAsync(SourcePath, string.IsNullOrWhiteSpace(ReferencePath) ? null : ReferencePath,
                AnalysisMaximumEdge, token).ConfigureAwait(true);
            if (!CanCommit(generation)) { session.Dispose(); return; }
            ReplaceSession(session);
            ReplaceSourceBitmap(await CreateBitmapAsync(session.AnalysisProxy, token));
            StatusMessage = $"已载入完整图 {session.SourceImage.Size.Width}×{session.SourceImage.Size.Height}；代理 {session.AnalysisProxy.Size.Width}×{session.AnalysisProxy.Size.Height}。";
            OnPropertyChanged(nameof(HasSession));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { if (!_lifetime.IsClosing) StatusMessage = "载入已取消。"; }
        catch (Exception exception) { if (generation == _generation) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _loadCancellation)) IsBusy = false; }
    }

    [RelayCommand] private Task AnalyzeProxyAsync() => AnalyzeAsync(fullSize: false);
    [RelayCommand] private Task AnalyzeFullAsync() => AnalyzeAsync(fullSize: true);

    [RelayCommand]
    private async Task ReconstructCurrentLevelAsync()
    {
        var analysis = _analysis;
        if (analysis is null) { StatusMessage = "请先完成一次代理或完整尺寸分解。"; return; }
        CancelAndDispose(ref _analysisCancellation);
        _analysisCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _analysisCancellation; var token = current.Token; var generation = _generation; IsBusy = true;
        try
        {
            var target = Math.Clamp(CurrentLevel, 1, analysis.Pyramid.Levels.Count);
            var result = await _reconstructLevel.ExecuteAsync(analysis, target, token).ConfigureAwait(true);
            var bitmap = await CreateBitmapAsync(result.Preview, token).ConfigureAwait(true);
            if (!CanCommit(generation) || !ReferenceEquals(analysis, _analysis)) { bitmap.Dispose(); return; }
            ReplaceResultBitmap(bitmap);
            DenoiseSummary = $"已从最深第 {analysis.Pyramid.Levels.Count} 层逐级逆变换到第 {target} 层；当前阶段尺寸 {result.Plane.Size.Width}×{result.Plane.Size.Height}。第 1 层表示完整逆变换并裁回源尺寸。";
            StatusMessage = "逐级重建阶段已投影；该教学预览不会替换完整尺寸导出结果。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { StatusMessage = "逐级重建已取消。"; }
        catch (Exception exception) { if (generation == _generation) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _analysisCancellation)) IsBusy = false; }
    }

    private async Task AnalyzeAsync(bool fullSize)
    {
        var session = _session;
        if (session is null) { StatusMessage = "请先载入图片。"; return; }
        if (!TryBuildRecipe(out var recipe, out var error)) { StatusMessage = error ?? "配方无效。"; return; }
        CancelAndDispose(ref _analysisCancellation);
        _analysisCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _analysisCancellation; var token = current.Token; var generation = _generation; IsBusy = true;
        try
        {
            StatusMessage = fullSize ? "正在显式执行完整尺寸分解与去噪…" : "正在分析代理上分解、投影与重建…";
            var analysis = await _decompose.ExecuteAsync(session, recipe, fullSize, CurrentLevel,
                Enum.Parse<WaveletSubband>(SelectedSubband), Enum.Parse<WaveletProjectionMode>(SelectedProjection), token).ConfigureAwait(true);
            var denoised = await _denoise.ExecuteAsync(session, analysis, recipe, token).ConfigureAwait(true);
            var subbandBitmap = await CreateBitmapAsync(analysis.Projection.Image, token);
            var resultBitmap = await CreateBitmapAsync(denoised.Reconstruction.Image, token);
            if (!CanCommit(generation) || !ReferenceEquals(session, _session) || analysis.RecipeFingerprint != recipe.Fingerprint())
            { subbandBitmap.Dispose(); resultBitmap.Dispose(); return; }
            _analysis = analysis;
            if (fullSize) _fullResult = denoised;
            ReplaceSubbandBitmap(subbandBitmap);
            ReplaceResultBitmap(resultBitmap);
            AnalysisSummary = $"{(fullSize ? "完整尺寸" : "代理")} {analysis.Pyramid.OriginalSize.Width}×{analysis.Pyramid.OriginalSize.Height} → 扩展 {analysis.Pyramid.PaddedSize.Width}×{analysis.Pyramid.PaddedSize.Height}；{analysis.Elapsed.TotalMilliseconds:N0} ms；sigma={analysis.Noise.Sigma:0.###}，建议 T={analysis.Noise.UniversalThreshold:0.###}。";
            DenoiseSummary = $"保留 {denoised.ThresholdStatistics.RetainedNonZero:N0}/{denoised.ThresholdStatistics.OriginalNonZero:N0} 非零细节系数；double max error={denoised.Reconstruction.MaximumAbsoluteError:E3}，RMS={denoised.Reconstruction.RootMeanSquareError:E3}；裁切 {denoised.Reconstruction.ClippedPixelCount:N0} 像素。";
            StatusMessage = fullSize ? "完整尺寸结果已生成；只有当前配方指纹对应的结果可导出。" : "代理分析完成；代理结果不会冒充完整尺寸导出。";
            OnPropertyChanged(nameof(HasAnalysis)); OnPropertyChanged(nameof(CanExportImage)); OnPropertyChanged(nameof(CanExportReport));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { StatusMessage = "分析已取消，未提交半成品。"; }
        catch (Exception exception) { if (generation == _generation) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _analysisCancellation)) IsBusy = false; }
    }

    [RelayCommand]
    private async Task RunScanAsync()
    {
        var session = _session;
        if (session is null) { StatusMessage = "请先载入图片。"; return; }
        if (!TryBuildRecipe(out var recipe, out var error)) { StatusMessage = error ?? "配方无效。"; return; }
        CancelAndDispose(ref _scanCancellation); _scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _scanCancellation; var token = current.Token; var generation = _generation; IsScanBusy = true;
        try
        {
            var thresholds = Enumerable.Range(0, 7).Select(index => Math.Max(0d, Threshold * (0.5d + index / 6d))).ToArray();
            var levels = Enumerable.Range(1, Levels).ToArray();
            var result = await _scan.ExecuteAsync(session, recipe, thresholds, levels, token).ConfigureAwait(true);
            if (!CanCommit(generation) || !ReferenceEquals(session, _session)) return;
            _scanResult = result;
            ScanPlotValues = result.Cases.Select(value => value.Statistics.RetainedRatio).ToArray();
            ScanCaseRows = result.Cases.Select(value =>
                $"#{value.Sequence + 1}  L={value.Levels}  T={value.Threshold:0.###}  保留={value.Statistics.RetainedRatio:P1}  RMS={value.ResidualRms:0.###}  PSNR={(value.PsnrLuma?.ToString("0.###") ?? "N/A")}  SSIM={(value.SsimLuma?.ToString("0.####") ?? "N/A")}").ToArray();
            ScanSummary = $"完成 {result.Cases.Count}/{thresholds.Length * levels.Length} 案例；状态 {(result.Canceled ? "Canceled" : "Completed")}。{result.MetricBoundary}";
            StatusMessage = result.Canceled ? "扫描已取消并保留已完成案例。" : "有限扫描完成。";
            OnPropertyChanged(nameof(CanExportReport));
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { if (generation == _generation) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _scanCancellation)) IsScanBusy = false; }
    }

    [RelayCommand]
    private async Task RunBenchmarkAsync()
    {
        var session = _session;
        if (session is null) { StatusMessage = "请先载入图片。"; return; }
        var payload = Encoding.UTF8.GetBytes(PayloadText ?? string.Empty);
        CancelAndDispose(ref _benchmarkCancellation); _benchmarkCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _benchmarkCancellation; var token = current.Token; var generation = _generation; IsBenchmarkBusy = true;
        try
        {
            StatusMessage = "正在完整尺寸载体上执行共同 Payload 与共同扰动集合…";
            var result = await _benchmark.ExecuteAsync(session.SourceImage, payload, token).ConfigureAwait(true);
            if (!CanCommit(generation) || !ReferenceEquals(session, _session)) return;
            _benchmarkResult = result;
            BenchmarkCaseRows = result.Cases.Select(value =>
                $"{value.CaseId}  {value.CarrierId}  完整性={(value.IntegrityValid ? "通过" : "失败")}  raw BER={(value.RawBitErrorRate?.ToString("P3") ?? "N/A")}  confidence={value.Confidence:0.###}").ToArray();
            BenchmarkSummary = string.Join("；", result.Capacities.Select(value => $"{value.CarrierId} 容量 {value.MaximumPayloadBytes:N0} B")) +
                $"；共同 Payload {result.PayloadLength} B；完整性通过 {result.Cases.Count(value => value.IntegrityValid)}/{result.Cases.Count}。";
            StatusMessage = "DCT/DWT 有限比较完成；结论仅适用于当前定义。";
            OnPropertyChanged(nameof(CanExportReport));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { StatusMessage = "水印比较已取消。"; }
        catch (Exception exception) { if (generation == _generation) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _benchmarkCancellation)) IsBenchmarkBusy = false; }
    }

    [RelayCommand]
    private async Task ExportImageAsync()
    {
        if (_fullResult is null || !TryBuildRecipe(out var recipe, out _)) { StatusMessage = "请先生成当前配方的完整尺寸结果。"; return; }
        var path = await _imageDialog.PickOutputImageAsync($"{Path.GetFileNameWithoutExtension(SourcePath)}.wavelet.png", _lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        await ExportCoreAsync(token => _exportImage.ExecuteAsync(_fullResult, recipe.Fingerprint(), path, token), "完整尺寸 PNG 已原子导出。");
    }

    [RelayCommand] private Task ExportJsonAsync() => ExportReportCoreAsync(csv: false);
    [RelayCommand] private Task ExportCsvAsync() => ExportReportCoreAsync(csv: true);

    private async Task ExportReportCoreAsync(bool csv)
    {
        if (!TryBuildRecipe(out var recipe, out _)) { StatusMessage = "当前配方无效。"; return; }
        var report = new WaveletExperimentReport("wavelet-experiment-v1", Path.GetFileName(SourcePath), recipe.Fingerprint(), SelectedTransform,
            SelectedChannel, Levels, Threshold, _scanResult?.Cases ?? [], _benchmarkResult,
            ["无参考图时不提供最佳去噪质量结论。", "水印比较不代表 DCT 或 DWT 的普遍优劣。"], DateTimeOffset.UtcNow);
        var name = $"{Path.GetFileNameWithoutExtension(SourcePath)}.wavelet.{(csv ? "csv" : "json")}";
        var path = csv
            ? await _reportDialog.PickWaveletCsvOutputAsync(name, _lifetime.ClosingToken).ConfigureAwait(true)
            : await _reportDialog.PickWaveletJsonOutputAsync(name, _lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        await ExportCoreAsync(token => _exportReport.ExecuteAsync(report, path, csv, token), "实验报告已原子导出。");
    }

    private async Task ExportCoreAsync(Func<CancellationToken, Task> action, string success)
    {
        CancelAndDispose(ref _exportCancellation); _exportCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var token = _exportCancellation.Token; IsExporting = true;
        try { await action(token).ConfigureAwait(true); StatusMessage = success; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { StatusMessage = "导出已取消。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { IsExporting = false; }
    }

    [RelayCommand] private void Cancel() { _loadCancellation?.Cancel(); _analysisCancellation?.Cancel(); _scanCancellation?.Cancel(); _benchmarkCancellation?.Cancel(); _exportCancellation?.Cancel(); }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var content = JsonSerializer.SerializeToElement(new Snapshot(SourcePath, ReferencePath, SelectedTransform, SelectedChannel,
            Levels, SelectedThresholdMode, SelectedThresholdSource, Threshold, TargetLh, TargetHl, TargetHh, CurrentLevel,
            SelectedSubband, SelectedProjection, AnalysisMaximumEdge));
        return ValueTask.FromResult(new DocumentSaveSnapshot(new(_revision), new(SnapshotSchema, content)));
    }

    public void AcceptChanges(DocumentRevision savedRevision)
    {
        var before = IsDirty; if (savedRevision.Value == _revision) _acceptedRevision = _revision;
        if (before != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_generation;
        CancelAndDispose(ref _loadCancellation); CancelAndDispose(ref _analysisCancellation); CancelAndDispose(ref _scanCancellation);
        CancelAndDispose(ref _benchmarkCancellation); CancelAndDispose(ref _exportCancellation); ReplaceSession(null);
        ReplaceSourceBitmap(null); ReplaceSubbandBitmap(null); ReplaceResultBitmap(null);
    }

    partial void OnSourcePathChanged(string value) => ParametersChanged(invalidateSession: true);
    partial void OnReferencePathChanged(string value) => ParametersChanged(invalidateSession: true);
    partial void OnSelectedTransformChanged(string value) => ParametersChanged();
    partial void OnSelectedChannelChanged(string value) => ParametersChanged();
    partial void OnLevelsChanged(int value) { CurrentLevel = Math.Clamp(CurrentLevel, 1, Math.Max(1, value)); ParametersChanged(); }
    partial void OnSelectedThresholdModeChanged(string value) => ParametersChanged();
    partial void OnSelectedThresholdSourceChanged(string value) => ParametersChanged();
    partial void OnThresholdChanged(double value) => ParametersChanged();
    partial void OnTargetLhChanged(bool value) => ParametersChanged();
    partial void OnTargetHlChanged(bool value) => ParametersChanged();
    partial void OnTargetHhChanged(bool value) => ParametersChanged();
    partial void OnAnalysisMaximumEdgeChanged(int value) => ParametersChanged(invalidateSession: true);

    private void ParametersChanged(bool invalidateSession = false)
    {
        if (_restoring) return;
        ++_generation; _analysisCancellation?.Cancel(); _scanCancellation?.Cancel(); _benchmarkCancellation?.Cancel();
        _analysis = null; _fullResult = null; _scanResult = null; _benchmarkResult = null;
        ScanPlotValues = Array.Empty<double>(); ScanCaseRows = Array.Empty<string>(); BenchmarkCaseRows = Array.Empty<string>();
        if (invalidateSession) ReplaceSession(null);
        MarkDirty(); OnPropertyChanged(nameof(HasAnalysis)); OnPropertyChanged(nameof(CanExportImage)); OnPropertyChanged(nameof(CanExportReport));
    }

    private bool TryBuildRecipe(out WaveletDenoiseRecipe recipe, out string? error)
    {
        recipe = null!; error = null;
        try
        {
            var subbands = new List<WaveletSubband>();
            if (TargetLh) subbands.Add(WaveletSubband.HorizontalDetail);
            if (TargetHl) subbands.Add(WaveletSubband.VerticalDetail);
            if (TargetHh) subbands.Add(WaveletSubband.DiagonalDetail);
            recipe = new(Enum.Parse<WaveletTransformId>(SelectedTransform), Enum.Parse<ImageChannel>(SelectedChannel), Levels,
                Enum.Parse<WaveletThresholdMode>(SelectedThresholdMode), Enum.Parse<WaveletThresholdSource>(SelectedThresholdSource),
                Threshold, Enumerable.Range(1, Levels), subbands);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException) { error = exception.Message; return false; }
    }

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema) { StatusMessage = $"不支持快照 schema {content.SchemaVersion}，已使用安全默认值。"; return; }
        try
        {
            var value = content.Payload.Deserialize<Snapshot>() ?? throw new JsonException("快照为空。");
            SourcePath = value.SourcePath ?? string.Empty; ReferencePath = value.ReferencePath ?? string.Empty;
            SelectedTransform = Enum.TryParse<WaveletTransformId>(value.Transform, out _) ? value.Transform : nameof(WaveletTransformId.Haar);
            SelectedChannel = Enum.TryParse<ImageChannel>(value.Channel, out _) ? value.Channel : nameof(ImageChannel.Luma);
            Levels = Math.Clamp(value.Levels, 1, WaveletLimits.MaximumLevels);
            SelectedThresholdMode = Enum.TryParse<WaveletThresholdMode>(value.ThresholdMode, out _) ? value.ThresholdMode : nameof(WaveletThresholdMode.Soft);
            SelectedThresholdSource = Enum.TryParse<WaveletThresholdSource>(value.ThresholdSource, out _) ? value.ThresholdSource : nameof(WaveletThresholdSource.Manual);
            Threshold = double.IsFinite(value.Threshold) && value.Threshold >= 0d ? value.Threshold : 12d;
            TargetLh = value.TargetLh; TargetHl = value.TargetHl; TargetHh = value.TargetHh;
            if (!TargetLh && !TargetHl && !TargetHh) TargetHh = true;
            CurrentLevel = Math.Clamp(value.CurrentLevel, 1, Levels);
            SelectedSubband = Enum.TryParse<WaveletSubband>(value.Subband, out _) ? value.Subband : nameof(WaveletSubband.DiagonalDetail);
            SelectedProjection = Enum.TryParse<WaveletProjectionMode>(value.Projection, out _) ? value.Projection : nameof(WaveletProjectionMode.Symmetric);
            AnalysisMaximumEdge = ImageAnalysisProxyProjector.SupportedMaximumEdges.Contains(value.AnalysisMaximumEdge) ? value.AnalysisMaximumEdge : 1024;
            StatusMessage = "已恢复路径与参数；为避免意外 IO，尚未自动读取图片或计算。";
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException) { StatusMessage = $"快照无效，已回退安全默认值：{exception.Message}"; }
    }

    private async Task<Bitmap> CreateBitmapAsync(PixelImage image, CancellationToken token)
    {
        var encoded = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token).ConfigureAwait(true);
        return new Bitmap(new MemoryStream(encoded, writable: false));
    }

    private void ReplaceSession(WaveletSession? value)
    {
        var previous = _session; _session = value; _analysis = null; _fullResult = null; _scanResult = null; _benchmarkResult = null;
        ScanPlotValues = Array.Empty<double>(); ScanCaseRows = Array.Empty<string>(); BenchmarkCaseRows = Array.Empty<string>();
        previous?.Dispose(); OnPropertyChanged(nameof(HasSession)); OnPropertyChanged(nameof(HasAnalysis));
        OnPropertyChanged(nameof(CanExportImage)); OnPropertyChanged(nameof(CanExportReport));
    }

    private void ReplaceSourceBitmap(Bitmap? value) { var previous = SourcePreview; SourcePreview = value; previous?.Dispose(); }
    private void ReplaceSubbandBitmap(Bitmap? value) { var previous = SubbandPreview; SubbandPreview = value; previous?.Dispose(); }
    private void ReplaceResultBitmap(Bitmap? value) { var previous = ResultPreview; ResultPreview = value; previous?.Dispose(); }

    private bool CanCommit(long generation) => !_disposed && !_lifetime.IsClosing && generation == _generation;
    private void MarkDirty() { var was = IsDirty; _revision++; if (was != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }
    private static void CancelAndDispose(ref CancellationTokenSource? source) { source?.Cancel(); source?.Dispose(); source = null; }

    private sealed record Snapshot(string SourcePath, string ReferencePath, string Transform, string Channel, int Levels,
        string ThresholdMode, string ThresholdSource, double Threshold, bool TargetLh, bool TargetHl, bool TargetHh,
        int CurrentLevel, string Subband, string Projection, int AnalysisMaximumEdge);
}
