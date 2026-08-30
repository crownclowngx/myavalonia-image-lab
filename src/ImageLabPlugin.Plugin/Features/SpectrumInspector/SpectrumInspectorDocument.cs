using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.SpectrumAnalysis;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.Imaging;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.SpectrumInspector;

/// <summary>“频域分析器”Document：拥有单张图片的一份可持久化分析配方和短生命周期分析会话。</summary>
/// <remarks>
/// Document 只编排应用用例，不直接执行 FFT、DCT、编解码或文件发布。每次分析/重建都有独立 generation；
/// 即使底层实现忽略取消，迟到结果也不能覆盖新图片、新通道或新频带。快照只保存路径和轻量参数，恢复时不
/// 自动读取文件，避免 Host 恢复布局时突然分配大型复数缓冲。
/// </remarks>
internal sealed partial class SpectrumInspectorDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private readonly IAnalyzeSpectrumUseCase _analyzeUseCase;
    private readonly IInspectDctBlockUseCase _inspectBlockUseCase;
    private readonly IReconstructSpectrumBandUseCase _reconstructUseCase;
    private readonly IProjectSpectrumUseCase _projectSpectrumUseCase;
    private readonly IImageFileDialog _fileDialog;
    private readonly IImageCodec _codec;
    private readonly IAtomicFileWriter _fileWriter;
    private readonly IDocumentLifetime _lifetime;
    private DocumentPresentationState _presentation = new("频域分析器");
    private SpectrumAnalysisSession? _session;
    private PixelImage? _reconstructedImage;
    private PixelImage? _magnitudeImage;
    private PixelImage? _phaseImage;
    private PixelImage? _dctImage;
    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _reconstructionCancellation;
    private CancellationTokenSource? _energyCancellation;
    private long _generation;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;

    public SpectrumInspectorDocument(
        IAnalyzeSpectrumUseCase analyzeUseCase,
        IInspectDctBlockUseCase inspectBlockUseCase,
        IReconstructSpectrumBandUseCase reconstructUseCase,
        IProjectSpectrumUseCase projectSpectrumUseCase,
        IImageFileDialog fileDialog,
        IImageCodec codec,
        IAtomicFileWriter fileWriter,
        IDocumentLifetime lifetime)
    {
        _analyzeUseCase = analyzeUseCase;
        _inspectBlockUseCase = inspectBlockUseCase;
        _reconstructUseCase = reconstructUseCase;
        _projectSpectrumUseCase = projectSpectrumUseCase;
        _fileDialog = fileDialog;
        _codec = codec;
        _fileWriter = fileWriter;
        _lifetime = lifetime;
    }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _selectedChannel = "Y";
    [ObservableProperty] private int _selectedMaximumEdge = 1024;
    [ObservableProperty] private string _selectedSpectrumView = "对数幅度";
    [ObservableProperty] private string _selectedBand = "全部";
    [ObservableProperty] private double _lowBoundary = 0.15d;
    [ObservableProperty] private double _highBoundary = 0.50d;
    [ObservableProperty] private double _customInner = 0d;
    [ObservableProperty] private double _customOuter = 1d;
    [ObservableProperty] private int _selectedSourceX;
    [ObservableProperty] private int _selectedSourceY;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "请选择 PNG/JPEG 图片。";
    [ObservableProperty] private string _sizeSummary = "尚未分析";
    [ObservableProperty] private string _energySummary = "尚无径向能量数据。";
    [ObservableProperty] private IReadOnlyList<double> _radialBins = Array.Empty<double>();
    [ObservableProperty] private string _frequencyPointSummary = "将指针移到频谱上可查看频点。";
    [ObservableProperty] private string _blockSummary = "点击原图可检查完整 8×8 DCT 块。";
    [ObservableProperty] private Bitmap? _sourcePreview;
    [ObservableProperty] private Bitmap? _spectrumPreview;
    [ObservableProperty] private Bitmap? _maskPreview;
    [ObservableProperty] private Bitmap? _reconstructionPreview;

    public IReadOnlyList<string> ChannelOptions { get; } = ["R", "G", "B", "Y", "Cb", "Cr"];
    public IReadOnlyList<int> MaximumEdgeOptions { get; } = [512, 1024, 2048];
    public IReadOnlyList<string> SpectrumViewOptions { get; } = ["线性幅度", "对数幅度", "百分位幅度", "相位", "分块 DCT"];
    public IReadOnlyList<string> BandOptions { get; } = ["全部", "低频", "中频", "高频", "自定义"];
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasSession => _session is not null;
    public bool HasReconstruction => _reconstructedImage is not null;
    public bool IsCustomBand => SelectedBand == "自定义";

    public event EventHandler? PresentationChanged;
    public event EventHandler? IsDirtyChanged;

    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation); cancellationToken.ThrowIfCancellationRequested();
        _restoring = true;
        try
        {
            if (activation is RestoreDocumentActivation restore) Restore(restore.RestoredContent);
            _presentation = new DocumentPresentationState(string.IsNullOrWhiteSpace(activation.Title) ? "频域分析器" : activation.Title);
            PresentationChanged?.Invoke(this, EventArgs.Empty);
            _revision = _acceptedRevision = 0;
        }
        finally { _restoring = false; }
        return ValueTask.CompletedTask;
    }

    [RelayCommand]
    private async Task SelectSourceAsync()
    {
        var path = await _fileDialog.PickImageAsync(_lifetime.ClosingToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path)) SourcePath = path;
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        try { ValidateSource(); }
        catch (Exception exception) { StatusMessage = exception.Message; return; }
        CancelAndDispose(ref _analysisCancellation);
        CancelAndDispose(ref _reconstructionCancellation);
        _analysisCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _analysisCancellation;
        var operationToken = current.Token;
        var generation = ++_generation;
        IsBusy = true; StatusMessage = "正在生成分析代理并执行 FFT…";
        try
        {
            var result = await _analyzeUseCase.ExecuteAsync(
                new SpectrumAnalysisRequest(SourcePath, ResolveChannel(), SelectedMaximumEdge), operationToken).ConfigureAwait(true);
            operationToken.ThrowIfCancellationRequested();
            if (generation != _generation || _lifetime.IsClosing) { result.Session.Dispose(); return; }
            ReplaceSession(result);
            await ReplaceBitmapAsync(result.Session.ProxyImage, PreviewKind.Source, operationToken, generation).ConfigureAwait(true);
            await RefreshSpectrumPreviewAsync(operationToken, generation).ConfigureAwait(true);
            if (generation != _generation || !ReferenceEquals(result.Session, _session)) return;
            SizeSummary = $"原图 {result.Session.SourceImage.Size.Width}×{result.Session.SourceImage.Size.Height}；" +
                $"分析代理 {result.Session.ProxyImage.Size.Width}×{result.Session.ProxyImage.Size.Height}；" +
                $"FFT {result.Session.Spectrum.PaddedWidth}×{result.Session.Spectrum.PaddedHeight}";
            var radial = result.Session.RadialEnergy;
            RadialBins = radial.Bins;
            EnergySummary = $"DC {radial.DcShare:P2}；低频 {radial.LowShare:P2}；中频 {radial.MediumShare:P2}；高频 {radial.HighShare:P2}";
            OnPropertyChanged(nameof(HasSession));
            StatusMessage = "分析完成；正在生成全部频带的精确重建。";
            await ReconstructCoreAsync(debounce: false).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested) { if (!_lifetime.IsClosing && generation == _generation) StatusMessage = "分析已取消。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(_analysisCancellation, current)) IsBusy = false; }
    }

    [RelayCommand]
    private async Task ReconstructAsync() => await ReconstructCoreAsync(debounce: false).ConfigureAwait(true);

    private async Task ReconstructCoreAsync(bool debounce)
    {
        var session = _session;
        if (session is null) return;
        CancelAndDispose(ref _reconstructionCancellation);
        _reconstructionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _reconstructionCancellation;
        var operationToken = current.Token;
        var generation = ++_generation;
        try
        {
            if (debounce) await Task.Delay(150, operationToken).ConfigureAwait(false);
            var result = await _reconstructUseCase.ExecuteAsync(session, ResolveBand(), operationToken).ConfigureAwait(true);
            if (generation != _generation || !ReferenceEquals(session, _session) || _lifetime.IsClosing) return;
            _reconstructedImage = result.Image;
            await ReplaceBitmapAsync(result.MaskPreview, PreviewKind.Mask, operationToken, generation).ConfigureAwait(true);
            await ReplaceBitmapAsync(result.Image, PreviewKind.Reconstruction, operationToken, generation).ConfigureAwait(true);
            OnPropertyChanged(nameof(HasReconstruction));
            StatusMessage = result.UsedExactAllPassShortcut ? "全部频带：已逐字节复制分析代理。" :
                $"重建完成；虚部残差 {result.MaximumImaginaryResidual:E2}；裁切像素 {result.ClippedPixelCount:N0}。";
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested) { }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand]
    private void InspectBlock()
    {
        if (_session is null) return;
        try
        {
            var report = _inspectBlockUseCase.Execute(_session, new ImagePoint(SelectedSourceX, SelectedSourceY));
            BlockSummary = report.IsAvailable
                ? $"块原点 ({report.Origin.X},{report.Origin.Y})；通道 {SelectedChannel}；IDCT 最大误差 {report.MaximumError:E3}\n\n" +
                  $"通道像素\n{FormatMatrix(report.Pixels)}\n\nDCT 系数\n{FormatMatrix(report.Coefficients)}\n\nIDCT 重建\n{FormatMatrix(report.Reconstructed)}"
                : report.UnavailableReason!;
        }
        catch (Exception exception) { BlockSummary = exception.Message; }
    }

    internal void InspectSourceAt(double normalizedX, double normalizedY)
    {
        if (_session is null) return;
        SelectedSourceX = Math.Clamp((int)(normalizedX * _session.SourceImage.Size.Width), 0, _session.SourceImage.Size.Width - 1);
        SelectedSourceY = Math.Clamp((int)(normalizedY * _session.SourceImage.Size.Height), 0, _session.SourceImage.Size.Height - 1);
        InspectBlock(); MarkChanged();
    }

    internal void InspectFrequencyAt(double normalizedX, double normalizedY)
    {
        var session = _session; if (session is null) return;
        var x = Math.Clamp((int)(normalizedX * session.Spectrum.PaddedWidth), 0, session.Spectrum.PaddedWidth - 1);
        var y = Math.Clamp((int)(normalizedY * session.Spectrum.PaddedHeight), 0, session.Spectrum.PaddedHeight - 1);
        var info = _projectSpectrumUseCase.Inspect(session, x, y, new FrequencyBandBoundaries(LowBoundary, HighBoundary));
        FrequencyPointSummary = $"显示 ({x},{y}) → FFT ({info.Coordinates.InternalX},{info.Coordinates.InternalY})；" +
            $"bin ({info.Coordinates.Kx},{info.Coordinates.Ky})；f=({info.Coordinates.Fx:F4},{info.Coordinates.Fy:F4}) cycles/pixel；" +
            $"ρ={info.Coordinates.Radius:F4}；幅值={info.Magnitude:E3}；相位={(info.PhaseRadians is null ? "未定义" : $"{info.PhaseRadians:F4} rad")}；" +
            $"能量={info.NormalizedEnergy:P4}；区域={RegionName(info.Region)}";
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_reconstructedImage is null) { StatusMessage = "没有可导出的有效重建结果。"; return; }
        var baseName = Path.GetFileNameWithoutExtension(SourcePath);
        var name = $"{baseName}.frequency-{SelectedChannel}-{SelectedBand}-{_reconstructedImage.Size.Width}x{_reconstructedImage.Size.Height}.png";
        var path = await _fileDialog.PickOutputImageAsync(name, _lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var bytes = await _codec.EncodeAsync(_reconstructedImage, ImageOutputFormat.Png, 100, _lifetime.ClosingToken).ConfigureAwait(false);
            await _fileWriter.WriteAsync(path, bytes, _lifetime.ClosingToken).ConfigureAwait(false);
            StatusMessage = $"已导出分析代理（不是原尺寸图片）：{path}";
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand] private void Cancel() { _analysisCancellation?.Cancel(); _reconstructionCancellation?.Cancel(); }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.SerializeToElement(new Snapshot(SourcePath, SelectedChannel, SelectedMaximumEdge,
            SelectedSpectrumView, LowBoundary, HighBoundary, SelectedBand, CustomInner, CustomOuter, SelectedSourceX, SelectedSourceY));
        return ValueTask.FromResult(new DocumentSaveSnapshot(new DocumentRevision(_revision), new DocumentContent(SnapshotSchema, payload)));
    }

    public void AcceptChanges(DocumentRevision savedRevision)
    {
        var wasDirty = IsDirty; if (savedRevision.Value == _revision) _acceptedRevision = _revision;
        if (wasDirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        ++_generation; CancelAndDispose(ref _analysisCancellation); CancelAndDispose(ref _reconstructionCancellation); CancelAndDispose(ref _energyCancellation);
        _session?.Dispose(); _session = null; ClearPreview(PreviewKind.Source); ClearPreview(PreviewKind.Spectrum);
        ClearPreview(PreviewKind.Mask); ClearPreview(PreviewKind.Reconstruction);
    }

    partial void OnSourcePathChanged(string value) { if (!_restoring) { InvalidateSession("图片已改变，请重新分析。"); MarkChanged(); } }
    partial void OnSelectedChannelChanged(string value) { if (!_restoring) { InvalidateSession("通道已改变，请重新分析。"); MarkChanged(); } }
    partial void OnSelectedMaximumEdgeChanged(int value) { if (!_restoring) { InvalidateSession("分析档位已改变，请重新分析。"); MarkChanged(); } }
    partial void OnSelectedSpectrumViewChanged(string value) { if (!_restoring && _session is not null) _ = RefreshSpectrumPreviewAsync(_lifetime.ClosingToken, _generation); MarkChanged(); }
    partial void OnSelectedBandChanged(string value) { OnPropertyChanged(nameof(IsCustomBand)); if (!_restoring) { MarkChanged(); _ = ReconstructCoreAsync(true); } }
    partial void OnLowBoundaryChanged(double value) { if (!_restoring) { MarkChanged(); QueueEnergyRefresh(); _ = ReconstructCoreAsync(true); } }
    partial void OnHighBoundaryChanged(double value) { if (!_restoring) { MarkChanged(); QueueEnergyRefresh(); _ = ReconstructCoreAsync(true); } }
    partial void OnCustomInnerChanged(double value) { if (!_restoring && IsCustomBand) { MarkChanged(); _ = ReconstructCoreAsync(true); } }
    partial void OnCustomOuterChanged(double value) { if (!_restoring && IsCustomBand) { MarkChanged(); _ = ReconstructCoreAsync(true); } }

    private async Task RefreshSpectrumPreviewAsync(CancellationToken token, long generation)
    {
        if (_session is null) return;
        PixelImage image;
        if (SelectedSpectrumView == "相位") image = _phaseImage!;
        else if (SelectedSpectrumView == "分块 DCT") image = _dctImage!;
        else if (SelectedSpectrumView == "对数幅度") image = _magnitudeImage!;
        else
        {
            var mode = SelectedSpectrumView == "线性幅度" ? SpectrumMagnitudeMode.Linear : SpectrumMagnitudeMode.Percentile;
            var session = _session;
            image = await Task.Run(() => _projectSpectrumUseCase.CreateMagnitude(session, mode, token), token).ConfigureAwait(true);
        }
        await ReplaceBitmapAsync(image, PreviewKind.Spectrum, token, generation).ConfigureAwait(true);
    }

    private void ReplaceSession(SpectrumAnalysisResult result)
    {
        _session?.Dispose(); _session = result.Session; _magnitudeImage = result.MagnitudePreview; _phaseImage = result.PhasePreview; _dctImage = result.DctPreview;
        _reconstructedImage = null; ClearPreview(PreviewKind.Mask); ClearPreview(PreviewKind.Reconstruction); OnPropertyChanged(nameof(HasReconstruction));
    }

    private async Task ReplaceBitmapAsync(PixelImage image, PreviewKind kind, CancellationToken token, long generation)
    {
        var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, writable: false); var replacement = new Bitmap(stream);
        if (generation != _generation || _lifetime.IsClosing) { replacement.Dispose(); return; }
        var previous = kind switch { PreviewKind.Source => SourcePreview, PreviewKind.Spectrum => SpectrumPreview, PreviewKind.Mask => MaskPreview, _ => ReconstructionPreview };
        switch (kind) { case PreviewKind.Source: SourcePreview = replacement; break; case PreviewKind.Spectrum: SpectrumPreview = replacement; break; case PreviewKind.Mask: MaskPreview = replacement; break; default: ReconstructionPreview = replacement; break; }
        previous?.Dispose();
    }

    private void InvalidateSession(string status)
    {
        ++_generation; CancelAndDispose(ref _analysisCancellation); CancelAndDispose(ref _reconstructionCancellation); CancelAndDispose(ref _energyCancellation); _session?.Dispose(); _session = null;
        _magnitudeImage = _phaseImage = _dctImage = _reconstructedImage = null; ClearPreview(PreviewKind.Source); ClearPreview(PreviewKind.Spectrum);
        ClearPreview(PreviewKind.Mask); ClearPreview(PreviewKind.Reconstruction); SizeSummary = "尚未分析"; EnergySummary = "尚无径向能量数据。"; RadialBins = Array.Empty<double>(); StatusMessage = status;
        OnPropertyChanged(nameof(HasSession)); OnPropertyChanged(nameof(HasReconstruction));
    }

    private void MarkChanged()
    {
        if (_restoring) return; var wasDirty = IsDirty; _revision++; if (!wasDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void QueueEnergyRefresh()
    {
        CancelAndDispose(ref _energyCancellation);
        if (_session is null) return;
        _energyCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        _ = RefreshEnergyAsync(_session, LowBoundary, HighBoundary, _energyCancellation);
    }

    private async Task RefreshEnergyAsync(SpectrumAnalysisSession session, double low, double high, CancellationTokenSource current)
    {
        var operationToken = current.Token;
        try
        {
            await Task.Delay(150, operationToken).ConfigureAwait(true);
            var boundaries = new FrequencyBandBoundaries(low, high);
            var radial = await Task.Run(() => _projectSpectrumUseCase.AnalyzeEnergy(session, boundaries, operationToken), operationToken).ConfigureAwait(true);
            if (!ReferenceEquals(session, _session) || !ReferenceEquals(current, _energyCancellation)) return;
            RadialBins = radial.Bins;
            EnergySummary = $"DC {radial.DcShare:P2}；低频 {radial.LowShare:P2}；中频 {radial.MediumShare:P2}；高频 {radial.HighShare:P2}";
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested) { }
        catch (ArgumentOutOfRangeException) { StatusMessage = "频带边界必须满足 0 < low < high < 1。"; }
    }

    private void ValidateSource()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || !File.Exists(SourcePath)) throw new InvalidOperationException("请选择存在的 PNG 或 JPEG 图片。 ");
        _ = new FrequencyBandBoundaries(LowBoundary, HighBoundary); _ = ResolveBand();
    }

    private ImageChannel ResolveChannel() => SelectedChannel switch { "R" => ImageChannel.Red, "G" => ImageChannel.Green, "B" => ImageChannel.Blue, "Cb" => ImageChannel.ChromaBlue, "Cr" => ImageChannel.ChromaRed, _ => ImageChannel.Luma };
    private FrequencyBandDefinition ResolveBand()
    {
        var boundaries = new FrequencyBandBoundaries(LowBoundary, HighBoundary);
        var kind = SelectedBand switch { "低频" => FrequencyBandKind.Low, "中频" => FrequencyBandKind.Medium, "高频" => FrequencyBandKind.High, "自定义" => FrequencyBandKind.Custom, _ => FrequencyBandKind.All };
        return new FrequencyBandDefinition(kind, boundaries, CustomInner, CustomOuter);
    }

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema) { StatusMessage = $"不支持 schema {content.SchemaVersion}，已使用安全默认值。"; return; }
        var value = content.Payload.Deserialize<Snapshot>(); if (value is null) return;
        SourcePath = value.SourcePath ?? string.Empty; SelectedChannel = ChannelOptions.Contains(value.Channel) ? value.Channel : "Y";
        SelectedMaximumEdge = MaximumEdgeOptions.Contains(value.MaximumEdge) ? value.MaximumEdge : 1024;
        SelectedSpectrumView = SpectrumViewOptions.Contains(value.View) ? value.View : "对数幅度";
        try { _ = new FrequencyBandBoundaries(value.Low, value.High); LowBoundary = value.Low; HighBoundary = value.High; } catch { LowBoundary = 0.15d; HighBoundary = 0.50d; }
        SelectedBand = BandOptions.Contains(value.Band) ? value.Band : "全部";
        if (value.Inner >= 0d && value.Inner < value.Outer && value.Outer <= 1d) { CustomInner = value.Inner; CustomOuter = value.Outer; }
        SelectedSourceX = Math.Max(0, value.SourceX); SelectedSourceY = Math.Max(0, value.SourceY);
        StatusMessage = File.Exists(SourcePath) ? "已恢复路径和参数；请显式点击“分析”。" : "已恢复参数，但源图片不存在，请重新选择。";
    }

    private static string FormatMatrix(IReadOnlyList<double> values) => string.Join('\n', Enumerable.Range(0, 8).Select(y => string.Join("  ", values.Skip(y * 8).Take(8).Select(v => v.ToString("0.00").PadLeft(9)))));
    private static string RegionName(FrequencyRegion region) => region switch { FrequencyRegion.Dc => "DC", FrequencyRegion.Low => "低频", FrequencyRegion.Medium => "中频", _ => "高频" };
    private static void CancelAndDispose(ref CancellationTokenSource? source) { source?.Cancel(); source?.Dispose(); source = null; }
    private void ClearPreview(PreviewKind kind)
    {
        var bitmap = kind switch { PreviewKind.Source => SourcePreview, PreviewKind.Spectrum => SpectrumPreview, PreviewKind.Mask => MaskPreview, _ => ReconstructionPreview };
        switch (kind) { case PreviewKind.Source: SourcePreview = null; break; case PreviewKind.Spectrum: SpectrumPreview = null; break; case PreviewKind.Mask: MaskPreview = null; break; default: ReconstructionPreview = null; break; }
        bitmap?.Dispose();
    }

    private enum PreviewKind { Source, Spectrum, Mask, Reconstruction }

    private sealed record Snapshot(string? SourcePath, string Channel, int MaximumEdge, string View, double Low, double High, string Band, double Inner, double Outer, int SourceX, int SourceY);
}
