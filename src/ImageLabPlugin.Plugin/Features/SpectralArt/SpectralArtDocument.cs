using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.SpectralArt;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.SpectralArt;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.SpectralArt;

/// <summary>Spectral Art 的多实例可持久化 Document，只协调状态、命令、取消和 Avalonia Bitmap 生命周期。</summary>
/// <remarks>
/// 本类刻意不保存 Complex、不执行 FFT/像素循环/共轭公式，也不解析 recipe JSON。载体 Session、Pattern 和
/// 最后有效结果只有在完整用例成功且 generation 仍匹配时才原子替换；取消、异常与迟到完成均保留旧结果。
/// 快照只保存轻量参数和文件显示名，恢复时不会访问文件、栅格化文字或执行 FFT。
/// </remarks>
internal sealed partial class SpectralArtDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private readonly IPrepareSpectralArtCarrierUseCase _prepare;
    private readonly ICreateSpectralPatternUseCase _createPattern;
    private readonly IRenderSpectralArtUseCase _render;
    private readonly IExportSpectralArtImageUseCase _exportImage;
    private readonly IImportSpectralArtRecipeUseCase _importRecipe;
    private readonly IExportSpectralArtRecipeUseCase _exportRecipe;
    private readonly IExportSpectralArtReportUseCase _exportReport;
    private readonly IImageFileDialog _imageDialog;
    private readonly ISpectralArtFileDialog _fileDialog;
    private readonly IImageCodec _codec;
    private readonly SpectralPatternPreviewProjector _patternProjector;
    private readonly ISpectralArtSnapshotSerializer _snapshotSerializer;
    private readonly IDocumentLifetime _lifetime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SpectralArtSession? _session;
    private SpectralPattern? _pattern;
    private SpectralArtRecipe? _recipe;
    private SpectralArtResult? _result;
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _debounceCancellation;
    private DocumentPresentationState _presentation = new("频谱艺术");
    private long _generation;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;
    private bool _disposed;

    public SpectralArtDocument(IPrepareSpectralArtCarrierUseCase prepare,
        ICreateSpectralPatternUseCase createPattern, IRenderSpectralArtUseCase render,
        IExportSpectralArtImageUseCase exportImage, IImportSpectralArtRecipeUseCase importRecipe,
        IExportSpectralArtRecipeUseCase exportRecipe, IExportSpectralArtReportUseCase exportReport,
        IImageFileDialog imageDialog, ISpectralArtFileDialog fileDialog, IImageCodec codec,
        SpectralPatternPreviewProjector patternProjector, ISpectralArtSnapshotSerializer snapshotSerializer,
        IDocumentLifetime lifetime)
    {
        _prepare = prepare; _createPattern = createPattern; _render = render; _exportImage = exportImage;
        _importRecipe = importRecipe; _exportRecipe = exportRecipe; _exportReport = exportReport;
        _imageDialog = imageDialog; _fileDialog = fileDialog; _codec = codec;
        _patternProjector = patternProjector; _snapshotSerializer = snapshotSerializer; _lifetime = lifetime;
    }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _patternImagePath = string.Empty;
    [ObservableProperty] private string _patternText = "SPECTRAL";
    [ObservableProperty] private string _selectedSourceKind = "文字";
    [ObservableProperty] private string _selectedSampling = "二值最近邻";
    [ObservableProperty] private string _selectedFit = "Contain";
    [ObservableProperty] private string _selectedBackground = "黑色";
    [ObservableProperty] private string _fontFamily = "Arial";
    [ObservableProperty] private double _fontSize = 72d;
    [ObservableProperty] private int _fontWeight = 700;
    [ObservableProperty] private int _padding = 8;
    [ObservableProperty] private int _patternWidth = 96;
    [ObservableProperty] private int _patternHeight = 32;
    [ObservableProperty] private double _binaryThreshold = 0.5d;
    [ObservableProperty] private bool _invertPattern;
    [ObservableProperty] private double _strength = SpectralArtProtocol.DefaultStrength;
    [ObservableProperty] private double _regionLeft = 0.14d;
    [ObservableProperty] private double _regionTop = -0.34d;
    [ObservableProperty] private double _regionRight = 0.34d;
    [ObservableProperty] private double _regionBottom = -0.14d;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "请选择载体并创建 Pattern；强度 0 会保留原图。";
    [ObservableProperty] private string _qualitySummary = "尚未渲染";
    [ObservableProperty] private string _frequencySummary = "尚未渲染";
    private Bitmap? _sourcePreview;
    private Bitmap? _patternPreview;
    private Bitmap? _mappingPreview;
    private Bitmap? _resultPreview;
    private Bitmap? _sourceSpectrumPreview;
    private Bitmap? _resultSpectrumPreview;
    private Bitmap? _spectrumDifferencePreview;
    private Bitmap? _spatialDifferencePreview;

    public IReadOnlyList<string> SourceKindOptions { get; } = ["文字", "Logo 图片", "二维码图片"];
    public IReadOnlyList<string> SamplingOptions { get; } = ["二值最近邻", "灰度面积"];
    public IReadOnlyList<string> FitOptions { get; } = ["Contain", "Stretch"];
    public IReadOnlyList<string> BackgroundOptions { get; } = ["黑色", "白色"];
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasCarrier => _session is not null;
    public bool HasPattern => _pattern is not null;
    public bool HasResult => _result is not null;
    public Bitmap? SourcePreview => _sourcePreview;
    public Bitmap? PatternPreview => _patternPreview;
    public Bitmap? MappingPreview => _mappingPreview;
    public Bitmap? ResultPreview => _resultPreview;
    public Bitmap? SourceSpectrumPreview => _sourceSpectrumPreview;
    public Bitmap? ResultSpectrumPreview => _resultSpectrumPreview;
    public Bitmap? SpectrumDifferencePreview => _spectrumDifferencePreview;
    public Bitmap? SpatialDifferencePreview => _spatialDifferencePreview;
    public event EventHandler? PresentationChanged;
    public event EventHandler? IsDirtyChanged;

    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation); cancellationToken.ThrowIfCancellationRequested();
        _restoring = true;
        try
        {
            if (activation is RestoreDocumentActivation restore) Restore(restore.RestoredContent);
            _presentation = new(string.IsNullOrWhiteSpace(activation.Title) ? "频谱艺术" : activation.Title);
            PresentationChanged?.Invoke(this, EventArgs.Empty); _revision = _acceptedRevision = 0;
        }
        finally { _restoring = false; }
        return ValueTask.CompletedTask;
    }

    [RelayCommand] private async Task SelectCarrierAsync() { var path = await _imageDialog.PickImageAsync(_lifetime.ClosingToken); if (path is not null) SourcePath = path; }
    [RelayCommand] private async Task SelectPatternImageAsync() { var path = await _imageDialog.PickImageAsync(_lifetime.ClosingToken); if (path is not null) PatternImagePath = path; }

    [RelayCommand]
    private Task PrepareCarrierAsync() => RunGuardedAsync("正在解码载体并建立只读 Y 频谱…", async (generation, token) =>
    {
        var candidate = await _prepare.ExecuteAsync(new SpectralCarrierRequest(SourcePath), token).ConfigureAwait(false);
        Bitmap? source = null, spectrum = null;
        try
        {
            source = await CreateBitmapAsync(candidate.SourceImage, token).ConfigureAwait(false);
            spectrum = await CreateBitmapAsync(candidate.SourceSpectrumPreview, token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _generation) return;
                var old = _session; _session = candidate; candidate = null!; old?.Dispose();
                ReplaceBitmap(ref _sourcePreview, source, nameof(SourcePreview)); source = null;
                ReplaceBitmap(ref _sourceSpectrumPreview, spectrum, nameof(SourceSpectrumPreview)); spectrum = null;
                InvalidateResult(false); StatusMessage = $"载体已准备：{_session.SourceImage.Size.Width}×{_session.SourceImage.Size.Height}，补零 {_session.Spectrum.PaddedWidth}×{_session.Spectrum.PaddedHeight}。";
                OnPropertyChanged(nameof(HasCarrier));
            });
        }
        finally { candidate?.Dispose(); source?.Dispose(); spectrum?.Dispose(); }
    });

    [RelayCommand]
    private Task CreatePatternAsync() => RunGuardedAsync("正在规范化 Pattern…", async (generation, token) =>
    {
        var request = CreatePatternRequest();
        var candidate = await _createPattern.ExecuteAsync(request, token).ConfigureAwait(false);
        var preview = await CreateBitmapAsync(_patternProjector.Project(candidate, token), token).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (generation != _generation) { preview.Dispose(); return; }
            _pattern = candidate; ReplaceBitmap(ref _patternPreview, preview, nameof(PatternPreview));
            InvalidateResult(false); StatusMessage = $"Pattern 已创建：{candidate.Width}×{candidate.Height}，指纹 {candidate.Fingerprint}。";
            OnPropertyChanged(nameof(HasPattern));
        });
    });

    [RelayCommand] private Task RenderAsync() => RenderCurrentAsync(false);

    private Task RenderCurrentAsync(bool debounced) => RunGuardedAsync(debounced ? "参数稳定，正在重新渲染…" : "正在映射频点、写入幅度并重建…", async (generation, token) =>
    {
        var session = _session ?? throw new InvalidOperationException("请先准备载体。");
        var pattern = _pattern ?? throw new InvalidOperationException("请先创建或导入 Pattern。");
        var recipe = new SpectralArtRecipe(pattern, ResolveRegion(), ResolveFit(), Strength);
        var candidate = await _render.ExecuteAsync(session, recipe, token).ConfigureAwait(false);
        var bitmaps = await CreateResultBitmapsAsync(candidate, token).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (generation != _generation) { DisposeAll(bitmaps); return; }
            _recipe = recipe; _result = candidate; CommitBitmaps(bitmaps);
            QualitySummary = $"PSNR-Y {Format(candidate.Quality.PsnrLumaDb)} dB；SSIM-Y {candidate.Quality.GlobalSsimLuma:F6}；RGB 改变 {candidate.Quality.ChangedPixelRatioRgb:P3}";
            FrequencySummary = $"写入 {candidate.Frequency.TotalWrittenBins} 点；能量变化 {candidate.Frequency.EnergyIncreaseRatio:P3}；最大虚部 {candidate.Raw.MaximumImaginaryResidual:E3}；共轭残差 {candidate.Frequency.MaximumConjugateResidual:E3}";
            StatusMessage = $"渲染完成；配方指纹 {candidate.RecipeFingerprint}。可见性仅是本实验相对量，不代表识别或扫码成功。";
            OnPropertyChanged(nameof(HasResult));
        });
    });

    [RelayCommand] private void Cancel() { ++_generation; CancelOperations(); StatusMessage = "已请求取消；Session、Pattern 和最后有效结果保持不变。"; }

    [RelayCommand]
    private async Task ImportRecipeAsync()
    {
        var path = await _fileDialog.PickSpectralRecipeInputAsync(_lifetime.ClosingToken); if (path is null) return;
        await RunGuardedAsync("正在严格导入配方…", async (generation, token) =>
        {
            var recipe = await _importRecipe.ExecuteAsync(path, token).ConfigureAwait(false);
            var preview = await CreateBitmapAsync(_patternProjector.Project(recipe.Pattern, token), token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _generation) { preview.Dispose(); return; }
                _pattern = recipe.Pattern; ApplyRecipe(recipe); ReplaceBitmap(ref _patternPreview, preview, nameof(PatternPreview));
                InvalidateResult(false); StatusMessage = "配方已导入；不会自动读取载体或执行 FFT。"; OnPropertyChanged(nameof(HasPattern));
            });
        });
    }

    [RelayCommand] private Task ExportRecipeAsync() => ExportRecipeCoreAsync();
    [RelayCommand] private Task ExportResultAsync() => ExportResultCoreAsync();
    [RelayCommand] private Task ExportReportJsonAsync() => ExportReportCoreAsync(false);
    [RelayCommand] private Task ExportReportCsvAsync() => ExportReportCoreAsync(true);

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 原始文字、绝对路径、Pattern 像素、FFT 和结果均不进入工作区快照；只保留可重建的非敏感意图。
        var snapshot = new SpectralArtSnapshotState(Path.GetFileName(SourcePath), Path.GetFileName(PatternImagePath), SelectedSourceKind,
            SelectedSampling, SelectedFit, SelectedBackground, FontFamily, FontSize, FontWeight, Padding,
            PatternWidth, PatternHeight, BinaryThreshold, InvertPattern, Strength, RegionLeft, RegionTop,
            RegionRight, RegionBottom, SpectralArtProtocol.SnapshotSchema);
        var payload = _snapshotSerializer.Serialize(snapshot);
        return ValueTask.FromResult(new DocumentSaveSnapshot(new DocumentRevision(_revision),
            new DocumentContent(SpectralArtProtocol.SnapshotSchema, payload)));
    }

    public void AcceptChanges(DocumentRevision savedRevision)
    {
        var dirty = IsDirty; if (savedRevision.Value == _revision) _acceptedRevision = _revision;
        if (dirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_generation; CancelOperations();
        _debounceCancellation?.Cancel(); _debounceCancellation?.Dispose(); _session?.Dispose(); _gate.Dispose();
        ReplaceBitmap(ref _sourcePreview, null, nameof(SourcePreview)); ReplaceBitmap(ref _patternPreview, null, nameof(PatternPreview));
        ReplaceBitmap(ref _mappingPreview, null, nameof(MappingPreview)); ReplaceBitmap(ref _resultPreview, null, nameof(ResultPreview));
        ReplaceBitmap(ref _sourceSpectrumPreview, null, nameof(SourceSpectrumPreview)); ReplaceBitmap(ref _resultSpectrumPreview, null, nameof(ResultSpectrumPreview));
        ReplaceBitmap(ref _spectrumDifferencePreview, null, nameof(SpectrumDifferencePreview)); ReplaceBitmap(ref _spatialDifferencePreview, null, nameof(SpatialDifferencePreview));
    }

    partial void OnSourcePathChanged(string value) { if (!_restoring) { InvalidateCarrier(); MarkChanged(); } }
    partial void OnPatternImagePathChanged(string value) { if (!_restoring) InvalidatePattern(); }
    partial void OnPatternTextChanged(string value) { if (!_restoring) InvalidatePattern(); }
    partial void OnSelectedSourceKindChanged(string value) { if (!_restoring) InvalidatePattern(); }
    partial void OnSelectedSamplingChanged(string value) { if (!_restoring) InvalidatePattern(); }
    partial void OnSelectedFitChanged(string value) { if (!_restoring) RecipeParameterChanged(); }
    partial void OnSelectedBackgroundChanged(string value) { if (!_restoring) InvalidatePattern(); }
    partial void OnFontFamilyChanged(string value) { if (!_restoring) InvalidatePattern(); }
    partial void OnFontSizeChanged(double value) { if (!_restoring) InvalidatePattern(); }
    partial void OnFontWeightChanged(int value) { if (!_restoring) InvalidatePattern(); }
    partial void OnPaddingChanged(int value) { if (!_restoring) InvalidatePattern(); }
    partial void OnPatternWidthChanged(int value) { if (!_restoring) InvalidatePattern(); }
    partial void OnPatternHeightChanged(int value) { if (!_restoring) InvalidatePattern(); }
    partial void OnBinaryThresholdChanged(double value) { if (!_restoring) InvalidatePattern(); }
    partial void OnInvertPatternChanged(bool value) { if (!_restoring) InvalidatePattern(); }
    partial void OnStrengthChanged(double value) { if (!_restoring) RecipeParameterChanged(); }
    partial void OnRegionLeftChanged(double value) { if (!_restoring) RecipeParameterChanged(); }
    partial void OnRegionTopChanged(double value) { if (!_restoring) RecipeParameterChanged(); }
    partial void OnRegionRightChanged(double value) { if (!_restoring) RecipeParameterChanged(); }
    partial void OnRegionBottomChanged(double value) { if (!_restoring) RecipeParameterChanged(); }

    private async Task RunGuardedAsync(string status, Func<long, CancellationToken, Task> operation)
    {
        if (_disposed) return; CancelOperations();
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _operationCancellation; var token = current.Token; var generation = ++_generation; var entered = false;
        IsBusy = true; StatusMessage = status;
        try { await _gate.WaitAsync(token); entered = true; await operation(generation, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { if (!_lifetime.IsClosing && generation == _generation) StatusMessage = "操作已取消；未提交迟到结果。"; }
        catch (Exception exception) { if (generation == _generation) StatusMessage = exception.Message; }
        finally { if (entered) _gate.Release(); if (ReferenceEquals(current, _operationCancellation)) IsBusy = false; }
    }

    private SpectralPatternRequest CreatePatternRequest()
    {
        var kind = ResolveSourceKind(); var sampling = ResolveSampling();
        var options = new SpectralPatternNormalizationOptions(kind, sampling, PatternWidth, PatternHeight,
            BinaryThreshold, InvertPattern, SelectedBackground == "白色" ? SpectralPatternBackground.White : SpectralPatternBackground.Black);
        return new SpectralPatternRequest(kind, PatternText, PatternImagePath, FontFamily, FontSize,
            FontWeight, Padding, options);
    }

    private SpectralArtRegion ResolveRegion() => new(RegionLeft, RegionTop, RegionRight, RegionBottom);
    private SpectralPatternSourceKind ResolveSourceKind() => SelectedSourceKind switch { "Logo 图片" => SpectralPatternSourceKind.LogoImage, "二维码图片" => SpectralPatternSourceKind.QrImage, _ => SpectralPatternSourceKind.Text };
    private SpectralPatternSamplingMode ResolveSampling() => SelectedSampling == "灰度面积" ? SpectralPatternSamplingMode.GrayscaleArea : SpectralPatternSamplingMode.BinaryNearest;
    private SpectralPatternFitMode ResolveFit() => SelectedFit == "Stretch" ? SpectralPatternFitMode.Stretch : SpectralPatternFitMode.Contain;

    private void InvalidateCarrier()
    {
        ++_generation; CancelOperations(); _session?.Dispose(); _session = null; InvalidateResult(false);
        ReplaceBitmap(ref _sourcePreview, null, nameof(SourcePreview)); ReplaceBitmap(ref _sourceSpectrumPreview, null, nameof(SourceSpectrumPreview));
        StatusMessage = "载体路径已改变；请显式重新准备。"; OnPropertyChanged(nameof(HasCarrier));
    }

    private void InvalidatePattern()
    {
        ++_generation; CancelOperations(); _pattern = null; _recipe = null; InvalidateResult(false);
        ReplaceBitmap(ref _patternPreview, null, nameof(PatternPreview)); StatusMessage = "Pattern 输入已改变；请重新创建 Pattern。";
        OnPropertyChanged(nameof(HasPattern)); MarkChanged();
    }

    private void RecipeParameterChanged()
    {
        if (!double.IsFinite(Strength) || Strength is < 0d or > SpectralAmplitudeWriter.MaximumStrength)
        { StatusMessage = "强度必须是 [0,8] 内的有限值。"; return; }
        InvalidateResult(true); MarkChanged();
        // 200 ms 防抖只在 Session 和 Pattern 均有效时触发；每次变化都取消前一次等待。
        _debounceCancellation?.Cancel(); _debounceCancellation?.Dispose();
        _debounceCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var token = _debounceCancellation.Token;
        _ = DebouncedRenderAsync(token);
    }

    private async Task DebouncedRenderAsync(CancellationToken token)
    {
        try { await Task.Delay(SpectralArtProtocol.DebounceMilliseconds, token); if (_session is not null && _pattern is not null) await RenderCurrentAsync(true); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void InvalidateResult(bool announce)
    {
        _recipe = null; _result = null;
        ReplaceBitmap(ref _mappingPreview, null, nameof(MappingPreview)); ReplaceBitmap(ref _resultPreview, null, nameof(ResultPreview));
        ReplaceBitmap(ref _resultSpectrumPreview, null, nameof(ResultSpectrumPreview)); ReplaceBitmap(ref _spectrumDifferencePreview, null, nameof(SpectrumDifferencePreview));
        ReplaceBitmap(ref _spatialDifferencePreview, null, nameof(SpatialDifferencePreview)); QualitySummary = FrequencySummary = "尚未渲染";
        if (announce) StatusMessage = "配方参数已改变；旧结果已作废。"; OnPropertyChanged(nameof(HasResult));
    }

    private async Task ExportResultCoreAsync()
    {
        if (_session is null || _result is null || _recipe is null) { StatusMessage = "没有可导出的有效结果。"; return; }
        var path = await _fileDialog.PickSpectralResultPngAsync("spectral-art-result.png", _lifetime.ClosingToken); if (path is null) return;
        try { await _exportImage.ExecuteAsync(_session, _result, _recipe, path, _lifetime.ClosingToken); StatusMessage = "PNG 已通过 RGBA 与频谱事实回读并原子导出。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    private async Task ExportRecipeCoreAsync()
    {
        var recipe = _recipe ?? (_pattern is null ? null : new SpectralArtRecipe(_pattern, ResolveRegion(), ResolveFit(), Strength));
        if (recipe is null) { StatusMessage = "请先创建或导入 Pattern。"; return; }
        var path = await _fileDialog.PickSpectralRecipeOutputAsync("spectral-art-recipe.json", _lifetime.ClosingToken); if (path is null) return;
        try { await _exportRecipe.ExecuteAsync(recipe, path, _lifetime.ClosingToken); StatusMessage = "独立配方已原子导出；不含载体路径和原始文字。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    private async Task ExportReportCoreAsync(bool csv)
    {
        if (_session is null || _result is null || _recipe is null) { StatusMessage = "没有可导出的有效诊断。"; return; }
        var path = csv ? await _fileDialog.PickSpectralReportCsvAsync("spectral-art-report.csv", _lifetime.ClosingToken)
            : await _fileDialog.PickSpectralReportJsonAsync("spectral-art-report.json", _lifetime.ClosingToken);
        if (path is null) return;
        var report = new SpectralArtReport(SpectralArtProtocol.ReportProtocol, 1, _result.SourceFingerprint,
            _session.SourceImage.Size.Width, _session.SourceImage.Size.Height, _session.Spectrum.PaddedWidth,
            _session.Spectrum.PaddedHeight, _recipe.Pattern.SourceKind, _recipe.Pattern.Width, _recipe.Pattern.Height,
            _recipe.Pattern.Fingerprint, _recipe.Region, _recipe.Strength, _result.Frequency, _result.Raw,
            _result.Quality, _result.Timings, "相对可见性不是识别率、扫码成功率或隐写安全证明。");
        try { await _exportReport.ExecuteAsync(report, path, csv, _lifetime.ClosingToken); StatusMessage = "脱敏报告已原子导出；不含路径、文件名或原始文字。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    private void ApplyRecipe(SpectralArtRecipe recipe)
    {
        _restoring = true;
        try
        {
            SelectedSourceKind = recipe.Pattern.SourceKind switch { SpectralPatternSourceKind.LogoImage => "Logo 图片", SpectralPatternSourceKind.QrImage => "二维码图片", _ => "文字" };
            SelectedSampling = recipe.Pattern.SamplingMode == SpectralPatternSamplingMode.GrayscaleArea ? "灰度面积" : "二值最近邻";
            SelectedFit = recipe.FitMode == SpectralPatternFitMode.Stretch ? "Stretch" : "Contain";
            PatternWidth = recipe.Pattern.Width; PatternHeight = recipe.Pattern.Height; Strength = recipe.Strength;
            RegionLeft = recipe.Region.Left; RegionTop = recipe.Region.Top; RegionRight = recipe.Region.Right; RegionBottom = recipe.Region.Bottom;
        }
        finally { _restoring = false; }
        MarkChanged();
    }

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SpectralArtProtocol.SnapshotSchema) { StatusMessage = $"不支持快照 schema {content.SchemaVersion}；已使用安全默认值。"; return; }
        var value = _snapshotSerializer.Deserialize(content.Payload); if (value is null || value.Schema != SpectralArtProtocol.SnapshotSchema) return;
        SourcePath = PatternImagePath = string.Empty; PatternText = "SPECTRAL";
        SelectedSourceKind = SourceKindOptions.Contains(value.SourceKind) ? value.SourceKind : "文字";
        SelectedSampling = SamplingOptions.Contains(value.Sampling) ? value.Sampling : "二值最近邻";
        SelectedFit = FitOptions.Contains(value.Fit) ? value.Fit : "Contain";
        SelectedBackground = BackgroundOptions.Contains(value.Background) ? value.Background : "黑色";
        FontFamily = string.IsNullOrWhiteSpace(value.FontFamily) ? "Arial" : value.FontFamily;
        FontSize = Math.Clamp(value.FontSize, 8d, 512d); FontWeight = Math.Clamp(value.FontWeight, 100, 900); Padding = Math.Clamp(value.Padding, 0, 128);
        PatternWidth = Math.Clamp(value.PatternWidth, 1, SpectralPattern.MaximumEdge); PatternHeight = Math.Clamp(value.PatternHeight, 1, SpectralPattern.MaximumEdge);
        BinaryThreshold = Math.Clamp(value.BinaryThreshold, 0d, 1d); InvertPattern = value.Invert; Strength = Math.Clamp(value.Strength, 0d, SpectralAmplitudeWriter.MaximumStrength);
        RegionLeft = value.Left; RegionTop = value.Top; RegionRight = value.Right; RegionBottom = value.Bottom;
        StatusMessage = $"已恢复 {value.SourceDisplayName ?? "载体"}/{value.PatternDisplayName ?? "Pattern"} 的轻量参数；请显式重新选择输入，不会自动 IO 或 FFT。";
    }

    private async Task<Bitmap> CreateBitmapAsync(PixelImage image, CancellationToken token)
    { var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token); using var stream = new MemoryStream(bytes, false); return new Bitmap(stream); }

    private async Task<Bitmap[]> CreateResultBitmapsAsync(SpectralArtResult result, CancellationToken token) =>
    [await CreateBitmapAsync(result.Output, token), await CreateBitmapAsync(result.PatternPreview, token),
     await CreateBitmapAsync(result.MappingPreview, token), await CreateBitmapAsync(result.SourceSpectrumPreview, token),
     await CreateBitmapAsync(result.ResultSpectrumPreview, token), await CreateBitmapAsync(result.SpectrumDifferencePreview, token),
     await CreateBitmapAsync(result.Difference4X.Absolute, token)];

    private void CommitBitmaps(Bitmap[] values)
    {
        ReplaceBitmap(ref _resultPreview, values[0], nameof(ResultPreview)); ReplaceBitmap(ref _patternPreview, values[1], nameof(PatternPreview));
        ReplaceBitmap(ref _mappingPreview, values[2], nameof(MappingPreview)); ReplaceBitmap(ref _sourceSpectrumPreview, values[3], nameof(SourceSpectrumPreview));
        ReplaceBitmap(ref _resultSpectrumPreview, values[4], nameof(ResultSpectrumPreview)); ReplaceBitmap(ref _spectrumDifferencePreview, values[5], nameof(SpectrumDifferencePreview));
        ReplaceBitmap(ref _spatialDifferencePreview, values[6], nameof(SpatialDifferencePreview));
    }

    private void ReplaceBitmap(ref Bitmap? field, Bitmap? value, string propertyName) { var old = field; field = value; OnPropertyChanged(propertyName); if (!ReferenceEquals(old, value)) old?.Dispose(); }
    private static void DisposeAll(IEnumerable<Bitmap> values) { foreach (var value in values) value.Dispose(); }
    private void CancelOperations() { _operationCancellation?.Cancel(); _operationCancellation?.Dispose(); _operationCancellation = null; }
    private void MarkChanged() { if (_restoring) return; var dirty = IsDirty; _revision++; if (dirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }
    private static string Format(double value) => double.IsPositiveInfinity(value) ? "∞" : value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

}
