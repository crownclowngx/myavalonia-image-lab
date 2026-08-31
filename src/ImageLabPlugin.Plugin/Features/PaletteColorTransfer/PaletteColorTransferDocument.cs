using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.ColorTransfer;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.ColorTransfer;
using ImageLabPlugin.Domain.Imaging;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.PaletteColorTransfer;

/// <summary>“调色板与颜色迁移”的多实例可持久化 Document。</summary>
/// <remarks>
/// 本类型只管理命令、可观察状态、generation、取消、Bitmap 和轻量快照；颜色公式、像素循环、聚类、
/// 报告与文件原子发布都委托给窄用例。一个 Scope 只拥有一个 Session；关闭时取消任务、释放 Bitmap 并
/// 阻止迟到结果提交，从而保证多实例之间不存在共享图片或取消状态。
/// </remarks>
internal sealed partial class PaletteColorTransferDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private readonly ColorTransferSession _session;
    private readonly IPrepareColorTransferSessionUseCase _prepare;
    private readonly IAnalyzeColorDistributionsUseCase _analyze;
    private readonly IFreezePaletteUseCase _freeze;
    private readonly IRunColorTransferUseCase _transfer;
    private readonly IRemapToPaletteUseCase _remap;
    private readonly IExportColorResultUseCase _exportImage;
    private readonly IExportColorReportUseCase _exportReport;
    private readonly IImageFileDialog _imageDialog;
    private readonly IColorTransferFileDialog _colorDialog;
    private readonly IImageCodec _codec;
    private readonly ColorPixelInspector _pixelInspector;
    private readonly PaletteSorter _paletteSorter;
    private readonly IDocumentLifetime _lifetime;
    private DocumentPresentationState _presentation = new("调色板与颜色迁移");
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _operationCancellation;
    private long _loadGeneration;
    private long _operationGeneration;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;
    private bool _disposed;

    public PaletteColorTransferDocument(ColorTransferSession session,
        IPrepareColorTransferSessionUseCase prepare, IAnalyzeColorDistributionsUseCase analyze,
        IFreezePaletteUseCase freeze, IRunColorTransferUseCase transfer, IRemapToPaletteUseCase remap,
        IExportColorResultUseCase exportImage, IExportColorReportUseCase exportReport,
        IImageFileDialog imageDialog, IColorTransferFileDialog colorDialog, IImageCodec codec,
        ColorPixelInspector pixelInspector, PaletteSorter paletteSorter,
        IDocumentLifetime lifetime)
    {
        _session = session; _prepare = prepare; _analyze = analyze; _freeze = freeze;
        _transfer = transfer; _remap = remap; _exportImage = exportImage; _exportReport = exportReport;
        _imageDialog = imageDialog; _colorDialog = colorDialog; _codec = codec; _pixelInspector = pixelInspector;
        _paletteSorter = paletteSorter; _lifetime = lifetime;
    }

    [ObservableProperty] private string _targetPath = string.Empty;
    [ObservableProperty] private string _referencePath = string.Empty;
    [ObservableProperty] private int _colorCount = 6;
    [ObservableProperty] private string _selectedPaletteSource = "目标图";
    [ObservableProperty] private string _selectedPaletteSort = "占比";
    [ObservableProperty] private string _selectedTransferMode = "完整 Lab";
    [ObservableProperty] private double _strength = 1d;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "请选择目标图；参考图仅在颜色统计迁移时必需。";
    [ObservableProperty] private string _targetSummary = "目标图尚未载入";
    [ObservableProperty] private string _referenceSummary = "参考图尚未载入";
    [ObservableProperty] private string _resultSummary = "尚无结果";
    [ObservableProperty] private string _protocolSummary = $"{SrgbColorSpace.ProtocolId}；Alpha={ColorTransferProtocols.Alpha}";
    [ObservableProperty] private IReadOnlyList<string> _paletteRows = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> _statisticsRows = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<PaletteEntry> _paletteEntries = Array.Empty<PaletteEntry>();
    [ObservableProperty] private IReadOnlyList<double> _redHistogram = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<double> _labAbDensity = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<double> _differenceHistogram = Array.Empty<double>();
    [ObservableProperty] private int _targetProbeX;
    [ObservableProperty] private int _targetProbeY;
    [ObservableProperty] private int _referenceProbeX;
    [ObservableProperty] private int _referenceProbeY;
    [ObservableProperty] private string _probeSummary = "尚未读取像素探针";
    [ObservableProperty] private Bitmap? _targetPreview;
    [ObservableProperty] private Bitmap? _referencePreview;
    [ObservableProperty] private Bitmap? _resultPreview;

    public IReadOnlyList<int> ColorCountOptions { get; } = Enumerable.Range(2, 11).ToArray();
    public IReadOnlyList<string> PaletteSourceOptions { get; } = ["目标图", "参考图"];
    public IReadOnlyList<string> PaletteSortOptions { get; } = ["占比", "L* 明度", "HSV 色相"];
    public IReadOnlyList<string> TransferModeOptions { get; } = ["完整 Lab", "保留目标 L*"];
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasTarget => _session.Target is not null;
    public bool HasReference => _session.Reference is not null;
    public bool HasFrozenPalette => _session.FrozenPalette is not null;
    public bool HasCurrentResult => _session.HasCurrentResult;
    public event EventHandler? PresentationChanged;
    public event EventHandler? IsDirtyChanged;

    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation); cancellationToken.ThrowIfCancellationRequested(); _restoring = true;
        try
        {
            if (activation is RestoreDocumentActivation restore) Restore(restore.RestoredContent);
            _presentation = new(string.IsNullOrWhiteSpace(activation.Title) ? "调色板与颜色迁移" : activation.Title);
            PresentationChanged?.Invoke(this, EventArgs.Empty); _revision = _acceptedRevision = 0;
        }
        finally { _restoring = false; }
        return ValueTask.CompletedTask;
    }

    [RelayCommand]
    private async Task SelectTargetAsync()
    { var path = await _imageDialog.PickImageAsync(_lifetime.ClosingToken).ConfigureAwait(true); if (!string.IsNullOrWhiteSpace(path)) TargetPath = path; }

    [RelayCommand]
    private async Task SelectReferenceAsync()
    { var path = await _imageDialog.PickImageAsync(_lifetime.ClosingToken).ConfigureAwait(true); if (!string.IsNullOrWhiteSpace(path)) ReferencePath = path; }

    [RelayCommand]
    private Task LoadTargetAsync() => LoadAsync(PaletteSource.Target);

    [RelayCommand]
    private Task LoadReferenceAsync() => LoadAsync(PaletteSource.Reference);

    private async Task LoadAsync(PaletteSource source)
    {
        var path = source == PaletteSource.Target ? TargetPath : ReferencePath;
        if (string.IsNullOrWhiteSpace(path)) { StatusMessage = source == PaletteSource.Target ? "请先选择目标图片。" : "请先选择参考图片。"; return; }
        CancelAndDispose(ref _loadCancellation); _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _loadCancellation; var token = current.Token; var generation = ++_loadGeneration; IsBusy = true;
        StatusMessage = source == PaletteSource.Target ? "正在载入目标图并建立 512 最大边预览…" : "正在载入参考图并建立 512 最大边预览…";
        try
        {
            var prepared = await _prepare.ExecuteAsync(path, 512, token).ConfigureAwait(true);
            var bitmap = await CreateBitmapAsync(prepared.Preview, token).ConfigureAwait(true);
            if (!CanCommitLoad(generation)) { bitmap.Dispose(); return; }
            if (source == PaletteSource.Target) { _session.SetTarget(prepared); ReplaceTargetPreview(bitmap); TargetSummary = Describe(prepared); }
            else { _session.SetReference(prepared); ReplaceReferencePreview(bitmap); ReferenceSummary = Describe(prepared); }
            ReplaceResultPreview(null); ResultSummary = "输入已改变；旧结果与导出资格已失效。";
            PaletteRows = []; PaletteEntries = []; StatisticsRows = []; RedHistogram = []; LabAbDensity = []; DifferenceHistogram = []; NotifyCapabilities();
            StatusMessage = "图片已载入；载入不会自动分析，请确认 K 后点击“分析颜色”。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { if (!_lifetime.IsClosing && generation == _loadGeneration) StatusMessage = "载入已取消。"; }
        catch (Exception exception) { if (generation == _loadGeneration) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _loadCancellation)) IsBusy = false; }
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (_session.Target is null) { StatusMessage = "请先载入目标图。"; return; }
        await RunOperationAsync("正在分析 Alpha 加权颜色分布并执行确定性聚类…", async token =>
        {
            var target = await _analyze.ExecuteAsync(_session.Target.FullImage, ColorCount, PaletteSource.Target, token).ConfigureAwait(true);
            ColorAnalysisResult? reference = null;
            if (_session.Reference is not null)
                reference = await _analyze.ExecuteAsync(_session.Reference.FullImage, ColorCount, PaletteSource.Reference, token).ConfigureAwait(true);
            return (target, reference);
        }, value =>
        {
            _session.SetAnalysis(value.target, PaletteSource.Target);
            if (value.reference is not null) _session.SetAnalysis(value.reference, PaletteSource.Reference);
            RefreshAnalysisRows(); StatusMessage = "分析完成；可选择来源并冻结调色板，或运行颜色统计迁移。";
            return Task.CompletedTask;
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private void FreezePalette()
    {
        try
        {
            var extraction = ResolvePaletteSource() == PaletteSource.Target ? _session.TargetAnalysis?.Palette : _session.ReferenceAnalysis?.Palette;
            if (extraction is null) { StatusMessage = "所选来源尚无与当前输入匹配的提取结果。"; return; }
            _session.SetFrozenPalette(_freeze.Execute(extraction)); RefreshPaletteRows(); NotifyCapabilities();
            StatusMessage = $"已冻结 {extraction.Entries.Count} 色调色板；fingerprint={extraction.Fingerprint}。";
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand]
    private async Task RunTransferAsync()
    {
        if (_session.Target is null || _session.Reference is null || _session.TargetAnalysis is null || _session.ReferenceAnalysis is null)
        { StatusMessage = "颜色统计迁移需要已载入并分析的目标图和参考图。"; return; }
        var target = _session.Target;
        var recipe = new ColorTransferRecipe(ResolveTransferMode(), Strength);
        await RunOperationAsync("正在从原目标执行 CIELAB 独立通道统计迁移…",
            token => _transfer.ExecuteAsync(target.FullImage, _session.TargetAnalysis.Distribution,
                _session.ReferenceAnalysis.Distribution, recipe, token),
            result => CommitResultAsync(result, target)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RemapPaletteAsync()
    {
        if (_session.Target is null || _session.FrozenPalette is null)
        { StatusMessage = "调色板重映射需要目标图和有效冻结调色板。"; return; }
        var target = _session.Target; var palette = _session.FrozenPalette;
        await RunOperationAsync("正在按 ΔE76 精确重映射，并用 ΔE00 汇总误差…",
            token => _remap.ExecuteAsync(target.FullImage, palette, token),
            result => CommitResultAsync(result, target)).ConfigureAwait(true);
    }

    private async Task CommitResultAsync(ColorOperationResult result, PreparedColorImage target)
    {
        var token = _operationCancellation?.Token ?? CancellationToken.None;
        var bitmap = await CreateBitmapAsync(result.Image, token).ConfigureAwait(true);
        if (_disposed || !ReferenceEquals(target, _session.Target)) { bitmap.Dispose(); return; }
        _session.CommitResult(result); ReplaceResultPreview(bitmap); NotifyCapabilities();
        ResultSummary = $"{result.Kind}；ΔE00 均值 {result.Difference.Mean:F3}，P95 {result.Difference.P95:F2}，" +
            $"最大 {result.Difference.Maximum:F2}；改变 {result.Difference.ChangedPixelCount} 像素；" +
            $"色度压缩 {result.Gamut.ChromaCompressedCount}，L* 裁切 {result.Gamut.LightnessClippedCount}；" +
            $"PSNR-RGB {FormatFinite(result.Quality.PsnrRgbDb)} dB，SSIM-Y {result.Quality.GlobalSsimLuma:F4}";
        StatisticsRows = BuildStatisticsRows(result);
        DifferenceHistogram = result.Difference.Histogram;
        StatusMessage = "完整尺寸结果已完成；指标表达与原目标的差异，不表示审美或质量提升。";
    }

    [RelayCommand]
    private async Task ExportPngAsync()
    {
        var result = _session.Result; var target = _session.Target;
        if (!_session.HasCurrentResult || result is null || target is null) { StatusMessage = "当前结果已过期或不存在，不能导出。"; return; }
        var path = await _colorDialog.PickColorResultPngAsync("palette-color-result.png", _lifetime.ClosingToken).ConfigureAwait(true); if (path is null) return;
        try { await _exportImage.ExecuteAsync(result.Image, path, target.Path, _lifetime.ClosingToken).ConfigureAwait(true); StatusMessage = "PNG 已通过真实回读与 Alpha 校验后原子导出。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand]
    private Task ExportJsonAsync() => ExportReportAsync(ColorReportFormat.Json);
    [RelayCommand]
    private Task ExportCsvAsync() => ExportReportAsync(ColorReportFormat.Csv);

    [RelayCommand]
    private void InspectPixel()
    {
        if (_session.Target is null) { StatusMessage = "像素探针需要先载入目标图。"; return; }
        try
        {
            var target = _pixelInspector.Inspect(_session.Target.FullImage,
                Math.Clamp(TargetProbeX, 0, _session.Target.FullImage.Size.Width - 1),
                Math.Clamp(TargetProbeY, 0, _session.Target.FullImage.Size.Height - 1), _session.FrozenPalette);
            var parts = new List<string> { DescribeProbe("目标", target) };
            if (_session.Reference is { } reference)
            {
                var fact = _pixelInspector.Inspect(reference.FullImage,
                    Math.Clamp(ReferenceProbeX, 0, reference.FullImage.Size.Width - 1),
                    Math.Clamp(ReferenceProbeY, 0, reference.FullImage.Size.Height - 1), _session.FrozenPalette);
                parts.Add(DescribeProbe("参考", fact));
            }
            if (_session.Result is { } result)
            {
                var fact = _pixelInspector.Inspect(result.Image,
                    Math.Clamp(TargetProbeX, 0, result.Image.Size.Width - 1),
                    Math.Clamp(TargetProbeY, 0, result.Image.Size.Height - 1), _session.FrozenPalette);
                parts.Add(DescribeProbe("结果", fact));
            }
            ProbeSummary = string.Join("\n", parts);
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    private async Task ExportReportAsync(ColorReportFormat format)
    {
        if (!TryCreateReport(out var report)) { StatusMessage = "当前没有可导出的有效结果报告。"; return; }
        var path = format == ColorReportFormat.Json
            ? await _colorDialog.PickColorReportJsonAsync("palette-color-report.json", _lifetime.ClosingToken).ConfigureAwait(true)
            : await _colorDialog.PickColorReportCsvAsync("palette-color-report.csv", _lifetime.ClosingToken).ConfigureAwait(true);
        if (path is null) return;
        try { await _exportReport.ExecuteAsync(report!, format, path, _lifetime.ClosingToken).ConfigureAwait(true); StatusMessage = $"{format} 报告已原子导出；未包含图片字节或绝对输入路径。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.SerializeToElement(new Snapshot(TargetPath, ReferencePath, ColorCount,
            SelectedPaletteSource, SelectedPaletteSort, SelectedTransferMode, Strength, SrgbColorSpace.ProtocolId));
        return ValueTask.FromResult(new DocumentSaveSnapshot(new DocumentRevision(_revision), new DocumentContent(SnapshotSchema, payload)));
    }

    public void AcceptChanges(DocumentRevision savedRevision)
    { var dirty = IsDirty; if (savedRevision.Value == _revision) _acceptedRevision = _revision; if (dirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_loadGeneration; ++_operationGeneration;
        CancelAndDispose(ref _loadCancellation); CancelAndDispose(ref _operationCancellation);
        ReplaceTargetPreview(null); ReplaceReferencePreview(null); ReplaceResultPreview(null); _session.Dispose();
    }

    partial void OnTargetPathChanged(string value) { if (!_restoring) MarkChanged(); }
    partial void OnReferencePathChanged(string value) { if (!_restoring) MarkChanged(); }
    partial void OnColorCountChanged(int value)
    { if (!ColorCountOptions.Contains(value)) { ColorCount = 6; return; } if (!_restoring) { _session.ChangeAnalysisRecipe(); PaletteRows = []; PaletteEntries = []; StatisticsRows = []; RedHistogram = []; LabAbDensity = []; NotifyCapabilities(); MarkChanged(); } }
    partial void OnSelectedPaletteSourceChanged(string value) { if (!_restoring) { RefreshPaletteRows(); MarkChanged(); } }
    partial void OnSelectedPaletteSortChanged(string value) { if (!_restoring) RefreshPaletteRows(); }
    partial void OnSelectedTransferModeChanged(string value) { if (!_restoring) InvalidateRecipe(); }
    partial void OnStrengthChanged(double value)
    { if (!double.IsFinite(value) || value is < 0d or > 1d) { Strength = Math.Clamp(double.IsFinite(value) ? value : 1d, 0d, 1d); return; } if (!_restoring) InvalidateRecipe(); }

    private void InvalidateRecipe() { _session.ChangeRecipe(); ReplaceResultPreview(null); ResultSummary = "配方已改变；当前结果过期。"; NotifyCapabilities(); MarkChanged(); }

    private async Task RunOperationAsync<T>(string status, Func<CancellationToken, Task<T>> operation, Func<T, Task> commit)
    {
        CancelAndDispose(ref _operationCancellation); _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _operationCancellation; var token = current.Token; var generation = ++_operationGeneration; IsBusy = true; StatusMessage = status;
        try { var value = await operation(token).ConfigureAwait(true); if (generation == _operationGeneration && !_disposed && !_lifetime.IsClosing) await commit(value).ConfigureAwait(true); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { if (!_lifetime.IsClosing && generation == _operationGeneration) StatusMessage = "操作已取消；未提交半结果。"; }
        catch (Exception exception) { if (generation == _operationGeneration) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _operationCancellation)) IsBusy = false; }
    }

    private void RefreshAnalysisRows()
    {
        var rows = new List<string>();
        if (_session.TargetAnalysis is { } target) rows.AddRange(DescribeStatistics("目标", target.Distribution.Statistics));
        if (_session.ReferenceAnalysis is { } reference) rows.AddRange(DescribeStatistics("参考", reference.Distribution.Statistics));
        StatisticsRows = rows;
        if (_session.TargetAnalysis is { } targetAnalysis)
        { RedHistogram = targetAnalysis.Distribution.RgbHistogram.Take(256).ToArray(); LabAbDensity = targetAnalysis.Distribution.LabAbDensity; }
        RefreshPaletteRows();
    }

    private void RefreshPaletteRows()
    {
        var palette = _session.FrozenPalette?.Entries ??
            (ResolvePaletteSource() == PaletteSource.Target ? _session.TargetAnalysis?.Palette.Entries : _session.ReferenceAnalysis?.Palette.Entries);
        if (palette is null) { PaletteRows = []; PaletteEntries = []; return; }
        var sorted = _paletteSorter.Sort(palette, ResolvePaletteSort());
        PaletteEntries = sorted;
        PaletteRows = sorted.Select(entry =>
        { var bytes = entry.Srgb.ToBytes(); return $"#{entry.ClusterIndex}  #{bytes.Red:X2}{bytes.Green:X2}{bytes.Blue:X2}  " +
            $"Lab({entry.Lab.L:F1},{entry.Lab.A:F1},{entry.Lab.B:F1})  占比 {entry.Proportion:P1}  簇内均值 ΔE76 {entry.MeanDeltaE76:F2}"; }).ToArray();
    }

    private IReadOnlyList<string> BuildStatisticsRows(ColorOperationResult result)
    {
        var rows = new List<string>();
        if (_session.TargetAnalysis is { } target) rows.AddRange(DescribeStatistics("目标", target.Distribution.Statistics));
        if (_session.ReferenceAnalysis is { } reference) rows.AddRange(DescribeStatistics("参考", reference.Distribution.Statistics));
        rows.AddRange(DescribeStatistics("结果", result.Distribution.Statistics));
        rows.Add($"ΔE00：均值 {result.Difference.Mean:F3}；P50 {result.Difference.P50:F2}；P95 {result.Difference.P95:F2}；最大 {result.Difference.Maximum:F2}");
        rows.Add($"色域映射：色度压缩 {result.Gamut.ChromaCompressedCount}；L* 裁切 {result.Gamut.LightnessClippedCount}；最大映射 ΔE76 {result.Gamut.MaximumDeltaE76:F3}");
        if (result.BeforeReferenceCloseness is { } before && result.AfterReferenceCloseness is { } after)
            rows.Add($"相对参考：均值残差 {before.MeanResidual:F3}→{after.MeanResidual:F3}；标准差残差 {before.StandardDeviationResidual:F3}→{after.StandardDeviationResidual:F3}；JSD(L/a/b) {after.JensenShannonL:F3}/{after.JensenShannonA:F3}/{after.JensenShannonB:F3}");
        return rows;
    }

    private bool TryCreateReport(out ColorExperimentReport? report)
    {
        report = null; if (!_session.HasCurrentResult || _session.Result is null || _session.TargetAnalysis is null || _session.Target is null) return false;
        report = new ColorExperimentReport(_session.Result.Kind, _session.Result.RecipeFingerprint, _session.Target.FullImage.Size,
            _session.Reference?.FullImage.Size, _session.TargetAnalysis.Distribution.Statistics,
            _session.ReferenceAnalysis?.Distribution.Statistics, _session.Result.Distribution.Statistics,
            _session.FrozenPalette, _session.Result.Difference, _session.Result.Gamut, _session.Result.Quality,
            _session.Result.BeforeReferenceCloseness, _session.Result.AfterReferenceCloseness); return true;
    }

    private static IEnumerable<string> DescribeStatistics(string label, ColorStatistics value)
    {
        yield return $"{label}：可见 {value.VisiblePixelCount}/{value.PixelCount}；有效 Alpha 权重 {value.EffectiveWeight:F3}";
        yield return $"{label} Lab 均值 ({value.MeanLab.L:F2}, {value.MeanLab.A:F2}, {value.MeanLab.B:F2})；标准差 ({value.StandardDeviationLab.L:F2}, {value.StandardDeviationLab.A:F2}, {value.StandardDeviationLab.B:F2})";
        yield return $"{label} Hue：{(value.CircularMeanHueDegrees is { } hue ? $"圆均值 {hue:F1}°，集中度 {value.HueConcentration:F3}" : "N/A")}；无色相权重 {value.UndefinedHueWeight:F3}";
    }

    private async Task<Bitmap> CreateBitmapAsync(PixelImage image, CancellationToken token)
    { var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token).ConfigureAwait(false); using var stream = new MemoryStream(bytes, false); return new Bitmap(stream); }
    private static string Describe(PreparedColorImage value) => $"完整尺寸 {value.FullImage.Size.Width}×{value.FullImage.Size.Height}；预览 {value.Preview.Size.Width}×{value.Preview.Size.Height}；fingerprint {value.ContentFingerprint}";
    private static string DescribeProbe(string label, ColorPixelFact value) =>
        $"{label} ({value.X},{value.Y}) RGBA=({value.Red},{value.Green},{value.Blue},{value.Alpha})；" +
        $"HSV=({(value.Hsv.HueStatus == HueStatus.Defined ? $"{value.Hsv.HueDegrees:F1}°" : "N/A")},{value.Hsv.Saturation:F3},{value.Hsv.Value:F3})；" +
        $"Lab=({value.Lab.L:F2},{value.Lab.A:F2},{value.Lab.B:F2})；" +
        $"palette={(value.PaletteClusterIndex is { } index ? $"#{index}，ΔE76={value.DeltaE76:F2}" : "N/A")}";
    private bool CanCommitLoad(long generation) => generation == _loadGeneration && !_disposed && !_lifetime.IsClosing;
    private PaletteSource ResolvePaletteSource() => SelectedPaletteSource == "参考图" ? PaletteSource.Reference : PaletteSource.Target;
    private PaletteSort ResolvePaletteSort() => SelectedPaletteSort switch { "L* 明度" => PaletteSort.Lightness, "HSV 色相" => PaletteSort.Hue, _ => PaletteSort.Proportion };
    private ColorTransferMode ResolveTransferMode() => SelectedTransferMode == "保留目标 L*" ? ColorTransferMode.PreserveTargetLightness : ColorTransferMode.FullLab;
    private static string FormatFinite(double value) => double.IsPositiveInfinity(value) ? "∞" : value.ToString("F2");
    private void ReplaceTargetPreview(Bitmap? value) { var old = TargetPreview; TargetPreview = value; old?.Dispose(); }
    private void ReplaceReferencePreview(Bitmap? value) { var old = ReferencePreview; ReferencePreview = value; old?.Dispose(); }
    private void ReplaceResultPreview(Bitmap? value) { var old = ResultPreview; ResultPreview = value; old?.Dispose(); }
    private static void CancelAndDispose(ref CancellationTokenSource? value) { value?.Cancel(); value?.Dispose(); value = null; }
    private void NotifyCapabilities() { OnPropertyChanged(nameof(HasTarget)); OnPropertyChanged(nameof(HasReference)); OnPropertyChanged(nameof(HasFrozenPalette)); OnPropertyChanged(nameof(HasCurrentResult)); }
    private void MarkChanged() { if (_restoring) return; var dirty = IsDirty; _revision++; if (dirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema) { StatusMessage = $"不支持 schema {content.SchemaVersion}，已使用安全默认值。"; return; }
        var value = content.Payload.Deserialize<Snapshot>(); if (value is null) return;
        TargetPath = value.TargetPath ?? string.Empty; ReferencePath = value.ReferencePath ?? string.Empty;
        ColorCount = ColorCountOptions.Contains(value.ColorCount) ? value.ColorCount : 6;
        SelectedPaletteSource = PaletteSourceOptions.Contains(value.PaletteSource) ? value.PaletteSource : "目标图";
        SelectedPaletteSort = PaletteSortOptions.Contains(value.PaletteSort) ? value.PaletteSort : "占比";
        SelectedTransferMode = TransferModeOptions.Contains(value.TransferMode) ? value.TransferMode : "完整 Lab";
        Strength = double.IsFinite(value.Strength) && value.Strength is >= 0d and <= 1d ? value.Strength : 1d;
        StatusMessage = "已恢复路径文本和轻量参数；不会自动读取图片、恢复调色板或运行算法。";
    }

    private sealed record Snapshot(string? TargetPath, string? ReferencePath, int ColorCount,
        string PaletteSource, string PaletteSort, string TransferMode, double Strength, string ColorProtocol);
}
