using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.FrequencyFiltering;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.Imaging;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.FrequencyFilter;

/// <summary>“频域滤波”多实例 Document：拥有轻量配方、Session 生命周期、取消和显示投影。</summary>
/// <remarks>
/// 所有数值循环均在 Domain/Application；本类只把用户意图交给五个窄用例。Session 参数变化会释放 FFT 缓存，
/// 数学参数变化会使频域/空间/原尺寸结果 stale，空间核尺寸只使空间比较 stale。每条异步分支都用 generation
/// 拒绝迟到提交，即使底层在取消边界前完成，也不能覆盖更新后的图片或配方。
/// </remarks>
internal sealed partial class FrequencyFilterDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private readonly IPrepareFrequencyFilterSessionUseCase _prepare;
    private readonly IApplyFrequencyFilterUseCase _apply;
    private readonly ICompareFrequencySpatialUseCase _compare;
    private readonly IRenderFullFrequencyFilterUseCase _renderFull;
    private readonly IExportFrequencyFilterImageUseCase _export;
    private readonly IImageFileDialog _dialog;
    private readonly IImageCodec _codec;
    private readonly IDocumentLifetime _lifetime;
    private DocumentPresentationState _presentation = new("频域滤波");
    private FrequencyFilterSession? _session;
    private FrequencyFilterResult? _proxyResult;
    private FrequencyFilterResult? _fullResult;
    private CancellationTokenSource? _prepareCancellation;
    private CancellationTokenSource? _filterCancellation;
    private CancellationTokenSource? _spatialCancellation;
    private CancellationTokenSource? _fullCancellation;
    private CancellationTokenSource? _exportCancellation;
    private long _generation;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;
    private bool _disposed;

    public FrequencyFilterDocument(IPrepareFrequencyFilterSessionUseCase prepare, IApplyFrequencyFilterUseCase apply,
        ICompareFrequencySpatialUseCase compare, IRenderFullFrequencyFilterUseCase renderFull,
        IExportFrequencyFilterImageUseCase export, IImageFileDialog dialog, IImageCodec codec, IDocumentLifetime lifetime)
    { _prepare = prepare; _apply = apply; _compare = compare; _renderFull = renderFull; _export = export; _dialog = dialog; _codec = codec; _lifetime = lifetime; }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _selectedChannel = "Y";
    [ObservableProperty] private int _analysisMaximumEdge = 1024;
    [ObservableProperty] private string _selectedKind = "低通";
    [ObservableProperty] private string _selectedFamily = "Gaussian";
    [ObservableProperty] private double _innerCutoff = 0.2d;
    [ObservableProperty] private double _outerCutoff = 0.65d;
    [ObservableProperty] private int _butterworthOrder = 2;
    [ObservableProperty] private string _selectedProjection = "Direct";
    [ObservableProperty] private double _projectionGain = 1d;
    [ObservableProperty] private int _kernelSize = 7;
    [ObservableProperty] private double _profileX = 0.5d;
    [ObservableProperty] private double _profileY = 0.5d;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isSpatialBusy;
    [ObservableProperty] private bool _isFullBusy;
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private string _statusMessage = "请选择 PNG/JPEG 图片并建立分析 Session。";
    [ObservableProperty] private string _sizeSummary = "尚未载入";
    [ObservableProperty] private string _resultSummary = "尚未执行滤波";
    [ObservableProperty] private string _transitionSummary = "执行后显示真实 90%–10% 径向响应";
    [ObservableProperty] private string _spatialSummary = "尚未执行有限空间核近似；它不是完整冲激响应的精确等价。";
    [ObservableProperty] private IReadOnlyList<double> _responseValues = Array.Empty<double>();
    [ObservableProperty] private Bitmap? _sourcePreview;
    [ObservableProperty] private Bitmap? _spectrumPreview;
    [ObservableProperty] private Bitmap? _maskPreview;
    [ObservableProperty] private Bitmap? _resultPreview;
    [ObservableProperty] private Bitmap? _differencePreview;

    public IReadOnlyList<string> ChannelOptions { get; } = ["R", "G", "B", "Y", "Cb", "Cr"];
    public IReadOnlyList<int> AnalysisEdgeOptions { get; } = [512, 1024, 2048];
    public IReadOnlyList<string> KindOptions { get; } = ["低通", "高通", "带通", "带阻"];
    public IReadOnlyList<string> FamilyOptions { get; } = ["Ideal", "Butterworth", "Gaussian"];
    public IReadOnlyList<string> ProjectionOptions { get; } = ["Direct", "Centered", "Additive"];
    public IReadOnlyList<int> KernelSizeOptions { get; } = [7, 15, 31];
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasSession => _session is not null;
    public bool HasResult => _proxyResult is not null;
    public bool UsesTwoCutoffs => SelectedKind is "带通" or "带阻";
    public bool IsButterworth => SelectedFamily == "Butterworth";
    public bool UsesProjectionGain => SelectedProjection != "Direct";
    public bool CanRenderFull => _session?.CanRenderFullSize == true && !IsFullBusy;
    public bool CanExport => CurrentResult() is not null && !IsExporting;
    public bool IsOperationBusy => IsBusy || IsSpatialBusy || IsFullBusy || IsExporting;

    public event EventHandler? PresentationChanged;
    public event EventHandler? IsDirtyChanged;

    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation); cancellationToken.ThrowIfCancellationRequested(); _restoring = true;
        try
        {
            if (activation is RestoreDocumentActivation restore) Restore(restore.RestoredContent);
            _presentation = new DocumentPresentationState(string.IsNullOrWhiteSpace(activation.Title) ? "频域滤波" : activation.Title);
            PresentationChanged?.Invoke(this, EventArgs.Empty); _revision = _acceptedRevision = 0;
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
    private async Task PrepareAsync()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || !File.Exists(SourcePath)) { StatusMessage = "请选择存在的 PNG 或 JPEG 图片。"; return; }
        CancelAndDispose(ref _prepareCancellation); CancelResultOperations();
        _prepareCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _prepareCancellation; var token = current.Token; var generation = ++_generation; IsBusy = true;
        try
        {
            StatusMessage = "正在解码一次、建立分析代理并缓存全局 FFT…";
            var session = await _prepare.ExecuteAsync(new(SourcePath, ResolveChannel(), AnalysisMaximumEdge), token).ConfigureAwait(true);
            var sourceBitmap = await CreateBitmapAsync(session.AnalysisProxy, token).ConfigureAwait(true);
            var spectrumBitmap = await CreateBitmapAsync(session.MagnitudePreview, token).ConfigureAwait(true);
            if (!CanCommit(generation)) { session.Dispose(); sourceBitmap.Dispose(); spectrumBitmap.Dispose(); return; }
            ReplaceSession(session); ReplaceSourceBitmap(sourceBitmap); ReplaceSpectrumBitmap(spectrumBitmap);
            SizeSummary = $"原图 {session.SourceImage.Size.Width}×{session.SourceImage.Size.Height}；代理 {session.AnalysisProxy.Size.Width}×{session.AnalysisProxy.Size.Height}；FFT {session.Spectrum.PaddedWidth}×{session.Spectrum.PaddedHeight}";
            StatusMessage = session.CanRenderFullSize ? "Session 已就绪；原图也在 2048² FFT 预算内。" : "Session 已就绪；原图超出完整尺寸 FFT 预算，仍可导出代理结果。";
            OnPropertyChanged(nameof(HasSession)); OnPropertyChanged(nameof(CanRenderFull)); await FilterCoreAsync(debounce: false).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { if (!_lifetime.IsClosing) StatusMessage = "准备已取消，未提交半成品。"; }
        catch (Exception exception) { if (generation == _generation) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _prepareCancellation)) IsBusy = false; }
    }

    [RelayCommand] private Task ApplyAsync() => FilterCoreAsync(debounce: false);

    private async Task FilterCoreAsync(bool debounce)
    {
        var session = _session; if (session is null) { StatusMessage = "请先建立分析 Session。"; return; }
        if (!TryRecipe(out var recipe, out var error)) { StatusMessage = error!; return; }
        CancelAndDispose(ref _filterCancellation); _filterCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _filterCancellation; var token = current.Token; var generation = ++_generation; IsBusy = true;
        try
        {
            if (debounce) await Task.Delay(150, token).ConfigureAwait(true);
            var result = await _apply.ExecuteAsync(session, recipe, token).ConfigureAwait(true);
            var mask = await CreateBitmapAsync(result.MaskPreview, token).ConfigureAwait(true);
            var image = await CreateBitmapAsync(result.Projection.Image, token).ConfigureAwait(true);
            var difference = await CreateBitmapAsync(result.Difference.Signed, token).ConfigureAwait(true);
            if (!CanCommit(generation) || !ReferenceEquals(session, _session) || result.RecipeFingerprint != recipe.Fingerprint())
            { mask.Dispose(); image.Dispose(); difference.Dispose(); return; }
            _proxyResult = result; _fullResult = null; ReplaceMaskBitmap(mask);
            ReplaceResultBitmap(image); ReplaceDifferenceBitmap(difference);
            ResponseValues = result.Mask.RadialSamples.Select(item => item.Gain).ToArray();
            var d = result.Diagnostics; var p = result.Projection.Statistics;
            ResultSummary = $"代理 {result.Projection.Image.Size.Width}×{result.Projection.Image.Size.Height}；raw {d.FilteredMinimum:0.###}..{d.FilteredMaximum:0.###}；raw 越界 {d.FilteredBelowZero:N0}/{d.FilteredAbove255:N0}；投影裁切 {p.LowClippedSamples:N0}/{p.HighClippedSamples:N0}；MAE {d.MeanAbsoluteDifference:0.###}；PSNR-Y {result.Quality.PsnrLumaDb:0.###} dB；SSIM-Y {result.Quality.GlobalSsimLuma:0.####}。";
            TransitionSummary = $"真实径向响应 {result.Mask.RadialSamples.Count} 点；输出 {SelectedProjection}；raw 缓存命中={result.Timings.UsedCachedRaw}；IFFT 虚部 {result.Raw.MaximumImaginaryResidual:E2}。";
            SpatialSummary = "滤波配方已改变，空间有限核近似需重新执行。"; StatusMessage = $"代理滤波完成，配方 {recipe.Fingerprint()}。";
            OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(CanExport));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { if (generation == _generation) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _filterCancellation)) IsBusy = false; }
    }

    [RelayCommand]
    private async Task CompareSpatialAsync()
    {
        var session = _session; if (session is null) { StatusMessage = "请先建立 Session。"; return; }
        if (!TryRecipe(out var recipe, out var error)) { StatusMessage = error ?? "配方无效。"; return; }
        CancelAndDispose(ref _spatialCancellation); _spatialCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _spatialCancellation; var token = current.Token; var generation = _generation; IsSpatialBusy = true;
        try
        {
            var result = await _compare.ExecuteAsync(session, recipe, KernelSize, token).ConfigureAwait(true);
            if (!CanCommit(generation) || !ReferenceEquals(session, _session)) return;
            SpatialSummary = $"{KernelSize}×{KernelSize} 截断核，Wrap/padded/raw double 近似；和 {result.ImpulseKernel.SumBeforeCorrection:0.######}→{result.ImpulseKernel.SumAfterCorrection:0.######}；L1/L2 保留 {result.ImpulseKernel.RetainedL1Ratio:P2}/{result.ImpulseKernel.RetainedL2Ratio:P2}；MAE {result.MeanAbsoluteError:0.######}，max {result.MaximumAbsoluteError:0.######}；本机中位 FFT {result.FrequencyElapsed.TotalMilliseconds:0.###} ms，空间 {result.SpatialElapsed.TotalMilliseconds:0.###} ms。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _spatialCancellation)) IsSpatialBusy = false; }
    }

    [RelayCommand]
    private async Task RenderFullAsync()
    {
        var session = _session; if (session is null) { StatusMessage = "请先建立 Session。"; return; }
        if (!TryRecipe(out var recipe, out var error)) { StatusMessage = error ?? "配方无效。"; return; }
        CancelAndDispose(ref _fullCancellation); _fullCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _fullCancellation; var token = current.Token; var generation = _generation; IsFullBusy = true;
        try
        {
            var result = await _renderFull.ExecuteAsync(session, recipe, token).ConfigureAwait(true);
            if (!CanCommit(generation) || !ReferenceEquals(session, _session)) return;
            _fullResult = result; StatusMessage = $"完整尺寸 {result.Projection.Image.Size.Width}×{result.Projection.Image.Size.Height} 已生成；导出将优先使用它。"; OnPropertyChanged(nameof(CanExport));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _fullCancellation)) IsFullBusy = false; }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var session = _session; var result = CurrentResult();
        if (session is null || result is null) { StatusMessage = "没有可导出的当前结果。"; return; }
        if (!TryRecipe(out var recipe, out var error)) { StatusMessage = error ?? "配方无效。"; return; }
        var sizeTag = result.IsFullSize ? "full" : $"proxy-{result.Projection.Image.Size.Width}x{result.Projection.Image.Size.Height}";
        var name = $"{Path.GetFileNameWithoutExtension(SourcePath)}.frequency-{SelectedProjection.ToLowerInvariant()}-{sizeTag}.png";
        var path = await _dialog.PickOutputImageAsync(name, _lifetime.ClosingToken).ConfigureAwait(true); if (string.IsNullOrWhiteSpace(path)) return;
        CancelAndDispose(ref _exportCancellation); _exportCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var token = _exportCancellation.Token; IsExporting = true;
        try
        {
            var saved = await _export.ExecuteAsync(new(result, session.SessionFingerprint, recipe.Fingerprint(), path), token).ConfigureAwait(true);
            StatusMessage = $"已原子导出 {(saved.IsFullSize ? "完整尺寸" : "代理")} PNG：{saved.OutputPath}。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { StatusMessage = "导出已取消，未报告成功。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { IsExporting = false; }
    }

    [RelayCommand] private void Cancel() { _prepareCancellation?.Cancel(); _filterCancellation?.Cancel(); _spatialCancellation?.Cancel(); _fullCancellation?.Cancel(); _exportCancellation?.Cancel(); }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.SerializeToElement(new Snapshot(SourcePath, SelectedChannel, AnalysisMaximumEdge, SelectedKind,
            SelectedFamily, InnerCutoff, OuterCutoff, ButterworthOrder, SelectedProjection, ProjectionGain, KernelSize, ProfileX, ProfileY));
        return ValueTask.FromResult(new DocumentSaveSnapshot(new DocumentRevision(_revision), new DocumentContent(SnapshotSchema, payload)));
    }
    public void AcceptChanges(DocumentRevision savedRevision)
    { var wasDirty = IsDirty; if (savedRevision.Value == _revision) _acceptedRevision = _revision; if (wasDirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_generation; CancelAndDispose(ref _prepareCancellation); CancelResultOperations(); ReplaceSession(null);
        ReplaceSourceBitmap(null); ReplaceSpectrumBitmap(null); ReplaceMaskBitmap(null); ReplaceResultBitmap(null); ReplaceDifferenceBitmap(null);
    }

    partial void OnSourcePathChanged(string value) { if (!_restoring) { InvalidateSession("图片路径已改变，请显式重新载入。"); MarkChanged(); } }
    partial void OnSelectedChannelChanged(string value) { if (!_restoring) { InvalidateSession("通道已改变，请重新建立 FFT Session。"); MarkChanged(); } }
    partial void OnAnalysisMaximumEdgeChanged(int value) { if (!_restoring) { InvalidateSession("代理档位已改变，请重新建立 FFT Session。"); MarkChanged(); } }
    partial void OnSelectedKindChanged(string value) { OnPropertyChanged(nameof(UsesTwoCutoffs)); FilterParameterChanged(); }
    partial void OnSelectedFamilyChanged(string value) { OnPropertyChanged(nameof(IsButterworth)); FilterParameterChanged(); }
    partial void OnInnerCutoffChanged(double value) => FilterParameterChanged();
    partial void OnOuterCutoffChanged(double value) => FilterParameterChanged();
    partial void OnButterworthOrderChanged(int value) => FilterParameterChanged();
    partial void OnSelectedProjectionChanged(string value) { OnPropertyChanged(nameof(UsesProjectionGain)); FilterParameterChanged(); }
    partial void OnProjectionGainChanged(double value) => FilterParameterChanged();
    partial void OnKernelSizeChanged(int value) { if (!_restoring) { CancelAndDispose(ref _spatialCancellation); SpatialSummary = "核尺寸已改变，空间近似已过期。"; MarkChanged(); } }
    partial void OnProfileXChanged(double value) { if (!_restoring) MarkChanged(); }
    partial void OnProfileYChanged(double value) { if (!_restoring) MarkChanged(); }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsOperationBusy));
    partial void OnIsSpatialBusyChanged(bool value) => OnPropertyChanged(nameof(IsOperationBusy));
    partial void OnIsFullBusyChanged(bool value) { OnPropertyChanged(nameof(IsOperationBusy)); OnPropertyChanged(nameof(CanRenderFull)); }
    partial void OnIsExportingChanged(bool value) { OnPropertyChanged(nameof(IsOperationBusy)); OnPropertyChanged(nameof(CanExport)); }

    private void FilterParameterChanged()
    {
        if (_restoring) return; ++_generation; CancelResultOperations(); _proxyResult = null; _fullResult = null;
        ResultSummary = "配方已改变，旧结果已过期。"; SpatialSummary = "配方已改变，空间近似已过期。"; MarkChanged();
        OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(CanExport)); if (_session is not null) _ = FilterCoreAsync(debounce: true);
    }
    private void InvalidateSession(string status)
    {
        ++_generation; CancelResultOperations(); ReplaceSession(null); _proxyResult = null; _fullResult = null; StatusMessage = status;
        OnPropertyChanged(nameof(HasSession)); OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(CanRenderFull)); OnPropertyChanged(nameof(CanExport));
    }
    private bool TryRecipe(out FrequencyFilterRecipe recipe, out string? error)
    {
        try
        {
            var kind = SelectedKind switch { "高通" => FrequencyFilterKind.HighPass, "带通" => FrequencyFilterKind.BandPass, "带阻" => FrequencyFilterKind.BandStop, _ => FrequencyFilterKind.LowPass };
            var family = SelectedFamily switch { "Ideal" => FrequencyFilterFamily.Ideal, "Butterworth" => FrequencyFilterFamily.Butterworth, _ => FrequencyFilterFamily.Gaussian };
            var projection = Enum.Parse<FrequencyProjectionMode>(SelectedProjection);
            recipe = new(kind, family, InnerCutoff, OuterCutoff, ButterworthOrder, projection, ProjectionGain, ResolveChannel()); error = null; return true;
        }
        catch (Exception exception) { recipe = null!; error = exception.Message; return false; }
    }
    private ImageChannel ResolveChannel() => SelectedChannel switch { "R" => ImageChannel.Red, "G" => ImageChannel.Green, "B" => ImageChannel.Blue, "Cb" => ImageChannel.ChromaBlue, "Cr" => ImageChannel.ChromaRed, _ => ImageChannel.Luma };
    private FrequencyFilterResult? CurrentResult()
    {
        if (!TryRecipe(out var recipe, out _) || _session is null) return null;
        if (_fullResult?.RecipeFingerprint == recipe.Fingerprint() && _fullResult.SessionFingerprint == _session.SessionFingerprint) return _fullResult;
        return _proxyResult?.RecipeFingerprint == recipe.Fingerprint() && _proxyResult.SessionFingerprint == _session.SessionFingerprint ? _proxyResult : null;
    }
    private async Task<Bitmap> CreateBitmapAsync(PixelImage image, CancellationToken token)
    { var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token).ConfigureAwait(false); using var stream = new MemoryStream(bytes, false); return new Bitmap(stream); }
    private bool CanCommit(long generation) => generation == _generation && !_lifetime.IsClosing && !_disposed;
    private void ReplaceSession(FrequencyFilterSession? value) { var previous = _session; _session = value; previous?.Dispose(); }
    private void ReplaceSourceBitmap(Bitmap? value) { var previous = SourcePreview; SourcePreview = value; previous?.Dispose(); }
    private void ReplaceSpectrumBitmap(Bitmap? value) { var previous = SpectrumPreview; SpectrumPreview = value; previous?.Dispose(); }
    private void ReplaceMaskBitmap(Bitmap? value) { var previous = MaskPreview; MaskPreview = value; previous?.Dispose(); }
    private void ReplaceResultBitmap(Bitmap? value) { var previous = ResultPreview; ResultPreview = value; previous?.Dispose(); }
    private void ReplaceDifferenceBitmap(Bitmap? value) { var previous = DifferencePreview; DifferencePreview = value; previous?.Dispose(); }
    private void CancelResultOperations() { CancelAndDispose(ref _filterCancellation); CancelAndDispose(ref _spatialCancellation); CancelAndDispose(ref _fullCancellation); CancelAndDispose(ref _exportCancellation); }
    private static void CancelAndDispose(ref CancellationTokenSource? source) { source?.Cancel(); source?.Dispose(); source = null; }
    private void MarkChanged() { if (_restoring) return; var wasDirty = IsDirty; _revision++; if (wasDirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema) { StatusMessage = $"不支持 schema {content.SchemaVersion}，已使用安全默认值。"; return; }
        try
        {
            var value = content.Payload.Deserialize<Snapshot>(); if (value is null) return;
            SourcePath = value.SourcePath ?? string.Empty; SelectedChannel = ChannelOptions.Contains(value.Channel) ? value.Channel : "Y";
            AnalysisMaximumEdge = AnalysisEdgeOptions.Contains(value.AnalysisEdge) ? value.AnalysisEdge : 1024;
            SelectedKind = KindOptions.Contains(value.Kind) ? value.Kind : "低通"; SelectedFamily = FamilyOptions.Contains(value.Family) ? value.Family : "Gaussian";
            InnerCutoff = value.InnerCutoff is > 0 and <= 1 ? value.InnerCutoff : 0.2; OuterCutoff = value.OuterCutoff > InnerCutoff && value.OuterCutoff <= 1 ? value.OuterCutoff : 0.65;
            ButterworthOrder = value.Order is >= 1 and <= 12 ? value.Order : 2; SelectedProjection = ProjectionOptions.Contains(value.Projection) ? value.Projection : "Direct";
            ProjectionGain = value.Gain is >= 0 and <= 4 ? value.Gain : 1; KernelSize = KernelSizeOptions.Contains(value.KernelSize) ? value.KernelSize : 7;
            ProfileX = value.ProfileX is >= 0 and <= 1 ? value.ProfileX : 0.5; ProfileY = value.ProfileY is >= 0 and <= 1 ? value.ProfileY : 0.5;
            StatusMessage = File.Exists(SourcePath) ? "已恢复轻量参数；请显式载入，不会自动解码或 FFT。" : "已恢复参数，但源图片不存在，请重新选择。";
        }
        catch (Exception exception) { StatusMessage = $"快照无效，已保留安全默认值：{exception.Message}"; }
    }
    private sealed record Snapshot(string? SourcePath, string Channel, int AnalysisEdge, string Kind, string Family,
        double InnerCutoff, double OuterCutoff, int Order, string Projection, double Gain, int KernelSize, double ProfileX, double ProfileY);
}
