using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.PeriodicNoiseRemoval;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.PeriodicNoiseRemoval;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.PeriodicNoiseRemoval;

internal sealed record PeriodicCandidateRow(int Number, PeriodicFrequency Frequency, string FrequencyText,
    string ScoreText, string RiskText, string SourceText, bool IsSelected, bool IsHighRisk);

/// <summary>“周期噪声与陷波器”多实例 Document：管理 Session、候选、草案/采用、取消和 Bitmap。</summary>
/// <remarks>
/// 数值扫描、共轭映射、Notch 公式、IFFT、诊断与 JSON 都委托给窄用例。本类只实现显式状态转换：检测不改配方，
/// 自动/手动选择只改草案，采用草案后结果仍过期，只有重新执行且与当前 Session/已采用配方指纹一致的结果可导出。
/// 每条异步路径都带 generation 和取消源，迟到、取消、异常或 Dispose 后的结果不能覆盖新状态。
/// </remarks>
internal sealed partial class PeriodicNoiseRemovalDocument : ObservableObject,
    IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private readonly IPreparePeriodicNoiseSessionUseCase _prepare;
    private readonly IDetectPeriodicNoiseCandidatesUseCase _detect;
    private readonly IMapPeriodicSpectrumSelectionUseCase _selectionMapper;
    private readonly IRenderPeriodicNoisePreviewUseCase _preview;
    private readonly IRenderFullPeriodicNoiseResultUseCase _renderFull;
    private readonly IImportPeriodicNoiseRecipeUseCase _importRecipe;
    private readonly IExportPeriodicNoiseRecipeUseCase _exportRecipe;
    private readonly IExportPeriodicNoiseCandidateSummaryUseCase _exportCandidates;
    private readonly IExportPeriodicNoiseArtifactUseCase _exportArtifact;
    private readonly IImageFileDialog _imageDialog;
    private readonly IPeriodicNoiseFileDialog _jsonDialog;
    private readonly IImageCodec _codec;
    private readonly IDocumentLifetime _lifetime;
    private DocumentPresentationState _presentation = new("周期噪声与陷波器");
    private PeriodicNoiseSession? _session;
    private PeriodicNoiseDetectionResult? _detection;
    private PeriodicNoiseRecipe? _draftRecipe;
    private PeriodicNoiseRecipe? _acceptedRecipe;
    private PeriodicNoiseRenderResult? _draftResult;
    private PeriodicNoiseRenderResult? _acceptedResult;
    private CancellationTokenSource? _workCancellation;
    private long _generation;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;
    private bool _disposed;

    public PeriodicNoiseRemovalDocument(IPreparePeriodicNoiseSessionUseCase prepare,
        IDetectPeriodicNoiseCandidatesUseCase detect, IMapPeriodicSpectrumSelectionUseCase selectionMapper,
        IRenderPeriodicNoisePreviewUseCase preview, IRenderFullPeriodicNoiseResultUseCase renderFull,
        IImportPeriodicNoiseRecipeUseCase importRecipe, IExportPeriodicNoiseRecipeUseCase exportRecipe,
        IExportPeriodicNoiseCandidateSummaryUseCase exportCandidates,
        IExportPeriodicNoiseArtifactUseCase exportArtifact, IImageFileDialog imageDialog,
        IPeriodicNoiseFileDialog jsonDialog, IImageCodec codec, IDocumentLifetime lifetime)
    {
        _prepare = prepare;
        _detect = detect;
        _selectionMapper = selectionMapper;
        _preview = preview;
        _renderFull = renderFull;
        _importRecipe = importRecipe;
        _exportRecipe = exportRecipe;
        _exportCandidates = exportCandidates;
        _exportArtifact = exportArtifact;
        _imageDialog = imageDialog;
        _jsonDialog = jsonDialog;
        _codec = codec;
        _lifetime = lifetime;
    }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _selectedChannel = "Y";
    [ObservableProperty] private int _analysisMaximumEdge = 1024;
    [ObservableProperty] private double _dcExclusionRadius = 0.025d;
    [ObservableProperty] private double _robustScoreThreshold = 6d;
    [ObservableProperty] private double _prominenceThreshold = 0.2d;
    [ObservableProperty] private double _suppressionRadius = 0.0125d;
    [ObservableProperty] private string _selectedTransition = "Gaussian";
    [ObservableProperty] private double _notchRadius = 0.01d;
    [ObservableProperty] private double _notchStrength = 0.9d;
    [ObservableProperty] private int _butterworthOrder = 2;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "请选择 PNG/JPEG 图片并建立分析 Session。";
    [ObservableProperty] private string _sessionSummary = "尚未载入";
    [ObservableProperty] private string _candidateSummary = "尚未运行候选检测。";
    [ObservableProperty] private string _recipeStateSummary = "尚无草案或已采用配方。";
    [ObservableProperty] private string _diagnosticsSummary = "尚未生成结果；陷波会永久丢弃被抑制频率中的真实纹理。";
    [ObservableProperty] private IReadOnlyList<PeriodicCandidateRow> _candidateRows = Array.Empty<PeriodicCandidateRow>();
    [ObservableProperty] private PeriodicCandidateRow? _selectedCandidate;
    [ObservableProperty] private IReadOnlyList<PeriodicSpectrumMarker> _spectrumMarkers = Array.Empty<PeriodicSpectrumMarker>();
    private Bitmap? _sourcePreview;
    private Bitmap? _originalSpectrumPreview;
    private Bitmap? _filteredSpectrumPreview;
    private Bitmap? _maskPreview;
    private Bitmap? _resultPreview;
    private Bitmap? _signedDifferencePreview;
    private Bitmap? _absoluteDifferencePreview;

    public IReadOnlyList<string> ChannelOptions { get; } = ["R", "G", "B", "Y", "Cb", "Cr"];
    public IReadOnlyList<int> AnalysisEdgeOptions { get; } = [512, 1024, 2048];
    public IReadOnlyList<string> TransitionOptions { get; } = ["Ideal", "Butterworth", "Gaussian"];
    public Bitmap? SourcePreview => _sourcePreview;
    public Bitmap? OriginalSpectrumPreview => _originalSpectrumPreview;
    public Bitmap? FilteredSpectrumPreview => _filteredSpectrumPreview;
    public Bitmap? MaskPreview => _maskPreview;
    public Bitmap? ResultPreview => _resultPreview;
    public Bitmap? SignedDifferencePreview => _signedDifferencePreview;
    public Bitmap? AbsoluteDifferencePreview => _absoluteDifferencePreview;
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasSession => _session is not null;
    public bool HasDetection => _detection is not null;
    public bool HasDraft => _draftRecipe is not null;
    public bool HasAcceptedRecipe => _acceptedRecipe is not null;
    public bool IsButterworth => SelectedTransition == "Butterworth";
    public bool CanRenderFull => _draftRecipe is null && _session?.CanRenderFullSize == true &&
        _acceptedRecipe is not null && !IsBusy;
    public bool CanExport => CurrentAcceptedResult() is not null && !IsBusy;

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
                ? "周期噪声与陷波器" : activation.Title);
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
        var operation = BeginOperation(incrementGeneration: true);
        try
        {
            StatusMessage = "正在解码一次、建立有界分析代理并缓存只读 FFT…";
            var session = await _prepare.ExecuteAsync(new PeriodicNoiseSessionRequest(SourcePath,
                ResolveChannel(), AnalysisMaximumEdge), operation.Token).ConfigureAwait(true);
            var source = await CreateBitmapAsync(session.AnalysisProxy, operation.Token).ConfigureAwait(true);
            var spectrum = await CreateBitmapAsync(session.MagnitudePreview, operation.Token).ConfigureAwait(true);
            if (!CanCommit(operation.Generation))
            {
                session.Dispose();
                source.Dispose();
                spectrum.Dispose();
                return;
            }
            ReplaceSession(session);
            ReplaceBitmap(ref _sourcePreview, source, nameof(SourcePreview));
            ReplaceBitmap(ref _originalSpectrumPreview, spectrum, nameof(OriginalSpectrumPreview));
            ResetSessionOutputsPreservingRecipes();
            SessionSummary = $"原图 {session.SourceImage.Size.Width}×{session.SourceImage.Size.Height}；代理 {session.AnalysisProxy.Size.Width}×{session.AnalysisProxy.Size.Height}；FFT {session.Spectrum.PaddedWidth}×{session.Spectrum.PaddedHeight}；Session {Short(session.SessionFingerprint)}";
            StatusMessage = session.CanRenderFullSize ? "Session 已就绪，可运行候选检测。" :
                "Session 已就绪；原图超出完整尺寸预算，只能生成明确标识的代理结果。";
            NotifyState();
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
        {
            if (!_lifetime.IsClosing) StatusMessage = "准备已取消，未提交半成品。";
        }
        catch (Exception exception) { if (CanCommit(operation.Generation)) StatusMessage = exception.Message; }
        finally { CompleteOperation(operation.Source); }
    }

    [RelayCommand]
    private async Task DetectAsync()
    {
        var session = _session;
        if (session is null) { StatusMessage = "请先建立分析 Session。"; return; }
        if (!TrySettings(out var settings, out var error)) { StatusMessage = error!; return; }
        var operation = BeginOperation(incrementGeneration: true);
        try
        {
            StatusMessage = "正在计算对数功率、径向稳健背景、局部峰与风险事实…";
            var detection = await _detect.ExecuteAsync(session, settings, operation.Token).ConfigureAwait(true);
            if (!CanCommit(operation.Generation) || !ReferenceEquals(session, _session)) return;
            _detection = detection;
            CandidateSummary = $"候选 {detection.Candidates.Count} 对；保守建议 {detection.Suggestions.Count} 对；候选并不等于已确认噪声。";
            RebuildRowsAndMarkers();
            StatusMessage = "候选检测完成；请复核真实纹理风险，再生成建议草案或手动选择。";
            NotifyState();
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { }
        catch (Exception exception) { if (CanCommit(operation.Generation)) StatusMessage = exception.Message; }
        finally { CompleteOperation(operation.Source); }
    }

    [RelayCommand]
    private void GenerateSuggestions()
    {
        if (_session is null || _detection is null) { StatusMessage = "请先运行候选检测。"; return; }
        if (_detection.Suggestions.Count == 0)
        {
            StatusMessage = "没有满足保守规则的默认建议；仍可人工选择候选或频点。";
            return;
        }
        SetDraft(CreateRecipe(_detection.Suggestions));
        StatusMessage = "已生成未确认陷波草案；预览和人工采用之前不会替换已采用配方。";
    }

    [RelayCommand]
    private void ToggleCandidate()
    {
        var row = SelectedCandidate;
        if (row is null || _session is null) return;
        ToggleFrequency(row.Frequency, PeriodicNotchOrigin.Manual);
    }

    public void ToggleSpectrumPoint(double normalizedX, double normalizedY)
    {
        var session = _session;
        if (session is null) { StatusMessage = "请先建立 Session。"; return; }
        try
        {
            var frequency = _selectionMapper.Execute(session, normalizedX, normalizedY);
            if (frequency.Radius < DcExclusionRadius)
            {
                StatusMessage = "DC 排除半径内的频点不能加入草案。";
                return;
            }
            ToggleFrequency(frequency, PeriodicNotchOrigin.Manual);
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        var session = _session;
        if (session is null) { StatusMessage = "请先建立 Session。"; return; }
        var recipe = _draftRecipe ?? _acceptedRecipe;
        if (recipe is null) { StatusMessage = "请先生成或手动建立陷波草案。"; return; }
        if (recipe.Channel != session.Channel) { StatusMessage = "配方通道与 Session 不一致，请重新载入。"; return; }
        var isDraft = ReferenceEquals(recipe, _draftRecipe);
        var operation = BeginOperation(incrementGeneration: true);
        try
        {
            StatusMessage = isDraft ? "正在预览未确认草案…" : "正在重建已采用配方的当前代理结果…";
            var selected = SelectedCandidates(recipe);
            var result = await _preview.ExecuteAsync(session, recipe, selected, isDraft, operation.Token)
                .ConfigureAwait(true);
            var filtered = await CreateBitmapAsync(result.FilteredSpectrumPreview, operation.Token).ConfigureAwait(true);
            var mask = await CreateBitmapAsync(result.Mask.Preview, operation.Token).ConfigureAwait(true);
            var image = await CreateBitmapAsync(result.Reconstruction, operation.Token).ConfigureAwait(true);
            var signed = await CreateBitmapAsync(result.Difference.Signed, operation.Token).ConfigureAwait(true);
            var absolute = await CreateBitmapAsync(result.Difference.Absolute, operation.Token).ConfigureAwait(true);
            if (!CanCommit(operation.Generation) || !ReferenceEquals(session, _session) ||
                result.RecipeFingerprint != recipe.Fingerprint())
            {
                filtered.Dispose(); mask.Dispose(); image.Dispose(); signed.Dispose(); absolute.Dispose(); return;
            }
            if (isDraft) _draftResult = result; else _acceptedResult = result;
            ReplaceBitmap(ref _filteredSpectrumPreview, filtered, nameof(FilteredSpectrumPreview));
            ReplaceBitmap(ref _maskPreview, mask, nameof(MaskPreview));
            ReplaceBitmap(ref _resultPreview, image, nameof(ResultPreview));
            ReplaceBitmap(ref _signedDifferencePreview, signed, nameof(SignedDifferencePreview));
            ReplaceBitmap(ref _absoluteDifferencePreview, absolute, nameof(AbsoluteDifferencePreview));
            UpdateDiagnostics(result);
            StatusMessage = isDraft ? "未确认草案预览已完成；导出仍被禁止。" : "已采用配方的代理结果已重建，可导出。";
            NotifyState();
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { }
        catch (Exception exception) { if (CanCommit(operation.Generation)) StatusMessage = exception.Message; }
        finally { CompleteOperation(operation.Source); }
    }

    [RelayCommand]
    private void AcceptDraft()
    {
        if (_draftRecipe is null) { StatusMessage = "当前没有可采用的草案。"; return; }
        ++_generation;
        CancelWork();
        _acceptedRecipe = _draftRecipe;
        _draftRecipe = null;
        _draftResult = null;
        _acceptedResult = null;
        ClearResultBitmaps();
        RecipeStateSummary = $"已采用配方 {Short(_acceptedRecipe.Fingerprint())}；结果已过期，请显式重新预览或执行原尺寸。";
        StatusMessage = "草案已采用，但不会沿用草案结果；请显式重建后再导出。";
        RebuildRowsAndMarkers();
        MarkChanged();
        NotifyState();
    }

    [RelayCommand]
    private void DiscardDraft()
    {
        if (_draftRecipe is null) { StatusMessage = "当前没有可放弃的草案。"; return; }
        ++_generation;
        CancelWork();
        _draftRecipe = null;
        _draftResult = null;
        ClearResultBitmaps();
        RecipeStateSummary = _acceptedRecipe is null ? "已放弃草案；当前没有已采用配方。" :
            $"已放弃草案；已采用配方仍为 {Short(_acceptedRecipe.Fingerprint())}。";
        StatusMessage = "未确认草案已放弃；如需恢复画面可重新执行已采用配方。";
        RebuildRowsAndMarkers();
        MarkChanged();
        NotifyState();
    }

    [RelayCommand]
    private async Task RenderFullAsync()
    {
        var session = _session;
        var recipe = _acceptedRecipe;
        if (session is null || recipe is null) { StatusMessage = "请先采用草案。"; return; }
        var operation = BeginOperation(incrementGeneration: true);
        try
        {
            var result = await _renderFull.ExecuteAsync(session, recipe, SelectedCandidates(recipe), operation.Token)
                .ConfigureAwait(true);
            if (!CanCommit(operation.Generation) || !ReferenceEquals(session, _session) ||
                result.RecipeFingerprint != recipe.Fingerprint()) return;
            _acceptedResult = result;
            UpdateDiagnostics(result);
            StatusMessage = $"预算内原尺寸 {result.Reconstruction.Size.Width}×{result.Reconstruction.Size.Height} 已完成，可导出。";
            NotifyState();
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { }
        catch (Exception exception) { if (CanCommit(operation.Generation)) StatusMessage = exception.Message; }
        finally { CompleteOperation(operation.Source); }
    }

    [RelayCommand] private Task ExportResultAsync() => ExportArtifactAsync(PeriodicNoiseExportArtifact.Reconstruction);
    [RelayCommand] private Task ExportMaskAsync() => ExportArtifactAsync(PeriodicNoiseExportArtifact.MaskPreview);

    private async Task ExportArtifactAsync(PeriodicNoiseExportArtifact artifact)
    {
        var session = _session;
        var recipe = _acceptedRecipe;
        var result = CurrentAcceptedResult();
        if (session is null || recipe is null || result is null) { StatusMessage = "没有当前已采用且指纹一致的结果。"; return; }
        var tag = artifact == PeriodicNoiseExportArtifact.Reconstruction ? "periodic-filtered" : "periodic-mask";
        var name = $"{Path.GetFileNameWithoutExtension(SourcePath)}.{tag}.{(result.IsFullSize ? "full" : "proxy")}.png";
        var path = await _imageDialog.PickOutputImageAsync(name, _lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        var operation = BeginOperation(incrementGeneration: false);
        try
        {
            var saved = await _exportArtifact.ExecuteAsync(new PeriodicNoiseArtifactExportRequest(result,
                session.SessionFingerprint, recipe.Fingerprint(), artifact, path), operation.Token).ConfigureAwait(true);
            StatusMessage = $"已原子导出 {(saved.IsFullSize ? "原尺寸" : "代理")} {saved.Artifact} PNG：{saved.OutputPath}";
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { CompleteOperation(operation.Source); }
    }

    [RelayCommand]
    private async Task ImportRecipeAsync()
    {
        var path = await _jsonDialog.PickRecipeInputAsync(_lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        var operation = BeginOperation(incrementGeneration: true);
        try
        {
            var recipe = await _importRecipe.ExecuteAsync(path, operation.Token).ConfigureAwait(true);
            if (!CanCommit(operation.Generation)) return;
            SelectedChannel = ChannelName(recipe.Channel);
            SelectedTransition = recipe.Transition.ToString();
            NotchRadius = recipe.Radius;
            NotchStrength = recipe.Strength;
            ButterworthOrder = recipe.ButterworthOrder;
            SetDraft(recipe);
            StatusMessage = "配方已作为未确认草案导入；不会自动读取图片、执行 FFT 或替换已采用结果。";
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { }
        catch (Exception exception) { if (CanCommit(operation.Generation)) StatusMessage = exception.Message; }
        finally { CompleteOperation(operation.Source); }
    }

    [RelayCommand]
    private async Task ExportRecipeAsync()
    {
        var recipe = _draftRecipe ?? _acceptedRecipe;
        if (recipe is null) { StatusMessage = "没有可导出的配方。"; return; }
        var path = await _jsonDialog.PickRecipeOutputAsync("periodic-notch-recipe.json", _lifetime.ClosingToken)
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        var operation = BeginOperation(incrementGeneration: false);
        try
        {
            await _exportRecipe.ExecuteAsync(recipe, path, operation.Token).ConfigureAwait(true);
            StatusMessage = $"已原子导出配方：{path}";
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { CompleteOperation(operation.Source); }
    }

    [RelayCommand]
    private async Task ExportCandidatesAsync()
    {
        var session = _session;
        var detection = _detection;
        if (session is null || detection is null) { StatusMessage = "没有可导出的候选摘要。"; return; }
        var path = await _jsonDialog.PickCandidateSummaryOutputAsync("periodic-candidates.json",
            _lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        var operation = BeginOperation(incrementGeneration: false);
        try
        {
            await _exportCandidates.ExecuteAsync(session, detection, path, operation.Token).ConfigureAwait(true);
            StatusMessage = $"已原子导出候选摘要：{path}";
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { CompleteOperation(operation.Source); }
    }

    [RelayCommand] private void Cancel() => CancelWork();

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = new Snapshot(SourcePath, SelectedChannel, AnalysisMaximumEdge, DcExclusionRadius,
            RobustScoreThreshold, ProminenceThreshold, SuppressionRadius, SelectedTransition, NotchRadius,
            NotchStrength, ButterworthOrder, ToSnapshot(_acceptedRecipe), ToSnapshot(_draftRecipe));
        return ValueTask.FromResult(new DocumentSaveSnapshot(new DocumentRevision(_revision),
            new DocumentContent(SnapshotSchema, JsonSerializer.SerializeToElement(snapshot))));
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
        CancelWork();
        ReplaceSession(null);
        ReplaceBitmap(ref _sourcePreview, null, nameof(SourcePreview));
        ReplaceBitmap(ref _originalSpectrumPreview, null, nameof(OriginalSpectrumPreview));
        ClearResultBitmaps();
    }

    partial void OnSourcePathChanged(string value) { if (!_restoring) { InvalidateSession("图片路径已改变，请显式重新载入。"); MarkChanged(); } }
    partial void OnSelectedChannelChanged(string value) { if (!_restoring) { InvalidateSession("通道已改变，请重新建立 FFT Session。"); MarkChanged(); } }
    partial void OnAnalysisMaximumEdgeChanged(int value) { if (!_restoring) { InvalidateSession("代理档位已改变，请重新建立 FFT Session。"); MarkChanged(); } }
    partial void OnDcExclusionRadiusChanged(double value) => DetectionParameterChanged();
    partial void OnRobustScoreThresholdChanged(double value) => DetectionParameterChanged();
    partial void OnProminenceThresholdChanged(double value) => DetectionParameterChanged();
    partial void OnSuppressionRadiusChanged(double value) => DetectionParameterChanged();
    partial void OnSelectedTransitionChanged(string value) { OnPropertyChanged(nameof(IsButterworth)); RecipeParameterChanged(); }
    partial void OnNotchRadiusChanged(double value) => RecipeParameterChanged();
    partial void OnNotchStrengthChanged(double value) => RecipeParameterChanged();
    partial void OnButterworthOrderChanged(int value) => RecipeParameterChanged();
    partial void OnIsBusyChanged(bool value) { NotifyState(); }

    private void DetectionParameterChanged()
    {
        if (_restoring) return;
        ++_generation;
        CancelWork();
        _detection = null;
        if (_draftRecipe?.Notches.Any(item => item.Origin == PeriodicNotchOrigin.Automatic) == true)
        {
            var manual = _draftRecipe.Notches.Where(item => item.Origin == PeriodicNotchOrigin.Manual).ToArray();
            _draftRecipe = manual.Length == 0 ? null : CreateRecipe(manual);
            _draftResult = null;
            ClearResultBitmaps();
        }
        CandidateRows = Array.Empty<PeriodicCandidateRow>();
        SpectrumMarkers = MarkersForSelectedRecipe();
        CandidateSummary = "检测设置已改变，旧候选已过期；FFT 仍可复用。";
        MarkChanged();
        NotifyState();
    }

    private void RecipeParameterChanged()
    {
        if (_restoring) return;
        if (_draftRecipe is not null || _acceptedRecipe is not null)
        {
            var notches = (_draftRecipe ?? _acceptedRecipe)!.Notches;
            try { SetDraft(CreateRecipe(notches)); }
            catch (Exception exception) { StatusMessage = exception.Message; }
        }
        MarkChanged();
    }

    private void ToggleFrequency(PeriodicFrequency frequency, PeriodicNotchOrigin origin)
    {
        var seed = _draftRecipe ?? _acceptedRecipe;
        var notches = seed?.Notches.ToList() ?? [];
        var canonical = PeriodicFrequency.Canonical(frequency);
        var index = notches.FindIndex(item => item.CanonicalFrequency == canonical);
        if (index >= 0) notches.RemoveAt(index);
        else
        {
            if (notches.Count >= 32) { StatusMessage = "草案最多允许 32 对陷波中心。"; return; }
            notches.Add(new PeriodicNotch(canonical, origin));
        }
        SetDraft(CreateRecipe(notches));
        StatusMessage = index >= 0 ? "已从未确认草案移除整对共轭中心。" : "已向未确认草案加入整对共轭中心。";
    }

    private void SetDraft(PeriodicNoiseRecipe recipe)
    {
        _draftRecipe = recipe;
        _draftResult = null;
        ClearResultBitmaps();
        RecipeStateSummary = $"未确认草案 {Short(recipe.Fingerprint())}；{recipe.EnabledNotchCount} 对中心；已采用配方 {(_acceptedRecipe is null ? "无" : Short(_acceptedRecipe.Fingerprint()))}。";
        RebuildRowsAndMarkers();
        MarkChanged();
        NotifyState();
    }

    private PeriodicNoiseRecipe CreateRecipe(IEnumerable<PeriodicNotch> notches) => new(ResolveChannel(),
        Enum.Parse<PeriodicNotchTransition>(SelectedTransition), NotchRadius, NotchStrength,
        ButterworthOrder, notches);

    private bool TrySettings(out PeriodicNoiseDetectionSettings settings, out string? error)
    {
        try
        {
            settings = new PeriodicNoiseDetectionSettings(DcExclusionRadius, RobustScoreThreshold,
                ProminenceThreshold, SuppressionRadius);
            error = null;
            return true;
        }
        catch (Exception exception) { settings = null!; error = exception.Message; return false; }
    }

    private IReadOnlyList<PeriodicFrequencyCandidate> SelectedCandidates(PeriodicNoiseRecipe recipe)
    {
        if (_detection is null) return Array.Empty<PeriodicFrequencyCandidate>();
        var selected = recipe.Notches.Where(item => item.Enabled).Select(item => item.CanonicalFrequency).ToHashSet();
        return _detection.Candidates.Where(item => selected.Contains(item.CanonicalFrequency)).ToArray();
    }

    private void RebuildRowsAndMarkers()
    {
        var recipe = _draftRecipe ?? _acceptedRecipe;
        var selected = recipe?.Notches.Where(item => item.Enabled).Select(item => item.CanonicalFrequency).ToHashSet()
            ?? [];
        if (_detection is null)
        {
            CandidateRows = Array.Empty<PeriodicCandidateRow>();
            SpectrumMarkers = MarkersForSelectedRecipe();
            return;
        }
        CandidateRows = _detection.Candidates.Select((item, index) => new PeriodicCandidateRow(index + 1,
            item.CanonicalFrequency,
            $"fx {item.CanonicalFrequency.Fx:0.####}, fy {item.CanonicalFrequency.Fy:0.####}；周期≈{item.CanonicalFrequency.PeriodPixels:0.##} px",
            $"score {item.RobustScore:0.##}；突出度 {item.Prominence:0.###}",
            item.RiskReasons == PeriodicPeakRiskReason.None ? "低风险事实" : item.RiskReasons.ToString(),
            _detection.Suggestions.Any(s => s.CanonicalFrequency == item.CanonicalFrequency) ? "自动建议" : "候选",
            selected.Contains(item.CanonicalFrequency), item.RiskLevel == PeriodicPeakRiskLevel.High)).ToArray();
        SpectrumMarkers = _detection.Candidates.Select((item, index) => ToMarker(index + 1,
            item.CanonicalFrequency, selected.Contains(item.CanonicalFrequency),
            item.RiskLevel == PeriodicPeakRiskLevel.High)).Concat(ManualMarkers(recipe, _detection)).ToArray();
    }

    private IReadOnlyList<PeriodicSpectrumMarker> MarkersForSelectedRecipe()
    {
        var recipe = _draftRecipe ?? _acceptedRecipe;
        if (recipe is null) return Array.Empty<PeriodicSpectrumMarker>();
        return recipe.Notches.Select((item, index) => ToMarker(index + 1, item.CanonicalFrequency,
            item.Enabled, highRisk: false)).ToArray();
    }

    private static IEnumerable<PeriodicSpectrumMarker> ManualMarkers(PeriodicNoiseRecipe? recipe,
        PeriodicNoiseDetectionResult detection)
    {
        if (recipe is null) yield break;
        var known = detection.Candidates.Select(item => item.CanonicalFrequency).ToHashSet();
        var number = detection.Candidates.Count + 1;
        foreach (var notch in recipe.Notches.Where(item => !known.Contains(item.CanonicalFrequency)))
            yield return ToMarker(number++, notch.CanonicalFrequency, notch.Enabled, highRisk: false);
    }

    private static PeriodicSpectrumMarker ToMarker(int number, PeriodicFrequency frequency, bool selected,
        bool highRisk)
    {
        var conjugate = frequency.Conjugate();
        return new PeriodicSpectrumMarker(number, frequency.Fx + 0.5d, frequency.Fy + 0.5d,
            conjugate.Fx + 0.5d, conjugate.Fy + 0.5d, selected, highRisk);
    }

    private void UpdateDiagnostics(PeriodicNoiseRenderResult result)
    {
        var d = result.Diagnostics;
        DiagnosticsSummary = $"{(result.IsDraft ? "未确认草案" : "已采用")} / {(result.IsFullSize ? "原尺寸" : "代理")}；修改 {d.Mask.ModifiedBinCount:N0} bins ({d.Mask.ModifiedBinRatio:P3})；频谱能量移除 {d.RemovedSpectrumEnergyRatio:P3}；raw {d.RawMinimum:0.###}..{d.RawMaximum:0.###}，越界 {d.RawBelowZero:N0}/{d.RawAbove255:N0}；MAE {d.MeanAbsoluteChannelDifference:0.###}，max {d.MaximumAbsoluteChannelDifference:0.###}；PSNR-Y {d.Quality.PsnrLumaDb:0.###} dB，SSIM-Y {d.Quality.GlobalSsimLuma:0.####}；虚部 {d.MaximumImaginaryResidual:E2}。陷波会永久丢弃被抑制频率中的真实纹理，导出图无法从自身恢复。";
    }

    private PeriodicNoiseRenderResult? CurrentAcceptedResult()
    {
        if (_draftRecipe is not null || _session is null || _acceptedRecipe is null || _acceptedResult is null ||
            _acceptedResult.IsDraft) return null;
        return _acceptedResult.SessionFingerprint == _session.SessionFingerprint &&
            _acceptedResult.RecipeFingerprint == _acceptedRecipe.Fingerprint() ? _acceptedResult : null;
    }

    private void ResetAnalysisState()
    {
        _detection = null;
        _draftRecipe = null;
        _acceptedRecipe = null;
        _draftResult = null;
        _acceptedResult = null;
        CandidateRows = Array.Empty<PeriodicCandidateRow>();
        SpectrumMarkers = Array.Empty<PeriodicSpectrumMarker>();
        CandidateSummary = "尚未运行候选检测。";
        RecipeStateSummary = "尚无草案或已采用配方。";
        DiagnosticsSummary = "尚未生成结果；陷波会永久丢弃被抑制频率中的真实纹理。";
        ClearResultBitmaps();
    }

    private void ResetSessionOutputsPreservingRecipes()
    {
        _detection = null;
        _draftResult = null;
        _acceptedResult = null;
        CandidateRows = Array.Empty<PeriodicCandidateRow>();
        SpectrumMarkers = MarkersForSelectedRecipe();
        CandidateSummary = "尚未运行候选检测。";
        RecipeStateSummary = _draftRecipe is not null ?
            $"已载入未确认草案 {Short(_draftRecipe.Fingerprint())}；请显式预览。" :
            _acceptedRecipe is not null ?
                $"已载入已采用配方 {Short(_acceptedRecipe.Fingerprint())}；结果未持久化，请显式重建。" :
                "尚无草案或已采用配方。";
        DiagnosticsSummary = "尚未生成结果；陷波会永久丢弃被抑制频率中的真实纹理。";
        ClearResultBitmaps();
    }

    private void InvalidateSession(string status)
    {
        ++_generation;
        CancelWork();
        ReplaceSession(null);
        ResetAnalysisState();
        ReplaceBitmap(ref _sourcePreview, null, nameof(SourcePreview));
        ReplaceBitmap(ref _originalSpectrumPreview, null, nameof(OriginalSpectrumPreview));
        SessionSummary = "尚未载入";
        StatusMessage = status;
        NotifyState();
    }

    private (CancellationTokenSource Source, CancellationToken Token, long Generation) BeginOperation(bool incrementGeneration)
    {
        CancelWork();
        _workCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        if (incrementGeneration) ++_generation;
        IsBusy = true;
        return (_workCancellation, _workCancellation.Token, _generation);
    }

    private void CompleteOperation(CancellationTokenSource source)
    {
        if (!ReferenceEquals(source, _workCancellation)) return;
        source.Dispose();
        _workCancellation = null;
        IsBusy = false;
    }

    private void CancelWork()
    {
        _workCancellation?.Cancel();
        _workCancellation?.Dispose();
        _workCancellation = null;
        IsBusy = false;
    }

    private bool CanCommit(long generation) => generation == _generation && !_disposed && !_lifetime.IsClosing;

    private async Task<Bitmap> CreateBitmapAsync(PixelImage image, CancellationToken cancellationToken)
    {
        var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, cancellationToken)
            .ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, writable: false);
        return new Bitmap(stream);
    }

    private void ReplaceSession(PeriodicNoiseSession? value)
    {
        var previous = _session;
        _session = value;
        previous?.Dispose();
    }

    private void ClearResultBitmaps()
    {
        ReplaceBitmap(ref _filteredSpectrumPreview, null, nameof(FilteredSpectrumPreview));
        ReplaceBitmap(ref _maskPreview, null, nameof(MaskPreview));
        ReplaceBitmap(ref _resultPreview, null, nameof(ResultPreview));
        ReplaceBitmap(ref _signedDifferencePreview, null, nameof(SignedDifferencePreview));
        ReplaceBitmap(ref _absoluteDifferencePreview, null, nameof(AbsoluteDifferencePreview));
    }

    private void ReplaceBitmap(ref Bitmap? field, Bitmap? value, string propertyName)
    {
        var previous = field;
        if (SetProperty(ref field, value, propertyName)) previous?.Dispose();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasSession));
        OnPropertyChanged(nameof(HasDetection));
        OnPropertyChanged(nameof(HasDraft));
        OnPropertyChanged(nameof(HasAcceptedRecipe));
        OnPropertyChanged(nameof(CanRenderFull));
        OnPropertyChanged(nameof(CanExport));
    }

    private void MarkChanged()
    {
        if (_restoring) return;
        var wasDirty = IsDirty;
        _revision++;
        if (wasDirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
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

    private static string ChannelName(ImageChannel channel) => channel switch
    {
        ImageChannel.Red => "R",
        ImageChannel.Green => "G",
        ImageChannel.Blue => "B",
        ImageChannel.ChromaBlue => "Cb",
        ImageChannel.ChromaRed => "Cr",
        _ => "Y"
    };

    private static string Short(string value) => value.Length <= 8 ? value : value[..8];

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema)
        {
            StatusMessage = $"不支持快照 schema {content.SchemaVersion}，已使用安全默认值。";
            return;
        }
        try
        {
            var value = content.Payload.Deserialize<Snapshot>();
            if (value is null) return;
            SourcePath = value.SourcePath ?? string.Empty;
            SelectedChannel = ChannelOptions.Contains(value.Channel) ? value.Channel : "Y";
            AnalysisMaximumEdge = AnalysisEdgeOptions.Contains(value.AnalysisEdge) ? value.AnalysisEdge : 1024;
            DcExclusionRadius = value.DcRadius;
            RobustScoreThreshold = value.ScoreThreshold;
            ProminenceThreshold = value.ProminenceThreshold;
            SuppressionRadius = value.SuppressionRadius;
            SelectedTransition = TransitionOptions.Contains(value.Transition) ? value.Transition : "Gaussian";
            NotchRadius = value.NotchRadius;
            NotchStrength = value.Strength;
            ButterworthOrder = value.Order;
            _acceptedRecipe = FromSnapshot(value.Accepted);
            _draftRecipe = FromSnapshot(value.Draft);
            RecipeStateSummary = _draftRecipe is not null ? $"已恢复未确认草案 {Short(_draftRecipe.Fingerprint())}；请显式载入图片。" :
                _acceptedRecipe is not null ? $"已恢复已采用配方 {Short(_acceptedRecipe.Fingerprint())}；结果未持久化，请显式载入并重建。" :
                "已恢复轻量参数；没有配方。";
            StatusMessage = "快照只恢复轻量参数和配方；不会自动读取图片、检测或执行 IFFT。";
        }
        catch (Exception exception) { StatusMessage = $"快照无效，已保留安全默认值：{exception.Message}"; }
    }

    private static SnapshotRecipe? ToSnapshot(PeriodicNoiseRecipe? recipe) => recipe is null ? null : new(
        ChannelName(recipe.Channel), recipe.Transition.ToString(), recipe.Radius, recipe.Strength,
        recipe.ButterworthOrder, recipe.Notches.Select(item => new SnapshotNotch(item.CanonicalFrequency.Fx,
            item.CanonicalFrequency.Fy, item.Origin.ToString(), item.Enabled)).ToArray());

    private static PeriodicNoiseRecipe? FromSnapshot(SnapshotRecipe? value)
    {
        if (value is null) return null;
        var channel = value.Channel switch
        {
            "R" => ImageChannel.Red,
            "G" => ImageChannel.Green,
            "B" => ImageChannel.Blue,
            "Cb" => ImageChannel.ChromaBlue,
            "Cr" => ImageChannel.ChromaRed,
            _ => ImageChannel.Luma
        };
        return new PeriodicNoiseRecipe(channel, Enum.Parse<PeriodicNotchTransition>(value.Transition),
            value.Radius, value.Strength, value.Order, value.Notches.Select(item => new PeriodicNotch(
                new PeriodicFrequency(item.Fx, item.Fy), Enum.Parse<PeriodicNotchOrigin>(item.Origin), item.Enabled)));
    }

    private sealed record Snapshot(string? SourcePath, string Channel, int AnalysisEdge, double DcRadius,
        double ScoreThreshold, double ProminenceThreshold, double SuppressionRadius, string Transition,
        double NotchRadius, double Strength, int Order, SnapshotRecipe? Accepted, SnapshotRecipe? Draft);
    private sealed record SnapshotRecipe(string Channel, string Transition, double Radius, double Strength,
        int Order, SnapshotNotch[] Notches);
    private sealed record SnapshotNotch(double Fx, double Fy, string Origin, bool Enabled);
}
