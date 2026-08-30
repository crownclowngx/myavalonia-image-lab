using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.BitPlanes;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.BitPlanes;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.BitPlaneViewer;

/// <summary>“位平面观察器”Document：管理路径、选择、异步代次、快照与四张 Bitmap。</summary>
/// <remarks>
/// 逐像素工作全部委托给四个窄用例。源图 generation 与投影 generation 分开：换图会取消整条链，
/// 换通道只重做 BytePlane/统计，换掩码只重做有界投影。所有返回结果提交前都检查代次、会话引用和
/// ClosingToken，底层即使忽略取消也不能让旧图片闪回。
/// </remarks>
internal sealed partial class BitPlaneViewerDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private readonly IPrepareBitPlaneSessionUseCase _prepareUseCase;
    private readonly IAnalyzeBitPlaneChannelUseCase _analyzeUseCase;
    private readonly IProjectBitPlaneViewUseCase _projectUseCase;
    private readonly IExportBitPlaneImageUseCase _exportUseCase;
    private readonly IImageFileDialog _dialog;
    private readonly IImageCodec _codec;
    private readonly IDocumentLifetime _lifetime;
    private DocumentPresentationState _presentation = new("位平面观察器");
    private BitPlaneSession? _session;
    private BitPlaneChannelAnalysis? _analysis;
    private BitPlanePreviewMap? _previewMap;
    private CancellationTokenSource? _sourceCancellation;
    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _projectionCancellation;
    private CancellationTokenSource? _exportCancellation;
    private long _sourceGeneration;
    private long _projectionGeneration;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;
    private bool _disposed;

    public BitPlaneViewerDocument(
        IPrepareBitPlaneSessionUseCase prepareUseCase,
        IAnalyzeBitPlaneChannelUseCase analyzeUseCase,
        IProjectBitPlaneViewUseCase projectUseCase,
        IExportBitPlaneImageUseCase exportUseCase,
        IImageFileDialog dialog,
        IImageCodec codec,
        IDocumentLifetime lifetime)
    {
        _prepareUseCase = prepareUseCase;
        _analyzeUseCase = analyzeUseCase;
        _projectUseCase = projectUseCase;
        _exportUseCase = exportUseCase;
        _dialog = dialog;
        _codec = codec;
        _lifetime = lifetime;
        RebuildRows();
    }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _selectedChannel = "Y";
    [ObservableProperty] private int _focusedBit = 7;
    [ObservableProperty] private int _maskValue = 0x80;
    [ObservableProperty] private int _highMinimumBit = 4;
    [ObservableProperty] private int _lowMaximumBit = 3;
    [ObservableProperty] private bool _showCheckerboard = true;
    [ObservableProperty] private bool _showExplanation = true;
    [ObservableProperty] private int _selectedSourceX;
    [ObservableProperty] private int _selectedSourceY;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private string _statusMessage = "请选择 PNG/JPEG 图片；初始观察 Y 的 bit 7。";
    [ObservableProperty] private string _imageSummary = "尚未分析";
    [ObservableProperty] private string _maskSummary = "0x80 / 0b10000000";
    [ObservableProperty] private string _probeSummary = "点击任一预览可查看原始 RGBA、通道字节、二进制与保留值。";
    [ObservableProperty] private IReadOnlyList<BitPlaneBitRow> _bitRows = Array.Empty<BitPlaneBitRow>();
    [ObservableProperty] private Bitmap? _sourcePreview;
    [ObservableProperty] private Bitmap? _focusedPreview;
    [ObservableProperty] private Bitmap? _combinedPreview;
    [ObservableProperty] private Bitmap? _reconstructionPreview;

    public IReadOnlyList<string> ChannelOptions { get; } = ["R", "G", "B", "Alpha", "Y"];
    public IReadOnlyList<int> BitOptions { get; } = [7, 6, 5, 4, 3, 2, 1, 0];
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasSession => _session is not null && _analysis is not null;
    public bool IsOperationBusy => IsBusy || IsExporting;
    public bool IsAlphaChannel => SelectedChannel == "Alpha";
    public bool ShowReconstructionCheckerboard => ShowCheckerboard && IsAlphaChannel;

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
            _presentation = new DocumentPresentationState(string.IsNullOrWhiteSpace(activation.Title) ? "位平面观察器" : activation.Title);
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
        InvalidateRuntime("正在读取原始 RGBA8888 像素…");
        _sourceCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _sourceCancellation;
        var token = current.Token;
        var generation = ++_sourceGeneration;
        IsBusy = true;
        try
        {
            var session = await _prepareUseCase.ExecuteAsync(SourcePath, token).ConfigureAwait(true);
            if (!CanCommitSource(generation)) { session.Dispose(); return; }
            ReplaceSession(session);
            await AnalyzeCurrentChannelAsync(session, generation, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { if (!_lifetime.IsClosing && generation == _sourceGeneration) StatusMessage = "分析已取消。"; }
        catch (Exception exception) { if (generation == _sourceGeneration) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _sourceCancellation)) IsBusy = false; }
    }

    private async Task AnalyzeCurrentChannelAsync(BitPlaneSession session, long sourceGeneration, CancellationToken token)
    {
        StatusMessage = $"正在抽取 {SelectedChannel} 并一次统计八个位平面…";
        var analysis = await _analyzeUseCase.ExecuteAsync(session, ResolveChannel(), token).ConfigureAwait(true);
        if (!CanCommitSource(sourceGeneration) || !ReferenceEquals(session, _session)) return;
        _analysis = analysis;
        RebuildRows();
        ImageSummary = $"原图 {session.SourceImage.Size.Width}×{session.SourceImage.Size.Height}；{session.SourceImage.Size.PixelCount:N0} 像素；通道 {SelectedChannel}";
        OnPropertyChanged(nameof(HasSession));
        await ProjectAsync(debounce: false).ConfigureAwait(true);
    }

    private async Task ProjectAsync(bool debounce)
    {
        var session = _session;
        var analysis = _analysis;
        if (session is null || analysis is null) return;
        CancelAndDispose(ref _projectionCancellation);
        _projectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _projectionCancellation;
        var token = current.Token;
        var generation = ++_projectionGeneration;
        try
        {
            if (debounce) await Task.Delay(100, token).ConfigureAwait(true);
            var projection = await _projectUseCase.ExecuteAsync(session, analysis, new BitMask8((byte)MaskValue), FocusedBit, token).ConfigureAwait(true);
            var bitmaps = await CreateBitmapsAsync(projection, token).ConfigureAwait(true);
            if (generation != _projectionGeneration || !ReferenceEquals(session, _session) || _lifetime.IsClosing)
            { DisposeBitmaps(bitmaps); return; }
            _previewMap = projection.Coordinates;
            ReplaceSourceBitmap(bitmaps.Source);
            ReplaceFocusedBitmap(bitmaps.Focused);
            ReplaceCombinedBitmap(bitmaps.Combined);
            ReplaceReconstructionBitmap(bitmaps.Reconstruction);
            RefreshProbe();
            StatusMessage = analysis.Channel == BitPlaneChannel.Luma
                ? $"投影完成；预览中 Y 逆变换裁切 {projection.ClippedPixelCount:N0} 个采样像素。"
                : "投影完成；单位平面为不透明黑白，组合图保持真实 0–255 量级。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { if (generation == _projectionGeneration) StatusMessage = exception.Message; }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var session = _session;
        var analysis = _analysis;
        if (session is null || analysis is null) { StatusMessage = "请先完成分析再导出。"; return; }
        var suggested = $"{Path.GetFileNameWithoutExtension(SourcePath)}.bit-plane-{SelectedChannel}-{MaskValue:X2}.png";
        var path = await _dialog.PickOutputImageAsync(suggested, _lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        CancelAndDispose(ref _exportCancellation);
        _exportCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var token = _exportCancellation.Token;
        IsExporting = true;
        StatusMessage = $"正在创建 {session.SourceImage.Size.Width}×{session.SourceImage.Size.Height} 完整尺寸重建并原子导出 PNG…";
        try
        {
            var result = await _exportUseCase.ExecuteAsync(session, analysis, new BitMask8((byte)MaskValue), path, token).ConfigureAwait(true);
            StatusMessage = $"已导出 PNG：{result.OutputPath}；尺寸 {result.Size.Width}×{result.Size.Height}；Y 裁切 {result.ClippedPixelCount:N0} 像素。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { StatusMessage = "导出已取消；未报告成功。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { IsExporting = false; }
    }

    [RelayCommand] private void Cancel() { _sourceCancellation?.Cancel(); _analysisCancellation?.Cancel(); _projectionCancellation?.Cancel(); _exportCancellation?.Cancel(); }
    [RelayCommand] private void SelectAll() => MaskValue = 0xFF;
    [RelayCommand] private void ClearAll() => MaskValue = 0x00;
    [RelayCommand] private void KeepHigh() => MaskValue = BitMask8.KeepHigh(HighMinimumBit).Value;
    [RelayCommand] private void KeepLow() => MaskValue = BitMask8.KeepLow(LowMaximumBit).Value;
    [RelayCommand] private void FocusBit(int bitIndex) => FocusedBit = bitIndex;

    internal void InspectAtNormalized(double x, double y)
    {
        if (_previewMap is null) return;
        (SelectedSourceX, SelectedSourceY) = _previewMap.FromNormalized(x, y);
        RefreshProbe();
        MarkChanged();
    }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.SerializeToElement(new Snapshot(SourcePath, SelectedChannel, FocusedBit, MaskValue,
            HighMinimumBit, LowMaximumBit, ShowCheckerboard, ShowExplanation, SelectedSourceX, SelectedSourceY));
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
        _disposed = true;
        ++_sourceGeneration; ++_projectionGeneration;
        CancelAndDispose(ref _sourceCancellation); CancelAndDispose(ref _analysisCancellation); CancelAndDispose(ref _projectionCancellation); CancelAndDispose(ref _exportCancellation);
        ReplaceSession(null);
        ReplaceSourceBitmap(null); ReplaceFocusedBitmap(null);
        ReplaceCombinedBitmap(null); ReplaceReconstructionBitmap(null);
    }

    partial void OnSourcePathChanged(string value) { if (!_restoring) { InvalidateRuntime("图片已改变，请显式点击“分析”。"); MarkChanged(); } }
    partial void OnSelectedChannelChanged(string value)
    {
        OnPropertyChanged(nameof(IsAlphaChannel)); OnPropertyChanged(nameof(ShowReconstructionCheckerboard));
        if (_restoring) return; MarkChanged();
        var session = _session;
        _analysis = null; RebuildRows(); OnPropertyChanged(nameof(HasSession));
        if (session is not null) _ = ReanalyzeChannelAsync(session);
    }
    partial void OnFocusedBitChanged(int value) { if (!_restoring) { _ = BitMask8.Single(value); MarkChanged(); _ = ProjectAsync(true); } }
    partial void OnMaskValueChanged(int value)
    {
        if ((uint)value > byte.MaxValue) { MaskValue = Math.Clamp(value, 0, 255); return; }
        MaskSummary = $"0x{value:X2} / {new BitMask8((byte)value).ToBinaryString()}";
        SynchronizeRows();
        if (!_restoring) { MarkChanged(); _ = ProjectAsync(true); }
    }
    partial void OnHighMinimumBitChanged(int value) { if ((uint)value > 7u) HighMinimumBit = Math.Clamp(value, 0, 7); else if (!_restoring) MarkChanged(); }
    partial void OnLowMaximumBitChanged(int value) { if ((uint)value > 7u) LowMaximumBit = Math.Clamp(value, 0, 7); else if (!_restoring) MarkChanged(); }
    partial void OnShowCheckerboardChanged(bool value) { OnPropertyChanged(nameof(ShowReconstructionCheckerboard)); if (!_restoring) MarkChanged(); }
    partial void OnShowExplanationChanged(bool value) { if (!_restoring) MarkChanged(); }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsOperationBusy));
    partial void OnIsExportingChanged(bool value) => OnPropertyChanged(nameof(IsOperationBusy));

    private async Task ReanalyzeChannelAsync(BitPlaneSession session)
    {
        CancelAndDispose(ref _analysisCancellation);
        CancelAndDispose(ref _projectionCancellation);
        _analysisCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _analysisCancellation;
        var token = current.Token;
        var generation = ++_projectionGeneration;
        IsBusy = true;
        try
        {
            var analysis = await _analyzeUseCase.ExecuteAsync(session, ResolveChannel(), token).ConfigureAwait(true);
            if (generation != _projectionGeneration || !ReferenceEquals(session, _session)) return;
            _analysis = analysis; RebuildRows();
            await ProjectAsync(false).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _analysisCancellation)) IsBusy = false; }
    }

    private async Task<(Bitmap Source, Bitmap Focused, Bitmap Combined, Bitmap Reconstruction)> CreateBitmapsAsync(BitPlaneProjection value, CancellationToken token)
    {
        var source = await CreateBitmapAsync(value.Source, token).ConfigureAwait(true);
        try
        {
            var focused = await CreateBitmapAsync(value.FocusedPlane, token).ConfigureAwait(true);
            try
            {
                var combined = await CreateBitmapAsync(value.CombinedPlane, token).ConfigureAwait(true);
                try { return (source, focused, combined, await CreateBitmapAsync(value.Reconstruction, token).ConfigureAwait(true)); }
                catch { combined.Dispose(); throw; }
            }
            catch { focused.Dispose(); throw; }
        }
        catch { source.Dispose(); throw; }
    }

    private async Task<Bitmap> CreateBitmapAsync(ImageLabPlugin.Domain.Imaging.PixelImage image, CancellationToken token)
    {
        var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, writable: false);
        return new Bitmap(stream);
    }

    private void ToggleBit(int bitIndex, bool selected)
    {
        var bit = 1 << bitIndex;
        MaskValue = selected ? MaskValue | bit : MaskValue & ~bit;
    }

    private void RebuildRows()
    {
        var stats = _analysis?.Statistics.ToDictionary(x => x.BitIndex);
        BitRows = Enumerable.Range(0, 8).Reverse().Select(bit => new BitPlaneBitRow(
            bit, (MaskValue & (1 << bit)) != 0, stats?.GetValueOrDefault(bit), ToggleBit, bitIndex => FocusedBit = bitIndex)).ToArray();
    }

    private void SynchronizeRows()
    {
        foreach (var row in BitRows) row.Synchronize((MaskValue & (1 << row.BitIndex)) != 0);
    }

    private void RefreshProbe()
    {
        if (_session is null || _analysis is null) return;
        SelectedSourceX = Math.Clamp(SelectedSourceX, 0, _session.SourceImage.Size.Width - 1);
        SelectedSourceY = Math.Clamp(SelectedSourceY, 0, _session.SourceImage.Size.Height - 1);
        var report = _projectUseCase.Inspect(_session, _analysis, new BitMask8((byte)MaskValue), SelectedSourceX, SelectedSourceY);
        ProbeSummary = $"({report.SourceX},{report.SourceY})  RGBA=({report.Red},{report.Green},{report.Blue},{report.Alpha})  " +
            $"{SelectedChannel}={report.ChannelValue} / {report.BinaryValue}  mask=0x{report.Mask:X2}  kept={report.KeptValue}";
    }

    private BitPlaneChannel ResolveChannel() => SelectedChannel switch
    {
        "R" => BitPlaneChannel.Red, "G" => BitPlaneChannel.Green, "B" => BitPlaneChannel.Blue,
        "Alpha" => BitPlaneChannel.Alpha, _ => BitPlaneChannel.Luma
    };

    private bool CanCommitSource(long generation) => generation == _sourceGeneration && !_lifetime.IsClosing && !_disposed;

    private void InvalidateRuntime(string status)
    {
        ++_sourceGeneration; ++_projectionGeneration;
        CancelAndDispose(ref _sourceCancellation); CancelAndDispose(ref _analysisCancellation); CancelAndDispose(ref _projectionCancellation); CancelAndDispose(ref _exportCancellation);
        ReplaceSession(null); _analysis = null; _previewMap = null; RebuildRows();
        ReplaceSourceBitmap(null); ReplaceFocusedBitmap(null);
        ReplaceCombinedBitmap(null); ReplaceReconstructionBitmap(null);
        ImageSummary = "尚未分析"; ProbeSummary = "点击任一预览可查看像素事实。"; StatusMessage = status; OnPropertyChanged(nameof(HasSession));
    }

    private void ReplaceSession(BitPlaneSession? replacement)
    {
        var previous = _session; _session = replacement; previous?.Dispose();
    }

    private void ReplaceSourceBitmap(Bitmap? replacement) { var previous = SourcePreview; SourcePreview = replacement; previous?.Dispose(); }
    private void ReplaceFocusedBitmap(Bitmap? replacement) { var previous = FocusedPreview; FocusedPreview = replacement; previous?.Dispose(); }
    private void ReplaceCombinedBitmap(Bitmap? replacement) { var previous = CombinedPreview; CombinedPreview = replacement; previous?.Dispose(); }
    private void ReplaceReconstructionBitmap(Bitmap? replacement) { var previous = ReconstructionPreview; ReconstructionPreview = replacement; previous?.Dispose(); }

    private static void DisposeBitmaps((Bitmap Source, Bitmap Focused, Bitmap Combined, Bitmap Reconstruction) value)
    { value.Source.Dispose(); value.Focused.Dispose(); value.Combined.Dispose(); value.Reconstruction.Dispose(); }

    private void MarkChanged()
    {
        if (_restoring) return;
        var wasDirty = IsDirty; _revision++;
        if (wasDirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema)
        { StatusMessage = $"不支持 schema {content.SchemaVersion}，已使用 Y / bit 7 / 0x80 安全默认值。"; return; }
        var value = content.Payload.Deserialize<Snapshot>();
        if (value is null) return;
        SourcePath = value.SourcePath ?? string.Empty;
        SelectedChannel = ChannelOptions.Contains(value.Channel) ? value.Channel : "Y";
        FocusedBit = (uint)value.FocusedBit <= 7u ? value.FocusedBit : 7;
        MaskValue = (uint)value.Mask <= byte.MaxValue ? value.Mask : 0x80;
        HighMinimumBit = (uint)value.HighMinimumBit <= 7u ? value.HighMinimumBit : 4;
        LowMaximumBit = (uint)value.LowMaximumBit <= 7u ? value.LowMaximumBit : 3;
        ShowCheckerboard = value.ShowCheckerboard; ShowExplanation = value.ShowExplanation;
        SelectedSourceX = Math.Max(0, value.SourceX); SelectedSourceY = Math.Max(0, value.SourceY);
        RebuildRows();
        StatusMessage = File.Exists(SourcePath) ? "已恢复路径和轻量参数；请显式点击“分析”。" : "已恢复参数，但源图片不存在，请重新选择。";
    }

    private static void CancelAndDispose(ref CancellationTokenSource? source)
    { source?.Cancel(); source?.Dispose(); source = null; }

    private sealed record Snapshot(string? SourcePath, string Channel, int FocusedBit, int Mask,
        int HighMinimumBit, int LowMaximumBit, bool ShowCheckerboard, bool ShowExplanation, int SourceX, int SourceY);
}
