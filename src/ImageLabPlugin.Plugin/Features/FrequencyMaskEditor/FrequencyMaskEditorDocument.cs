using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.FrequencyMaskEditing;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.FrequencyMaskEditing;
using ImageLabPlugin.Domain.Shared.Imaging;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.FrequencyMaskEditor;

/// <summary>频谱遮罩编辑器的多实例 Document：只协调状态、窄用例、历史、取消和 Bitmap 生命周期。</summary>
/// <remarks>
/// FFT、共轭写入、画笔光栅化和 JSON DTO 均位于 Domain/Application/Infrastructure。本类用 generation 拒绝迟到结果；
/// 一次 gesture 只在释放时形成一条操作，强度变化不进入历史，快照只保存有界配方文本和轻量视图参数。
/// </remarks>
internal sealed partial class FrequencyMaskEditorDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private readonly IPrepareFrequencyMaskEditorSessionUseCase _prepare;
    private readonly IRenderFrequencyMaskUseCase _render;
    private readonly IRenderFullFrequencyMaskUseCase _renderFull;
    private readonly IExportFrequencyMaskImageUseCase _exportImage;
    private readonly IImportFrequencyMaskRecipeUseCase _importRecipe;
    private readonly IExportFrequencyMaskRecipeUseCase _exportRecipe;
    private readonly IFrequencyMaskRecipeSerializer _serializer;
    private readonly IInspectFrequencyMaskPointUseCase _inspect;
    private readonly IImageFileDialog _imageDialog;
    private readonly IFrequencyMaskRecipeFileDialog _recipeDialog;
    private readonly IImageCodec _codec;
    private readonly IDocumentLifetime _lifetime;
    private readonly MaskEditHistory _history = new();
    private DocumentPresentationState _presentation = new("频谱遮罩编辑器");
    private FrequencyMaskEditorSession? _session;
    private FrequencyMaskRenderResult? _proxyResult;
    private FrequencyMaskRenderResult? _fullResult;
    private CancellationTokenSource? _prepareCancellation;
    private CancellationTokenSource? _renderCancellation;
    private CancellationTokenSource? _fullCancellation;
    private CancellationTokenSource? _ioCancellation;
    private int? _originalPaddedWidth;
    private int? _originalPaddedHeight;
    private long _generation;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;
    private bool _disposed;

    public FrequencyMaskEditorDocument(IPrepareFrequencyMaskEditorSessionUseCase prepare,
        IRenderFrequencyMaskUseCase render, IRenderFullFrequencyMaskUseCase renderFull,
        IExportFrequencyMaskImageUseCase exportImage, IImportFrequencyMaskRecipeUseCase importRecipe,
        IExportFrequencyMaskRecipeUseCase exportRecipe, IFrequencyMaskRecipeSerializer serializer,
        IInspectFrequencyMaskPointUseCase inspect, IImageFileDialog imageDialog,
        IFrequencyMaskRecipeFileDialog recipeDialog, IImageCodec codec, IDocumentLifetime lifetime)
    {
        _prepare = prepare;
        _render = render;
        _renderFull = renderFull;
        _exportImage = exportImage;
        _importRecipe = importRecipe;
        _exportRecipe = exportRecipe;
        _serializer = serializer;
        _inspect = inspect;
        _imageDialog = imageDialog;
        _recipeDialog = recipeDialog;
        _codec = codec;
        _lifetime = lifetime;
    }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _selectedChannel = "Y";
    [ObservableProperty] private int _analysisMaximumEdge = 1024;
    [ObservableProperty] private string _selectedTool = "衰减画笔";
    [ObservableProperty] private double _brushRadius = 0.035d;
    [ObservableProperty] private double _targetGain;
    [ObservableProperty] private double _opacity = 1d;
    [ObservableProperty] private double _ringInnerRatio = 0.5d;
    [ObservableProperty] private bool _isBandLockEnabled;
    [ObservableProperty] private double _bandInnerRadius;
    [ObservableProperty] private double _bandOuterRadius = 1d;
    private double _strength = 1d;
    [ObservableProperty] private double _maskOpacity = 0.55d;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isFullBusy;
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private string _statusMessage = "请选择 PNG/JPEG 图片并建立分析 Session。";
    [ObservableProperty] private string _sizeSummary = "尚未载入";
    [ObservableProperty] private string _diagnosticsSummary = "初始遮罩为全通；编辑后显示数值诊断。";
    [ObservableProperty] private string _probeSummary = "在频谱画布移动指针可检查频点。";
    [ObservableProperty] private double _probeX = -1d;
    [ObservableProperty] private double _probeY = -1d;
    [ObservableProperty] private double _mirrorX = -1d;
    [ObservableProperty] private double _mirrorY = -1d;
    private Bitmap? _sourcePreview;
    private Bitmap? _spectrumPreview;
    private Bitmap? _maskPreview;
    private Bitmap? _resultPreview;
    private Bitmap? _differencePreview;

    public IReadOnlyList<string> ChannelOptions { get; } = ["R", "G", "B", "Y", "Cb", "Cr"];
    public IReadOnlyList<int> AnalysisEdgeOptions { get; } = [512, 1024, 2048];
    public IReadOnlyList<string> ToolOptions { get; } = ["衰减画笔", "恢复橡皮", "矩形", "圆环"];
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasSession => _session is not null;
    public bool HasResult => CurrentProxyResult() is not null;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public bool CanRenderFull => _session?.CanRenderFullSize == true && !IsFullBusy;
    public bool CanExport => CurrentResult() is not null && !IsExporting;
    public bool IsOperationBusy => IsBusy || IsFullBusy || IsExporting;
    public double Strength
    {
        get => _strength;
        set
        {
            if (!double.IsFinite(value) || value is < 0d or > 1d)
                throw new ArgumentOutOfRangeException(nameof(value), "全局遮罩强度必须位于 [0,1]。");
            if (!SetProperty(ref _strength, value) || _restoring) return;
            RecipeChanged("全局强度已改变；编辑历史保持不变。");
        }
    }
    public Bitmap? SourcePreview => _sourcePreview;
    public Bitmap? SpectrumPreview => _spectrumPreview;
    public Bitmap? MaskPreview => _maskPreview;
    public Bitmap? ResultPreview => _resultPreview;
    public Bitmap? DifferencePreview => _differencePreview;

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
            _presentation = new DocumentPresentationState(string.IsNullOrWhiteSpace(activation.Title)
                ? "频谱遮罩编辑器" : activation.Title);
            PresentationChanged?.Invoke(this, EventArgs.Empty);
            _revision = _acceptedRevision = 0;
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
    private async Task PrepareAsync()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || !File.Exists(SourcePath))
        {
            StatusMessage = "请选择存在的 PNG 或 JPEG 图片。";
            return;
        }
        CancelAndDispose(ref _prepareCancellation);
        CancelResultOperations();
        _prepareCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _prepareCancellation;
        var token = current.Token;
        var generation = ++_generation;
        IsBusy = true;
        try
        {
            StatusMessage = "正在解码一次、建立分析代理并缓存只读全局 FFT…";
            var session = await _prepare.ExecuteAsync(new(SourcePath, ResolveChannel(), AnalysisMaximumEdge), token).ConfigureAwait(true);
            var sourceBitmap = await CreateBitmapAsync(session.AnalysisProxy, token).ConfigureAwait(true);
            var spectrumBitmap = await CreateBitmapAsync(session.MagnitudePreview, token).ConfigureAwait(true);
            if (!CanCommit(generation))
            {
                session.Dispose(); sourceBitmap.Dispose(); spectrumBitmap.Dispose(); return;
            }
            ReplaceSession(session);
            ReplaceBitmap(ref _sourcePreview, sourceBitmap, nameof(SourcePreview));
            ReplaceBitmap(ref _spectrumPreview, spectrumBitmap, nameof(SpectrumPreview));
            _originalPaddedWidth ??= session.Spectrum.PaddedWidth;
            _originalPaddedHeight ??= session.Spectrum.PaddedHeight;
            SizeSummary = $"原图 {session.SourceImage.Size.Width}×{session.SourceImage.Size.Height}；代理 {session.AnalysisProxy.Size.Width}×{session.AnalysisProxy.Size.Height}；FFT {session.Spectrum.PaddedWidth}×{session.Spectrum.PaddedHeight}";
            StatusMessage = session.CanRenderFullSize
                ? "Session 已就绪；原图也在 2048² FFT 预算内。"
                : "Session 已就绪；原图超出完整尺寸 FFT 预算，只提供代理结果。";
            NotifyState();
            await RenderCoreAsync(false).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (!_lifetime.IsClosing) StatusMessage = "准备已取消，未提交半成品。";
        }
        catch (Exception exception) { if (generation == _generation) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _prepareCancellation)) IsBusy = false; }
    }

    [RelayCommand] private Task ApplyAsync() => RenderCoreAsync(false);

    internal void CommitGesture(IReadOnlyList<NormalizedFrequencyPoint> points)
    {
        if (_session is null) { StatusMessage = "请先建立分析 Session。"; return; }
        try
        {
            if (points.Count == 0) return;
            var band = CurrentBandLock();
            FrequencyMaskOperation operation = SelectedTool switch
            {
                "恢复橡皮" => FrequencyMaskOperation.Eraser(points.ToArray(), BrushRadius, Opacity, band),
                "矩形" when points.Count >= 2 => FrequencyMaskOperation.Rectangle(points[0], points[^1], TargetGain, Opacity, band),
                "圆环" when points.Count >= 2 => CreateRing(points[0], points[^1], band),
                "矩形" or "圆环" => throw new InvalidOperationException("矩形或圆环需要拖动形成有效几何。"),
                _ => FrequencyMaskOperation.Brush(points.ToArray(), BrushRadius, TargetGain, Opacity, band)
            };
            CommitOperation(operation, $"已提交一次{SelectedTool}操作。");
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    internal void InspectAt(double normalizedX, double normalizedY)
    {
        var session = _session;
        var result = CurrentProxyResult();
        if (session is null || result is null) return;
        try
        {
            var point = _inspect.Execute(session, result, normalizedX, normalizedY);
            ProbeX = point.DisplayX / (double)Math.Max(1, result.EffectiveMask.Width - 1);
            ProbeY = point.DisplayY / (double)Math.Max(1, result.EffectiveMask.Height - 1);
            MirrorX = point.ConjugateDisplayX / (double)Math.Max(1, result.EffectiveMask.Width - 1);
            MirrorY = point.ConjugateDisplayY / (double)Math.Max(1, result.EffectiveMask.Height - 1);
            ProbeSummary = $"显示({point.DisplayX},{point.DisplayY}) → 自然({point.InternalX},{point.InternalY})；共轭({point.ConjugateInternalX},{point.ConjugateInternalY})；f=({point.FrequencyX:0.####},{point.FrequencyY:0.####})，r={point.Radius:0.####}；|F|={point.OriginalMagnitude:0.###}；M={point.EditGain:0.####}，H={point.EffectiveGain:0.####}";
        }
        catch (Exception exception) { ProbeSummary = exception.Message; }
    }

    [RelayCommand] private void InvertAll() => CommitOperation(FrequencyMaskOperation.Invert(), "已反转全部增益。");
    [RelayCommand] private void ResetAllPass() => CommitOperation(FrequencyMaskOperation.Reset(), "已重置为全通；可撤销。");

    [RelayCommand]
    private void Undo()
    {
        if (!_history.Undo()) return;
        RecipeChanged("已撤销一步。");
    }

    [RelayCommand]
    private void Redo()
    {
        if (!_history.Redo()) return;
        RecipeChanged("已重做一步。");
    }

    [RelayCommand]
    private async Task ImportRecipeAsync()
    {
        var path = await _recipeDialog.PickRecipeInputAsync(_lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        CancelAndDispose(ref _ioCancellation);
        _ioCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var token = _ioCancellation.Token;
        IsExporting = true;
        try
        {
            var recipe = await _importRecipe.ExecuteAsync(path, token).ConfigureAwait(true);
            _restoring = true;
            try
            {
                _history.Replace(recipe.Operations);
                Strength = recipe.Strength;
                _originalPaddedWidth = recipe.OriginalPaddedWidth;
                _originalPaddedHeight = recipe.OriginalPaddedHeight;
            }
            finally { _restoring = false; }
            RecipeChanged("配方导入成功；归一化频率会按当前 FFT 网格重放。");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { IsExporting = false; }
    }

    [RelayCommand]
    private async Task ExportRecipeAsync()
    {
        var path = await _recipeDialog.PickRecipeOutputAsync("frequency-mask-recipe.json", _lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        CancelAndDispose(ref _ioCancellation);
        _ioCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var token = _ioCancellation.Token;
        IsExporting = true;
        try
        {
            await _exportRecipe.ExecuteAsync(CurrentRecipe(), path, token).ConfigureAwait(true);
            StatusMessage = $"已原子导出配方：{path}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { IsExporting = false; }
    }

    [RelayCommand] private Task ExportReconstructionAsync() => ExportImageCoreAsync(FrequencyMaskExportArtifact.Reconstruction);
    [RelayCommand] private Task ExportMaskPreviewAsync() => ExportImageCoreAsync(FrequencyMaskExportArtifact.MaskPreview);

    private async Task ExportImageCoreAsync(FrequencyMaskExportArtifact artifact)
    {
        var session = _session;
        var result = CurrentResult();
        if (session is null || result is null) { StatusMessage = "没有与当前配方一致的可导出结果。"; return; }
        var tag = artifact == FrequencyMaskExportArtifact.Reconstruction ? "reconstruction" : "mask-preview";
        var path = await _imageDialog.PickOutputImageAsync($"{Path.GetFileNameWithoutExtension(SourcePath)}.{tag}.png", _lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        CancelAndDispose(ref _ioCancellation);
        _ioCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var token = _ioCancellation.Token;
        IsExporting = true;
        try
        {
            var saved = await _exportImage.ExecuteAsync(new(result, session.SessionFingerprint, CurrentRecipe().Fingerprint(), artifact, path), token).ConfigureAwait(true);
            StatusMessage = $"已原子导出{(saved.Artifact == FrequencyMaskExportArtifact.Reconstruction ? "重建" : "遮罩显示")} PNG：{saved.OutputPath}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { IsExporting = false; }
    }

    [RelayCommand]
    private async Task RenderFullAsync()
    {
        var session = _session;
        if (session is null) { StatusMessage = "请先建立 Session。"; return; }
        CancelAndDispose(ref _fullCancellation);
        _fullCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _fullCancellation;
        var token = current.Token;
        var generation = _generation;
        var recipe = CurrentRecipe();
        IsFullBusy = true;
        try
        {
            var result = await _renderFull.ExecuteAsync(session, recipe, token).ConfigureAwait(true);
            if (!CanCommit(generation) || !ReferenceEquals(session, _session) || result.RecipeFingerprint != recipe.Fingerprint()) return;
            _fullResult = result;
            StatusMessage = $"完整尺寸 {result.Reconstruction.Size.Width}×{result.Reconstruction.Size.Height} 已生成；重建导出将优先使用它。";
            NotifyState();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _fullCancellation)) IsFullBusy = false; }
    }

    [RelayCommand] private void Cancel() { _prepareCancellation?.Cancel(); _renderCancellation?.Cancel(); _fullCancellation?.Cancel(); _ioCancellation?.Cancel(); }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var recipeJson = Encoding.UTF8.GetString(_serializer.Serialize(CurrentRecipe()));
        var payload = JsonSerializer.SerializeToElement(new Snapshot(SourcePath, SelectedChannel, AnalysisMaximumEdge,
            SelectedTool, BrushRadius, TargetGain, Opacity, RingInnerRatio, IsBandLockEnabled, BandInnerRadius,
            BandOuterRadius, MaskOpacity, recipeJson));
        return ValueTask.FromResult(new DocumentSaveSnapshot(new DocumentRevision(_revision),
            new DocumentContent(SnapshotSchema, payload)));
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
        ++_generation;
        CancelAndDispose(ref _prepareCancellation);
        CancelResultOperations();
        ReplaceSession(null);
        ReplaceBitmap(ref _sourcePreview, null, nameof(SourcePreview));
        ReplaceBitmap(ref _spectrumPreview, null, nameof(SpectrumPreview));
        ReplaceBitmap(ref _maskPreview, null, nameof(MaskPreview));
        ReplaceBitmap(ref _resultPreview, null, nameof(ResultPreview));
        ReplaceBitmap(ref _differencePreview, null, nameof(DifferencePreview));
    }

    private async Task RenderCoreAsync(bool debounce)
    {
        var session = _session;
        if (session is null) return;
        var recipe = CurrentRecipe();
        CancelAndDispose(ref _renderCancellation);
        _renderCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _renderCancellation;
        var token = current.Token;
        var generation = ++_generation;
        IsBusy = true;
        try
        {
            if (debounce) await Task.Delay(150, token).ConfigureAwait(true);
            var result = await _render.ExecuteAsync(session, recipe, token).ConfigureAwait(true);
            var mask = await CreateBitmapAsync(result.MaskPreview, token).ConfigureAwait(true);
            var reconstruction = await CreateBitmapAsync(result.Reconstruction, token).ConfigureAwait(true);
            var difference = await CreateBitmapAsync(result.Difference.Signed, token).ConfigureAwait(true);
            if (!CanCommit(generation) || !ReferenceEquals(session, _session) || result.RecipeFingerprint != recipe.Fingerprint())
            { mask.Dispose(); reconstruction.Dispose(); difference.Dispose(); return; }
            _proxyResult = result;
            _fullResult = null;
            ReplaceBitmap(ref _maskPreview, mask, nameof(MaskPreview));
            ReplaceBitmap(ref _resultPreview, reconstruction, nameof(ResultPreview));
            ReplaceBitmap(ref _differencePreview, difference, nameof(DifferencePreview));
            var m = result.MaskStatistics;
            var raw = result.RawStatistics;
            DiagnosticsSummary = $"H={m.MinimumGain:0.####}..{m.MaximumGain:0.####}，均值 {m.MeanGain:0.####}；编辑 {m.NonAllPassBins:N0} bins ({m.NonAllPassRatio:P2})；共轭误差 {m.MaximumConjugateError:E2}；能量保留 {m.RetainedEnergyRatio:P2}；IFFT 虚部 {result.Raw.MaximumImaginaryResidual:E2}；raw {raw.Minimum:0.###}..{raw.Maximum:0.###}，越界 {raw.BelowZero:N0}/{raw.Above255:N0}；PSNR-Y {result.Quality.PsnrLumaDb:0.###} dB，SSIM-Y {result.Quality.GlobalSsimLuma:0.####}。";
            StatusMessage = $"代理重建完成，配方 {recipe.Fingerprint()}；操作 {_history.Count}/{FrequencyMaskRecipe.MaximumOperations}。";
            NotifyState();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { if (generation == _generation) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _renderCancellation)) IsBusy = false; }
    }

    private FrequencyMaskOperation CreateRing(NormalizedFrequencyPoint center, NormalizedFrequencyPoint edge,
        FrequencyBandLock? band)
    {
        var outer = Math.Min(1d, Math.Sqrt(Math.Pow(edge.X - center.X, 2d) + Math.Pow(edge.Y - center.Y, 2d)));
        if (outer <= 0d) throw new InvalidOperationException("圆环外半径必须大于零。");
        return FrequencyMaskOperation.Ring(center, outer * RingInnerRatio, outer, TargetGain, Opacity, band);
    }

    private void CommitOperation(FrequencyMaskOperation operation, string status)
    {
        _history.Add(operation);
        RecipeChanged(status);
    }

    private void RecipeChanged(string status)
    {
        ++_generation;
        CancelResultOperations();
        _proxyResult = null;
        _fullResult = null;
        StatusMessage = status;
        MarkChanged();
        NotifyState();
        if (_session is not null) _ = RenderCoreAsync(true);
    }

    private FrequencyBandLock? CurrentBandLock() => IsBandLockEnabled
        ? new FrequencyBandLock(BandInnerRadius, BandOuterRadius)
        : null;

    private FrequencyMaskRecipe CurrentRecipe() =>
        _history.CreateRecipe(Strength, _originalPaddedWidth, _originalPaddedHeight);

    private FrequencyMaskRenderResult? CurrentProxyResult()
    {
        var session = _session;
        if (session is null) return null;
        var fingerprint = CurrentRecipe().Fingerprint();
        return _proxyResult is { IsFullSize: false } result && result.SessionFingerprint == session.SessionFingerprint &&
            result.RecipeFingerprint == fingerprint ? result : null;
    }

    private FrequencyMaskRenderResult? CurrentResult()
    {
        var session = _session;
        if (session is null) return null;
        var fingerprint = CurrentRecipe().Fingerprint();
        if (_fullResult is { } full && full.SessionFingerprint == session.SessionFingerprint && full.RecipeFingerprint == fingerprint) return full;
        return CurrentProxyResult();
    }

    private ImageChannel ResolveChannel() => SelectedChannel switch
    {
        "R" => ImageChannel.Red,
        "G" => ImageChannel.Green,
        "B" => ImageChannel.Blue,
        "Cb" => ImageChannel.ChromaBlue,
        "Cr" => ImageChannel.ChromaRed,
        _ => ImageChannel.Luma
    };

    private async Task<Bitmap> CreateBitmapAsync(PixelImage image, CancellationToken token)
    {
        var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, false);
        return new Bitmap(stream);
    }

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema)
        {
            StatusMessage = $"不支持 snapshot schema {content.SchemaVersion}，已使用安全默认值。";
            return;
        }
        try
        {
            var value = content.Payload.Deserialize<Snapshot>();
            if (value is null) return;
            SourcePath = value.SourcePath ?? string.Empty;
            SelectedChannel = ChannelOptions.Contains(value.Channel) ? value.Channel : "Y";
            AnalysisMaximumEdge = AnalysisEdgeOptions.Contains(value.AnalysisEdge) ? value.AnalysisEdge : 1024;
            SelectedTool = ToolOptions.Contains(value.Tool) ? value.Tool : "衰减画笔";
            BrushRadius = value.BrushRadius is > 0d and <= 1d ? value.BrushRadius : 0.035d;
            TargetGain = value.TargetGain is >= 0d and <= 1d ? value.TargetGain : 0d;
            Opacity = value.Opacity is > 0d and <= 1d ? value.Opacity : 1d;
            RingInnerRatio = value.RingInnerRatio is >= 0d and < 1d ? value.RingInnerRatio : 0.5d;
            IsBandLockEnabled = value.BandLockEnabled;
            BandInnerRadius = value.BandInner is >= 0d and < 1d ? value.BandInner : 0d;
            BandOuterRadius = value.BandOuter > BandInnerRadius && value.BandOuter <= 1d ? value.BandOuter : 1d;
            MaskOpacity = value.MaskOpacity is >= 0d and <= 1d ? value.MaskOpacity : 0.55d;
            if (!string.IsNullOrWhiteSpace(value.RecipeJson))
            {
                var recipe = _serializer.Deserialize(Encoding.UTF8.GetBytes(value.RecipeJson));
                _history.Replace(recipe.Operations);
                Strength = recipe.Strength;
                _originalPaddedWidth = recipe.OriginalPaddedWidth;
                _originalPaddedHeight = recipe.OriginalPaddedHeight;
            }
            StatusMessage = File.Exists(SourcePath)
                ? "已恢复轻量参数与有界配方；请显式载入，不会自动解码或 FFT。"
                : "已恢复参数与配方，但源图片不存在，请重新选择。";
        }
        catch (Exception exception) { StatusMessage = $"快照无效，已保留安全默认值：{exception.Message}"; }
    }

    private void InvalidateSession(string status)
    {
        ++_generation;
        CancelResultOperations();
        ReplaceSession(null);
        _proxyResult = null;
        _fullResult = null;
        StatusMessage = status;
        NotifyState();
    }

    private void ReplaceSession(FrequencyMaskEditorSession? value)
    {
        var previous = _session;
        _session = value;
        previous?.Dispose();
    }

    private void ReplaceBitmap(ref Bitmap? field, Bitmap? value, string propertyName)
    {
        var previous = field;
        field = value;
        OnPropertyChanged(propertyName);
        previous?.Dispose();
    }

    private bool CanCommit(long generation) => generation == _generation && !_disposed && !_lifetime.IsClosing;
    private void CancelResultOperations()
    {
        CancelAndDispose(ref _renderCancellation);
        CancelAndDispose(ref _fullCancellation);
        CancelAndDispose(ref _ioCancellation);
    }
    private static void CancelAndDispose(ref CancellationTokenSource? source) { source?.Cancel(); source?.Dispose(); source = null; }
    private void MarkChanged()
    {
        if (_restoring) return;
        var wasDirty = IsDirty;
        _revision++;
        if (wasDirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }
    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasSession));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanRenderFull));
        OnPropertyChanged(nameof(CanExport));
    }

    partial void OnSourcePathChanged(string value) { if (!_restoring) { InvalidateSession("图片路径已改变，请显式重新载入。"); MarkChanged(); } }
    partial void OnSelectedChannelChanged(string value) { if (!_restoring) { InvalidateSession("通道已改变，请重新建立 FFT Session。"); MarkChanged(); } }
    partial void OnAnalysisMaximumEdgeChanged(int value) { if (!_restoring) { InvalidateSession("代理档位已改变，请重新建立 FFT Session。"); MarkChanged(); } }
    partial void OnSelectedToolChanged(string value) { if (!_restoring) MarkChanged(); }
    partial void OnBrushRadiusChanged(double value) { if (!_restoring) MarkChanged(); }
    partial void OnTargetGainChanged(double value) { if (!_restoring) MarkChanged(); }
    partial void OnOpacityChanged(double value) { if (!_restoring) MarkChanged(); }
    partial void OnRingInnerRatioChanged(double value) { if (!_restoring) MarkChanged(); }
    partial void OnIsBandLockEnabledChanged(bool value) { if (!_restoring) MarkChanged(); }
    partial void OnBandInnerRadiusChanged(double value) { if (!_restoring) MarkChanged(); }
    partial void OnBandOuterRadiusChanged(double value) { if (!_restoring) MarkChanged(); }
    partial void OnMaskOpacityChanged(double value) { if (!_restoring) MarkChanged(); }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsOperationBusy));
    partial void OnIsFullBusyChanged(bool value) { OnPropertyChanged(nameof(IsOperationBusy)); OnPropertyChanged(nameof(CanRenderFull)); }
    partial void OnIsExportingChanged(bool value) { OnPropertyChanged(nameof(IsOperationBusy)); OnPropertyChanged(nameof(CanExport)); }

    private sealed record Snapshot(string? SourcePath, string Channel, int AnalysisEdge, string Tool,
        double BrushRadius, double TargetGain, double Opacity, double RingInnerRatio, bool BandLockEnabled,
        double BandInner, double BandOuter, double MaskOpacity, string RecipeJson);
}
