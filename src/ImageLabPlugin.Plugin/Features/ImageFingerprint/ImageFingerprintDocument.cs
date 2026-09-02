using System.Globalization;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.Fingerprinting;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Fingerprinting;
using ImageLabPlugin.Domain.Shared.Imaging;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.ImageFingerprint;

/// <summary>“感知指纹”Document：拥有两条路径、一个比较 Session、一次稳定性结果和所有展示资源。</summary>
/// <remarks>
/// Document 只负责命令、快照、取消、generation、Revision 和 Bitmap 生命周期；归一化、DCT、汉明距离、
/// 扰动和 JSON 均由窄用例完成。任一路径变化都会立即取消任务并释放旧 Session，使旧结果不能复制或导出。
/// </remarks>
internal sealed partial class ImageFingerprintDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private readonly IPrepareFingerprintComparisonUseCase _prepareUseCase;
    private readonly IRunFingerprintStabilityUseCase _stabilityUseCase;
    private readonly IExportFingerprintReportUseCase _exportUseCase;
    private readonly IImageFileDialog _imageDialog;
    private readonly IFingerprintReportFileDialog _reportDialog;
    private readonly ITextClipboard _clipboard;
    private readonly IImageCodec _codec;
    private readonly IDocumentLifetime _lifetime;
    private DocumentPresentationState _presentation = new("感知指纹");
    private FingerprintComparisonSession? _session;
    private FingerprintReport? _report;
    private CancellationTokenSource? _comparisonCancellation;
    private CancellationTokenSource? _stabilityCancellation;
    private long _generation;
    private long _stabilityGeneration;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;
    private bool _disposed;
    private string? _restoredSelectedAlgorithmId;

    public ImageFingerprintDocument(
        IPrepareFingerprintComparisonUseCase prepareUseCase,
        IRunFingerprintStabilityUseCase stabilityUseCase,
        IExportFingerprintReportUseCase exportUseCase,
        IImageFileDialog imageDialog,
        IFingerprintReportFileDialog reportDialog,
        ITextClipboard clipboard,
        IImageCodec codec,
        IDocumentLifetime lifetime)
    {
        _prepareUseCase = prepareUseCase;
        _stabilityUseCase = stabilityUseCase;
        _exportUseCase = exportUseCase;
        _imageDialog = imageDialog;
        _reportDialog = reportDialog;
        _clipboard = clipboard;
        _codec = codec;
        _lifetime = lifetime;
    }

    [ObservableProperty] private string _referencePath = string.Empty;
    [ObservableProperty] private string _candidatePath = string.Empty;
    [ObservableProperty] private bool _showFingerprintBitmaps = true;
    [ObservableProperty] private bool _showPreviews = true;
    [ObservableProperty] private bool _showLimitations = true;
    [ObservableProperty] private string _selectedStabilityKind = "缩放";
    [ObservableProperty] private string _stabilityValuesText = "1, 0.75, 0.5, 0.25";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isStabilityBusy;
    [ObservableProperty] private string _statusMessage = "请选择参考图和候选图。";
    [ObservableProperty] private string _overview = "尚未计算";
    [ObservableProperty] private string _imageFacts = "算法输入始终来自完整解码图片；预览不参与指纹计算。";
    [ObservableProperty] private string _stabilitySummary = "可对参考图运行缩放、JPEG、亮度或轻度中心裁剪单轴试验。";
    [ObservableProperty] private IReadOnlyList<FingerprintAlgorithmRow> _algorithmRows = Array.Empty<FingerprintAlgorithmRow>();
    [ObservableProperty] private FingerprintAlgorithmRow? _selectedAlgorithm;
    [ObservableProperty] private IReadOnlyList<FingerprintStabilityPoint> _stabilityPoints = Array.Empty<FingerprintStabilityPoint>();
    [ObservableProperty] private Bitmap? _referencePreview;
    [ObservableProperty] private Bitmap? _candidatePreview;
    [ObservableProperty] private Bitmap? _stabilityPreview;
    [ObservableProperty] private ulong _referenceBits;
    [ObservableProperty] private ulong _candidateBits;
    [ObservableProperty] private ulong _xorBits;
    [ObservableProperty] private string _selectedAlgorithmDetails = "选择算法行后显示位图、版本和限制。";

    public IReadOnlyList<string> StabilityKinds { get; } = ["缩放", "JPEG", "亮度", "中心裁剪"];
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasResult => _report is not null;
    public bool HasSession => _session is not null;
    public bool HasStability => _report?.Stability is not null;
    public bool IsOperationBusy => IsBusy || IsStabilityBusy;

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
            _presentation = new DocumentPresentationState(string.IsNullOrWhiteSpace(activation.Title) ? "感知指纹" : activation.Title);
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
        var first = ReferencePath;
        ReferencePath = CandidatePath;
        CandidatePath = first;
        if (!string.IsNullOrWhiteSpace(ReferencePath) && !string.IsNullOrWhiteSpace(CandidatePath)) await ComputeAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ComputeAsync()
    {
        if (string.IsNullOrWhiteSpace(ReferencePath) || string.IsNullOrWhiteSpace(CandidatePath))
        { StatusMessage = "请先选择参考图和候选图。"; return; }
        CancelAndDispose(ref _comparisonCancellation);
        CancelAndDispose(ref _stabilityCancellation);
        _comparisonCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _comparisonCancellation;
        var token = current.Token;
        var generation = ++_generation;
        var referencePath = ReferencePath;
        var candidatePath = CandidatePath;
        IsBusy = true;
        StatusMessage = "正在顺序解码并计算 aHash、dHash 和 pHash…";
        FingerprintComparisonSession? incoming = null;
        Bitmap? referenceBitmap = null;
        Bitmap? candidateBitmap = null;
        try
        {
            incoming = await _prepareUseCase.ExecuteAsync(new(referencePath, candidatePath), token).ConfigureAwait(true);
            referenceBitmap = await CreateBitmapAsync(incoming.ReferenceProxy, token).ConfigureAwait(true);
            candidateBitmap = await CreateBitmapAsync(incoming.CandidateProxy, token).ConfigureAwait(true);
            if (!CanCommit(generation, referencePath, candidatePath)) return;
            ReplaceSession(incoming); incoming = null;
            ReplaceReferenceBitmap(referenceBitmap); referenceBitmap = null;
            ReplaceCandidateBitmap(candidateBitmap); candidateBitmap = null;
            ReplaceStabilityBitmap(null);
            var summary = _session!.Summary;
            AlgorithmRows = summary.Algorithms.Select(ToRow).ToArray();
            SelectedAlgorithm = AlgorithmRows.FirstOrDefault(value => value.AlgorithmId == _restoredSelectedAlgorithmId) ?? AlgorithmRows.FirstOrDefault();
            _restoredSelectedAlgorithmId = null;
            Overview = ImageFingerprintHelpCatalog.OverviewText(summary.Overview);
            ImageFacts = $"参考图 {summary.Reference.Size.Width}×{summary.Reference.Size.Height}{(summary.Reference.HasAlpha ? "，含 Alpha" : "，不透明")}；候选图 {summary.Candidate.Size.Width}×{summary.Candidate.Size.Height}{(summary.Candidate.HasAlpha ? "，含 Alpha" : "，不透明")}；归一化 {summary.NormalizationId}";
            _report = new FingerprintReport(1, summary);
            StabilityPoints = [];
            StabilitySummary = "双图结果已保留；可从参考图运行单轴稳定性试验。";
            StatusMessage = "指纹比较完成。距离 0 只表示该算法摘要相同，位相似度不是来源概率。";
            NotifyResultState();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { if (!_lifetime.IsClosing && generation == _generation) StatusMessage = "指纹计算已取消。"; }
        catch (Exception exception) { if (generation == _generation) StatusMessage = $"指纹计算失败：{exception.Message}"; }
        finally
        {
            incoming?.Dispose(); referenceBitmap?.Dispose(); candidateBitmap?.Dispose();
            if (ReferenceEquals(_comparisonCancellation, current)) IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunStabilityAsync()
    {
        var session = _session;
        if (session is null) { StatusMessage = "请先完成双图指纹计算。"; return; }
        FingerprintStabilityRecipe recipe;
        try { recipe = new(ToKind(SelectedStabilityKind), ParseValues(StabilityValuesText)); }
        catch (Exception exception) { StatusMessage = $"稳定性参数无效：{exception.Message}"; return; }
        CancelAndDispose(ref _stabilityCancellation);
        _stabilityCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _stabilityCancellation;
        var token = current.Token;
        var generation = ++_stabilityGeneration;
        IsStabilityBusy = true;
        StatusMessage = "正在串行运行稳定性试验…";
        try
        {
            var result = await _stabilityUseCase.ExecuteAsync(session, recipe, null, token).ConfigureAwait(true);
            if (generation != _stabilityGeneration || !ReferenceEquals(session, _session) || _disposed) return;
            StabilityPoints = result.Points;
            StabilitySummary = $"{(result.IsComplete ? "完成" : "未完成")} {result.Points.Count}/{recipe.Values.Count} 点。{result.Notice}";
            if (result.CurrentSamplePreview is not null) ReplaceStabilityBitmap(await CreateBitmapAsync(result.CurrentSamplePreview, token).ConfigureAwait(true));
            _report = _report is null ? null : _report with { Stability = result };
            StatusMessage = result.IsComplete ? "稳定性试验完成。" : "稳定性试验已取消，已完成点仍可查看。";
            NotifyResultState();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { StatusMessage = "稳定性试验已取消。"; }
        catch (Exception exception) { StatusMessage = $"稳定性试验失败：{exception.Message}"; }
        finally { if (ReferenceEquals(_stabilityCancellation, current)) IsStabilityBusy = false; }
    }

    [RelayCommand] private void Cancel() { _comparisonCancellation?.Cancel(); _stabilityCancellation?.Cancel(); }

    [RelayCommand]
    private async Task CopySummaryAsync()
    {
        var report = _report; if (report is null) { StatusMessage = "当前没有可复制的指纹摘要。"; return; }
        var copied = await _clipboard.TrySetTextAsync(_exportUseCase.CreateHumanReadableText(report), _lifetime.ClosingToken).ConfigureAwait(true);
        StatusMessage = copied ? "指纹摘要已复制。" : "剪贴板不可用；当前结果仍已保留。";
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        var report = _report; if (report is null) { StatusMessage = "当前没有可导出的指纹报告。"; return; }
        var path = await _reportDialog.PickFingerprintJsonOutputAsync($"fingerprint-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json", _lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        try { await _exportUseCase.ExecuteAsync(report, path, _lifetime.ClosingToken).ConfigureAwait(false); StatusMessage = $"已原子导出指纹报告：{path}"; }
        catch (Exception exception) { StatusMessage = $"导出失败：{exception.Message}"; }
    }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.SerializeToElement(new Snapshot(ReferencePath, CandidatePath, SelectedAlgorithm?.AlgorithmId, ShowFingerprintBitmaps, ShowPreviews, ShowLimitations, SelectedStabilityKind, StabilityValuesText));
        return ValueTask.FromResult(new DocumentSaveSnapshot(new DocumentRevision(_revision), new DocumentContent(SnapshotSchema, payload)));
    }

    public void AcceptChanges(DocumentRevision savedRevision)
    {
        var wasDirty = IsDirty;
        if (savedRevision.Value == _revision) _acceptedRevision = _revision;
        if (wasDirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; ++_generation; ++_stabilityGeneration;
        CancelAndDispose(ref _comparisonCancellation); CancelAndDispose(ref _stabilityCancellation);
        ReplaceSession(null); ReplaceReferenceBitmap(null); ReplaceCandidateBitmap(null); ReplaceStabilityBitmap(null);
        _report = null;
    }

    partial void OnReferencePathChanged(string value) { if (!_restoring) { Invalidate("参考图已改变，请重新计算。"); MarkChanged(); } }
    partial void OnCandidatePathChanged(string value) { if (!_restoring) { Invalidate("候选图已改变，请重新计算。"); MarkChanged(); } }
    partial void OnShowFingerprintBitmapsChanged(bool value) { if (!_restoring) MarkChanged(); }
    partial void OnShowPreviewsChanged(bool value) { if (!_restoring) MarkChanged(); }
    partial void OnShowLimitationsChanged(bool value) { if (!_restoring) MarkChanged(); }
    partial void OnStabilityValuesTextChanged(string value) { if (!_restoring) MarkChanged(); }
    partial void OnSelectedStabilityKindChanged(string value)
    {
        if (_restoring) return;
        StabilityValuesText = value switch { "JPEG" => "100, 90, 80, 70, 60, 50, 40", "亮度" => "-20, -10, 0, 10, 20", "中心裁剪" => "0, 2, 5, 8, 10", _ => "1, 0.75, 0.5, 0.25" };
        MarkChanged();
    }
    partial void OnSelectedAlgorithmChanged(FingerprintAlgorithmRow? value)
    {
        if (value is null) return;
        ReferenceBits = value.ReferenceBits; CandidateBits = value.CandidateBits; XorBits = value.XorBits;
        SelectedAlgorithmDetails = $"{value.Name}｜{value.AlgorithmId}｜阈值 ≤ {value.Threshold}｜{value.Limitation} 位相似度不是概率。";
    }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsOperationBusy));
    partial void OnIsStabilityBusyChanged(bool value) => OnPropertyChanged(nameof(IsOperationBusy));

    private async Task<Bitmap> CreateBitmapAsync(PixelImage image, CancellationToken token)
    {
        var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, writable: false);
        return new Bitmap(stream);
    }

    private bool CanCommit(long generation, string referencePath, string candidatePath) =>
        generation == _generation && ReferencePath == referencePath && CandidatePath == candidatePath && !_lifetime.IsClosing && !_disposed;

    private void Invalidate(string message)
    {
        ++_generation; ++_stabilityGeneration;
        CancelAndDispose(ref _comparisonCancellation); CancelAndDispose(ref _stabilityCancellation);
        ReplaceSession(null); ReplaceReferenceBitmap(null); ReplaceCandidateBitmap(null); ReplaceStabilityBitmap(null);
        _report = null; AlgorithmRows = []; SelectedAlgorithm = null; StabilityPoints = [];
        Overview = "结果已过期"; StatusMessage = message; NotifyResultState();
    }

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema) { StatusMessage = $"不支持 schema {content.SchemaVersion}，已使用安全默认值。"; return; }
        var value = content.Payload.Deserialize<Snapshot>(); if (value is null) return;
        ReferencePath = value.ReferencePath ?? string.Empty;
        CandidatePath = value.CandidatePath ?? string.Empty;
        _restoredSelectedAlgorithmId = value.SelectedAlgorithmId;
        ShowFingerprintBitmaps = value.ShowFingerprintBitmaps;
        ShowPreviews = value.ShowPreviews;
        ShowLimitations = value.ShowLimitations;
        SelectedStabilityKind = StabilityKinds.Contains(value.SelectedStabilityKind) ? value.SelectedStabilityKind : "缩放";
        StabilityValuesText = string.IsNullOrWhiteSpace(value.StabilityValuesText) || value.StabilityValuesText.Length > 256 ? "1, 0.75, 0.5, 0.25" : value.StabilityValuesText;
        StatusMessage = "已恢复路径和轻量参数；请显式点击“计算指纹”，恢复过程不会读取图片。";
    }

    private static FingerprintAlgorithmRow ToRow(FingerprintAlgorithmResult value) => new(
        ImageFingerprintHelpCatalog.DisplayName(value.AlgorithmId), value.AlgorithmId.Value,
        value.Reference.ToCanonicalHex(), value.Candidate.ToCanonicalHex(), value.Distance.Distance,
        $"{value.Distance.BitSimilarityPercent:F2}%", ImageFingerprintHelpCatalog.DecisionText(value.Decision),
        value.ReferenceThreshold, value.Limitation, value.Reference.Bits, value.Candidate.Bits);

    private static FingerprintStabilityKind ToKind(string value) => value switch
    { "JPEG" => FingerprintStabilityKind.Jpeg, "亮度" => FingerprintStabilityKind.Brightness, "中心裁剪" => FingerprintStabilityKind.CenterCrop, _ => FingerprintStabilityKind.Scale };

    private static IReadOnlyList<decimal> ParseValues(string text) => text.Split([',', '，', ';', '；', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture)).ToArray();

    private void ReplaceSession(FingerprintComparisonSession? replacement) { var previous = _session; _session = replacement; previous?.Dispose(); }
    private void ReplaceReferenceBitmap(Bitmap? replacement) { var previous = ReferencePreview; ReferencePreview = replacement; previous?.Dispose(); }
    private void ReplaceCandidateBitmap(Bitmap? replacement) { var previous = CandidatePreview; CandidatePreview = replacement; previous?.Dispose(); }
    private void ReplaceStabilityBitmap(Bitmap? replacement) { var previous = StabilityPreview; StabilityPreview = replacement; previous?.Dispose(); }
    private void NotifyResultState() { OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(HasSession)); OnPropertyChanged(nameof(HasStability)); }
    private void MarkChanged() { if (_restoring) return; var wasDirty = IsDirty; _revision++; if (!wasDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }
    private static void CancelAndDispose(ref CancellationTokenSource? source) { source?.Cancel(); source?.Dispose(); source = null; }

    private sealed record Snapshot(string? ReferencePath, string? CandidatePath, string? SelectedAlgorithmId, bool ShowFingerprintBitmaps, bool ShowPreviews, bool ShowLimitations, string SelectedStabilityKind, string StabilityValuesText);
}
