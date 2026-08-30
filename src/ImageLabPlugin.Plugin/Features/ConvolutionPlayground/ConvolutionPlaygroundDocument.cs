using System.Globalization;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.Convolution;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Convolution;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.ConvolutionPlayground;

/// <summary>“卷积核实验台”Document：拥有单实例参数、会话、异步代次和界面位图。</summary>
/// <remarks>
/// 领域与应用层都不认识 Avalonia。Document 只协调命令并把 PixelImage 适配成 Bitmap：每次参数改变都会推进
/// generation、取消旧任务并立即使完整尺寸结果过期；迟到结果提交前同时核对 generation、Session 引用和
/// recipe fingerprint。替换 Bitmap 时先发布新引用再释放旧对象，关闭时则先取消、再释放 Session/Bitmap，
/// 从而避免多实例间共享大图或旧图回闪。
/// </remarks>
internal sealed partial class ConvolutionPlaygroundDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private readonly IPrepareConvolutionSessionUseCase _prepareUseCase;
    private readonly IRenderConvolutionPreviewUseCase _previewUseCase;
    private readonly IInspectConvolutionPixelUseCase _inspectUseCase;
    private readonly IRenderKernelResponseUseCase _responseUseCase;
    private readonly IRenderFullConvolutionUseCase _fullUseCase;
    private readonly IExportConvolutionImageUseCase _exportUseCase;
    private readonly ConvolutionKernelParser _parser;
    private readonly ConvolutionPresetFactory _factory;
    private readonly IImageFileDialog _dialog;
    private readonly IImageCodec _codec;
    private readonly IDocumentLifetime _lifetime;
    private DocumentPresentationState _presentation = new("卷积核实验台");
    private ConvolutionSession? _session;
    private ConvolutionPreviewResult? _preview;
    private FullConvolutionResult? _fullResult;
    private KernelFrequencyResponse? _frequencyResponse;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _fullCancellation;
    private CancellationTokenSource? _exportCancellation;
    private long _generation;
    private long _responseProjectionGeneration;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;
    private bool _updatingParameters;
    private bool _disposed;

    public ConvolutionPlaygroundDocument(
        IPrepareConvolutionSessionUseCase prepareUseCase,
        IRenderConvolutionPreviewUseCase previewUseCase,
        IInspectConvolutionPixelUseCase inspectUseCase,
        IRenderKernelResponseUseCase responseUseCase,
        IRenderFullConvolutionUseCase fullUseCase,
        IExportConvolutionImageUseCase exportUseCase,
        ConvolutionKernelParser parser,
        ConvolutionPresetFactory factory,
        IImageFileDialog dialog,
        IImageCodec codec,
        IDocumentLifetime lifetime)
    {
        _prepareUseCase = prepareUseCase; _previewUseCase = previewUseCase; _inspectUseCase = inspectUseCase;
        _responseUseCase = responseUseCase; _fullUseCase = fullUseCase; _exportUseCase = exportUseCase;
        _parser = parser; _factory = factory; _dialog = dialog; _codec = codec; _lifetime = lifetime;
        ApplyPresetCore(markChanged: false);
    }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _selectedPreset = "gaussian";
    [ObservableProperty] private int _kernelSize = 3;
    [ObservableProperty] private double _sigma = 1d;
    [ObservableProperty] private double _amount = 1d;
    [ObservableProperty] private double _highBoostA = 2d;
    [ObservableProperty] private double _motionLength = 3d;
    [ObservableProperty] private double _angleDegrees;
    [ObservableProperty] private double _embossStrength = 1d;
    [ObservableProperty] private string _kernelText = string.Empty;
    [ObservableProperty] private string _selectedBorder = "Reflect101";
    [ObservableProperty] private double _constantBorderValue;
    [ObservableProperty] private string _selectedNormalization = "KernelSum";
    [ObservableProperty] private double _explicitDivisor = 1d;
    [ObservableProperty] private double _bias;
    [ObservableProperty] private string _selectedChannel = "Rgb";
    [ObservableProperty] private string _selectedGradientOutput = "Magnitude";
    [ObservableProperty] private int _analysisMaximumEdge = 1024;
    [ObservableProperty] private bool _showPhaseResponse;
    [ObservableProperty] private int _probeX;
    [ObservableProperty] private int _probeY;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isFullBusy;
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private string _statusMessage = "请选择 PNG/JPEG；默认使用 3×3 高斯核和 Reflect-101 边界。";
    [ObservableProperty] private string _kernelSummary = "尚未生成核";
    [ObservableProperty] private string _resultSummary = "尚未生成代理结果";
    [ObservableProperty] private string _responseSummary = "频率响应不包含偏置、边界扩展和输出裁切。";
    [ObservableProperty] private string _probeSummary = "生成代理后输入坐标，可查看逐项贡献。";
    [ObservableProperty] private string _fullResultSummary = "尚未执行完整尺寸卷积";
    [ObservableProperty] private Bitmap? _sourcePreview;
    [ObservableProperty] private Bitmap? _resultPreview;
    [ObservableProperty] private Bitmap? _differencePreview;
    [ObservableProperty] private Bitmap? _responsePreview;

    public IReadOnlyList<string> PresetOptions { get; } = ["identity", "mean", "gaussian", "motion", "sharpen", "unsharp", "high-boost", "sobel", "prewitt", "scharr", "laplacian-4", "laplacian-8", "emboss", "custom"];
    public IReadOnlyList<string> BorderOptions { get; } = Enum.GetNames<BorderMode>();
    public IReadOnlyList<string> NormalizationOptions { get; } = Enum.GetNames<KernelNormalizationMode>();
    public IReadOnlyList<string> ChannelOptions { get; } = Enum.GetNames<ConvolutionChannelMode>();
    public IReadOnlyList<string> GradientOutputOptions { get; } = Enum.GetNames<GradientOutputMode>();
    public IReadOnlyList<int> AnalysisEdgeOptions { get; } = [512, 1024, 2048];
    public string HelpSummary => ConvolutionHelpCatalog.Summary;
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasSession => _session is not null;
    public bool HasPreview => _preview is not null;
    public bool CanExport => _fullResult is not null && TryBuildRecipe(out var recipe, out _) &&
        StringComparer.Ordinal.Equals(_fullResult.RecipeFingerprint, recipe.Fingerprint());
    public bool IsOperationBusy => IsBusy || IsFullBusy || IsExporting;

    public event EventHandler? PresentationChanged;
    public event EventHandler? IsDirtyChanged;

    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation); cancellationToken.ThrowIfCancellationRequested();
        _restoring = true;
        try
        {
            if (activation is RestoreDocumentActivation restore) Restore(restore.RestoredContent);
            _presentation = new DocumentPresentationState(string.IsNullOrWhiteSpace(activation.Title) ? "卷积核实验台" : activation.Title);
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
    private async Task LoadSourceAsync()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || !File.Exists(SourcePath))
        { StatusMessage = "请选择存在的 PNG 或 JPEG 图片。"; return; }
        CancelAndDispose(ref _loadCancellation); _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _loadCancellation; var token = current.Token; var generation = ++_generation; IsBusy = true;
        try
        {
            StatusMessage = $"正在解码并建立最大边 {AnalysisMaximumEdge} 的抗混叠分析代理…";
            var session = await _prepareUseCase.ExecuteAsync(SourcePath, AnalysisMaximumEdge, token).ConfigureAwait(true);
            if (!CanCommit(generation)) { session.Dispose(); return; }
            ReplaceSession(session); ReplaceSourceBitmap(await CreateBitmapAsync(session.AnalysisProxy, token));
            StatusMessage = $"已载入完整图 {session.SourceImage.Size.Width}×{session.SourceImage.Size.Height}；代理 {session.AnalysisProxy.Size.Width}×{session.AnalysisProxy.Size.Height}。";
            OnPropertyChanged(nameof(HasSession)); await RenderPreviewCoreAsync(debounce: false).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { if (!_lifetime.IsClosing) StatusMessage = "载入已取消。"; }
        catch (Exception exception) { if (generation == _generation) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _loadCancellation)) IsBusy = false; }
    }

    [RelayCommand] private void ApplyPreset() => ApplyPresetCore(markChanged: true);
    [RelayCommand] private Task RenderPreviewAsync() => RenderPreviewCoreAsync(debounce: false);

    [RelayCommand]
    private async Task RenderFullAsync()
    {
        var session = _session;
        if (session is null) { StatusMessage = "请先载入图片。"; return; }
        if (!TryBuildRecipe(out var recipe, out var error)) { StatusMessage = error ?? "配方无效。"; return; }
        CancelAndDispose(ref _fullCancellation); _fullCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _fullCancellation; var token = current.Token; var generation = _generation; IsFullBusy = true;
        try
        {
            var operations = checked(session.SourceImage.Size.PixelCount * recipe.Operator.PrimaryKernel.Size * recipe.Operator.PrimaryKernel.Size * (recipe.Channel == ConvolutionChannelMode.Rgb ? 3L : 1L));
            StatusMessage = $"正在显式执行完整尺寸卷积，约 {operations:N0} 次乘加；可随时取消。";
            var result = await _fullUseCase.ExecuteAsync(session, recipe, token).ConfigureAwait(true);
            if (!CanCommit(generation) || !ReferenceEquals(session, _session) || result.RecipeFingerprint != recipe.Fingerprint()) return;
            _fullResult = result; FullResultSummary = $"完整尺寸 {result.Image.Size.Width}×{result.Image.Size.Height}；{result.Elapsed.TotalMilliseconds:N0} ms；YCbCr 回写裁切 {result.ColorReconstructionClippedPixels:N0} 像素。";
            StatusMessage = "完整尺寸结果已生成；只有当前配方对应的结果可导出 PNG。"; OnPropertyChanged(nameof(CanExport));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { StatusMessage = "完整尺寸计算已取消，未提交半成品。"; }
        catch (Exception exception) { if (generation == _generation) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _fullCancellation)) IsFullBusy = false; }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_fullResult is null) { StatusMessage = "请先生成当前配方的完整尺寸结果。"; return; }
        if (!TryBuildRecipe(out var recipe, out var error)) { StatusMessage = error ?? "配方无效。"; return; }
        var path = await _dialog.PickOutputImageAsync($"{Path.GetFileNameWithoutExtension(SourcePath)}.convolution.png", _lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        CancelAndDispose(ref _exportCancellation); _exportCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var token = _exportCancellation.Token; IsExporting = true;
        try
        {
            var exported = await _exportUseCase.ExecuteAsync(_fullResult, recipe.Fingerprint(), path, token).ConfigureAwait(true);
            StatusMessage = $"已原子导出 PNG：{exported.OutputPath}（{exported.Size.Width}×{exported.Size.Height}）。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { StatusMessage = "导出已取消，未报告成功。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { IsExporting = false; }
    }

    [RelayCommand]
    private void InspectPixel()
    {
        if (_session is null || _preview is null) { StatusMessage = "请先生成代理结果。"; return; }
        if (!TryBuildRecipe(out var recipe, out var error)) { StatusMessage = error ?? "配方无效。"; return; }
        try
        {
            ProbeX = Math.Clamp(ProbeX, 0, _session.AnalysisProxy.Size.Width - 1); ProbeY = Math.Clamp(ProbeY, 0, _session.AnalysisProxy.Size.Height - 1);
            var report = _inspectUseCase.Execute(_session, _preview, recipe, ProbeX, ProbeY);
            var gradient = report.Magnitude is null ? string.Empty : $"；Gy={report.SecondaryDividedValue:0.######}，Magnitude={report.Magnitude:0.######}，Y 核贡献 {report.SecondaryContributions?.Count ?? 0} 项";
            ProbeSummary = $"({report.X},{report.Y}) 源 RGBA={report.SourcePixel}，结果 RGBA={report.ResultPixel}；Σx={report.Accumulator:0.######}，d={report.Divisor:0.######}，Gx/Σd={report.DividedValue:0.######}{gradient}，bias={report.Bias:0.###}，round={report.RoundedValue}，byte={report.FinalByte}；X/单核贡献 {report.Contributions.Count} 项，低/高裁切={report.LowClipped}/{report.HighClipped}。";
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand] private void Cancel() { _loadCancellation?.Cancel(); _previewCancellation?.Cancel(); _fullCancellation?.Cancel(); _exportCancellation?.Cancel(); }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parsed = _parser.Parse(KernelText); var coefficients = parsed.Kernel?.Coefficients.ToArray() ?? Array.Empty<double>();
        var payload = JsonSerializer.SerializeToElement(new Snapshot(SourcePath, SelectedPreset, KernelSize, Sigma, Amount,
            HighBoostA, MotionLength, AngleDegrees, EmbossStrength, parsed.Kernel?.Size ?? 3, coefficients, SelectedBorder,
            ConstantBorderValue, SelectedNormalization, ExplicitDivisor, Bias, SelectedChannel, SelectedGradientOutput,
            AnalysisMaximumEdge, ShowPhaseResponse, ProbeX, ProbeY, "true-convolution-v1"));
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
        CancelAndDispose(ref _loadCancellation); CancelAndDispose(ref _previewCancellation); CancelAndDispose(ref _fullCancellation); CancelAndDispose(ref _exportCancellation);
        ReplaceSession(null); ReplaceSourceBitmap(null); ReplaceResultBitmap(null);
        ReplaceDifferenceBitmap(null); ReplaceResponseBitmap(null);
    }

    private async Task RenderPreviewCoreAsync(bool debounce)
    {
        var session = _session;
        if (session is null) { StatusMessage = "请先载入图片。"; return; }
        if (!TryBuildRecipe(out var recipe, out var error)) { StatusMessage = error ?? "配方无效。"; return; }
        CancelAndDispose(ref _previewCancellation); _previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _previewCancellation; var token = current.Token; var generation = _generation; IsBusy = true;
        try
        {
            if (debounce) await Task.Delay(200, token).ConfigureAwait(true);
            StatusMessage = "正在代理上执行可取消真卷积并生成频率响应…";
            var previewTask = _previewUseCase.ExecuteAsync(session, recipe, token);
            var responseTask = _responseUseCase.ExecuteAsync(recipe, token);
            await Task.WhenAll(previewTask, responseTask).ConfigureAwait(true);
            var preview = await previewTask.ConfigureAwait(true); var response = await responseTask.ConfigureAwait(true);
            var resultBitmap = await CreateBitmapAsync(preview.Convolution.Image, token).ConfigureAwait(true);
            var differenceBitmap = await CreateBitmapAsync(preview.Difference.Absolute, token).ConfigureAwait(true);
            var responseBitmap = await CreateBitmapAsync(ShowPhaseResponse ? response.PhaseImage : response.MagnitudeImage, token).ConfigureAwait(true);
            if (!CanCommit(generation) || !ReferenceEquals(session, _session) || preview.RecipeFingerprint != recipe.Fingerprint())
            { resultBitmap.Dispose(); differenceBitmap.Dispose(); responseBitmap.Dispose(); return; }
            _preview = preview; _frequencyResponse = response; ReplaceResultBitmap(resultBitmap);
            ReplaceDifferenceBitmap(differenceBitmap); ReplaceResponseBitmap(responseBitmap);
            var statistics = preview.Convolution.Channels[0].Plane.Statistics;
            ResultSummary = $"代理 {preview.Convolution.Image.Size.Width}×{preview.Convolution.Image.Size.Height}；raw {statistics.RawMinimum:0.###}..{statistics.RawMaximum:0.###}；低/高裁切 {statistics.LowClippedSamples:N0}/{statistics.HighClippedSamples:N0}；MAE {preview.Difference.MeanAbsoluteError:0.###}。";
            ResponseSummary = $"256×256 {(response.IsGradientSummary ? "双核幅频摘要" : "核响应")}；DC={response.DcGain:0.######}，max={response.MaximumMagnitude:0.######}；不含偏置、边界和裁切。";
            StatusMessage = $"代理真卷积完成，用时 {preview.Elapsed.TotalMilliseconds:N0} ms。"; OnPropertyChanged(nameof(HasPreview));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { if (generation == _generation) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _previewCancellation)) IsBusy = false; }
    }

    private void ApplyPresetCore(bool markChanged)
    {
        try
        {
            _updatingParameters = true;
            if (SelectedPreset != "custom")
            {
                var definition = _factory.Create(SelectedPreset, KernelSize, Sigma, Amount, HighBoostA, MotionLength, AngleDegrees, EmbossStrength);
                KernelText = _parser.Format(definition.PrimaryKernel); SelectedNormalization = definition.RecommendedNormalization.ToString();
                Bias = definition.RecommendedBias;
                KernelSummary = $"{definition.DisplayName}；{definition.PrimaryKernel.Size}×{definition.PrimaryKernel.Size}；和={definition.PrimaryKernel.Sum:0.######}；绝对值和={definition.PrimaryKernel.AbsoluteSum:0.######}；{definition.Explanation}";
            }
            else
            {
                var parsed = _parser.Parse(KernelText); KernelSummary = parsed.IsSuccess ? DescribeKernel(parsed.Kernel!) : FormatParseError(parsed.Errors[0]);
            }
        }
        catch (Exception exception) { KernelSummary = exception.Message; StatusMessage = exception.Message; }
        finally { _updatingParameters = false; }
        if (markChanged) ParametersChanged(schedulePreview: true);
    }

    private bool TryBuildRecipe(out ConvolutionRecipe recipe, out string? error)
    {
        try
        {
            ConvolutionOperatorDefinition definition;
            if (SelectedPreset == "custom")
            {
                var parsed = _parser.Parse(KernelText);
                if (!parsed.IsSuccess) { recipe = null!; error = FormatParseError(parsed.Errors[0]); return false; }
                definition = _factory.Custom(parsed.Kernel!);
            }
            else definition = _factory.Create(SelectedPreset, KernelSize, Sigma, Amount, HighBoostA, MotionLength, AngleDegrees, EmbossStrength);
            recipe = new ConvolutionRecipe(definition,
                new BorderDefinition(Enum.Parse<BorderMode>(SelectedBorder), ConstantBorderValue),
                new KernelNormalizationDefinition(Enum.Parse<KernelNormalizationMode>(SelectedNormalization), ExplicitDivisor),
                Bias, Enum.Parse<ConvolutionChannelMode>(SelectedChannel), Enum.Parse<GradientOutputMode>(SelectedGradientOutput));
            recipe.Validate(); error = null; KernelSummary = DescribeKernel(definition.PrimaryKernel); return true;
        }
        catch (Exception exception) { recipe = null!; error = exception.Message; return false; }
    }

    private void ParametersChanged(bool schedulePreview)
    {
        if (_restoring || _updatingParameters) return;
        ++_generation; CancelAndDispose(ref _previewCancellation); CancelAndDispose(ref _fullCancellation); CancelAndDispose(ref _exportCancellation);
        _fullResult = null; FullResultSummary = "参数已改变，旧完整尺寸结果已过期"; OnPropertyChanged(nameof(CanExport));
        MarkChanged(); if (schedulePreview && _session is not null) _ = RenderPreviewCoreAsync(debounce: true);
    }

    partial void OnSourcePathChanged(string value) { if (!_restoring) { InvalidateSession("图片路径已改变，请显式载入。"); MarkChanged(); } }
    partial void OnSelectedPresetChanged(string value) { if (!_restoring && !_updatingParameters) ApplyPresetCore(markChanged: true); }
    partial void OnKernelSizeChanged(int value) { if (!_restoring && !_updatingParameters) ApplyPresetCore(markChanged: true); }
    partial void OnSigmaChanged(double value) { if (!_restoring && !_updatingParameters) ApplyPresetCore(markChanged: true); }
    partial void OnAmountChanged(double value) { if (!_restoring && !_updatingParameters) ApplyPresetCore(markChanged: true); }
    partial void OnHighBoostAChanged(double value) { if (!_restoring && !_updatingParameters) ApplyPresetCore(markChanged: true); }
    partial void OnMotionLengthChanged(double value) { if (!_restoring && !_updatingParameters) ApplyPresetCore(markChanged: true); }
    partial void OnAngleDegreesChanged(double value) { if (!_restoring && !_updatingParameters) ApplyPresetCore(markChanged: true); }
    partial void OnEmbossStrengthChanged(double value) { if (!_restoring && !_updatingParameters) ApplyPresetCore(markChanged: true); }
    partial void OnKernelTextChanged(string value)
    {
        if (_restoring || _updatingParameters) return;
        _updatingParameters = true; SelectedPreset = "custom"; _updatingParameters = false;
        var parsed = _parser.Parse(value); KernelSummary = parsed.IsSuccess ? DescribeKernel(parsed.Kernel!) : FormatParseError(parsed.Errors[0]);
        ParametersChanged(schedulePreview: parsed.IsSuccess);
    }
    partial void OnSelectedBorderChanged(string value) => ParametersChanged(true);
    partial void OnConstantBorderValueChanged(double value) => ParametersChanged(true);
    partial void OnSelectedNormalizationChanged(string value) => ParametersChanged(true);
    partial void OnExplicitDivisorChanged(double value) => ParametersChanged(true);
    partial void OnBiasChanged(double value) => ParametersChanged(true);
    partial void OnSelectedChannelChanged(string value) => ParametersChanged(true);
    partial void OnSelectedGradientOutputChanged(string value) => ParametersChanged(true);
    partial void OnAnalysisMaximumEdgeChanged(int value) { if (!_restoring && _session is not null) InvalidateSession("代理档位已改变，请重新载入源图。"); ParametersChanged(false); }
    partial void OnShowPhaseResponseChanged(bool value)
    {
        if (_restoring) return;
        MarkChanged(); _ = RefreshResponseProjectionAsync();
    }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsOperationBusy));
    partial void OnIsFullBusyChanged(bool value) => OnPropertyChanged(nameof(IsOperationBusy));
    partial void OnIsExportingChanged(bool value) => OnPropertyChanged(nameof(IsOperationBusy));

    private async Task<Bitmap> CreateBitmapAsync(ImageLabPlugin.Domain.Imaging.PixelImage image, CancellationToken token)
    {
        var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, writable: false); return new Bitmap(stream);
    }
    private bool CanCommit(long generation) => generation == _generation && !_lifetime.IsClosing && !_disposed;
    private void InvalidateSession(string status)
    {
        ++_generation; CancelAndDispose(ref _loadCancellation); CancelAndDispose(ref _previewCancellation); CancelAndDispose(ref _fullCancellation); CancelAndDispose(ref _exportCancellation);
        ReplaceSession(null); _preview = null; _fullResult = null; _frequencyResponse = null; ReplaceSourceBitmap(null); ReplaceResultBitmap(null);
        ReplaceDifferenceBitmap(null); ReplaceResponseBitmap(null); ResultSummary = "尚未生成代理结果"; FullResultSummary = "尚未执行完整尺寸卷积";
        StatusMessage = status; OnPropertyChanged(nameof(HasSession)); OnPropertyChanged(nameof(HasPreview)); OnPropertyChanged(nameof(CanExport));
    }
    private void ReplaceSession(ConvolutionSession? value) { var previous = _session; _session = value; previous?.Dispose(); }
    private void ReplaceSourceBitmap(Bitmap? value) { var previous = SourcePreview; SourcePreview = value; previous?.Dispose(); }
    private void ReplaceResultBitmap(Bitmap? value) { var previous = ResultPreview; ResultPreview = value; previous?.Dispose(); }
    private void ReplaceDifferenceBitmap(Bitmap? value) { var previous = DifferencePreview; DifferencePreview = value; previous?.Dispose(); }
    private void ReplaceResponseBitmap(Bitmap? value) { var previous = ResponsePreview; ResponsePreview = value; previous?.Dispose(); }
    private async Task RefreshResponseProjectionAsync()
    {
        var response = _frequencyResponse; if (response is null || _lifetime.IsClosing) return;
        var generation = ++_responseProjectionGeneration;
        try
        {
            var bitmap = await CreateBitmapAsync(ShowPhaseResponse ? response.PhaseImage : response.MagnitudeImage, _lifetime.ClosingToken).ConfigureAwait(true);
            if (generation != _responseProjectionGeneration || _lifetime.IsClosing || _disposed) { bitmap.Dispose(); return; }
            ReplaceResponseBitmap(bitmap);
        }
        catch (OperationCanceledException) when (_lifetime.ClosingToken.IsCancellationRequested) { }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }
    private void MarkChanged() { if (_restoring) return; var wasDirty = IsDirty; _revision++; if (wasDirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }
    private static void CancelAndDispose(ref CancellationTokenSource? source) { source?.Cancel(); source?.Dispose(); source = null; }
    private static string DescribeKernel(ConvolutionKernel kernel) => $"{kernel.Size}×{kernel.Size} 中心锚点；和={kernel.Sum:0.######}；绝对值和={kernel.AbsoluteSum:0.######}；真卷积读取 f(x-kx,y-ky)。";
    private static string FormatParseError(KernelParseError error) => $"核矩阵第 {error.Row} 行第 {error.Column} 列：{error.Reason}";

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema) { StatusMessage = $"不支持 schema {content.SchemaVersion}，已使用安全的 3×3 Gaussian 默认值。"; return; }
        try
        {
            var value = content.Payload.Deserialize<Snapshot>(); if (value is null || value.Convention != "true-convolution-v1") return;
            SourcePath = value.SourcePath ?? string.Empty; KernelSize = value.KernelSize is >= 3 and <= 31 && (value.KernelSize & 1) == 1 ? value.KernelSize : 3;
            Sigma = value.Sigma is >= 0.1 and <= 5 ? value.Sigma : 1; Amount = value.Amount is >= 0 and <= 5 ? value.Amount : 1;
            HighBoostA = value.HighBoostA is >= 1 and <= 6 ? value.HighBoostA : 2; MotionLength = value.MotionLength is >= 1 and <= 31 ? value.MotionLength : 3;
            AngleDegrees = double.IsFinite(value.AngleDegrees) ? value.AngleDegrees : 0; EmbossStrength = value.EmbossStrength is >= 0 and <= 5 ? value.EmbossStrength : 1;
            SelectedPreset = PresetOptions.Contains(value.Preset) ? value.Preset : "custom";
            if (value.CustomCoefficients.Length == checked(value.CustomSize * value.CustomSize)) KernelText = _parser.Format(new ConvolutionKernel(value.CustomSize, value.CustomCoefficients));
            SelectedBorder = BorderOptions.Contains(value.Border) ? value.Border : "Reflect101"; ConstantBorderValue = double.IsFinite(value.ConstantBorder) ? value.ConstantBorder : 0;
            SelectedNormalization = NormalizationOptions.Contains(value.Normalization) ? value.Normalization : "KernelSum"; ExplicitDivisor = double.IsFinite(value.ExplicitDivisor) ? value.ExplicitDivisor : 1;
            Bias = double.IsFinite(value.Bias) ? value.Bias : 0; SelectedChannel = ChannelOptions.Contains(value.Channel) ? value.Channel : "Rgb";
            SelectedGradientOutput = GradientOutputOptions.Contains(value.GradientOutput) ? value.GradientOutput : "Magnitude";
            AnalysisMaximumEdge = AnalysisEdgeOptions.Contains(value.AnalysisEdge) ? value.AnalysisEdge : 1024; ShowPhaseResponse = value.ShowPhase;
            ProbeX = Math.Max(0, value.ProbeX); ProbeY = Math.Max(0, value.ProbeY); ApplyPresetCore(false);
            StatusMessage = File.Exists(SourcePath) ? "已恢复轻量参数；请显式载入图片，不会自动卷积。" : "已恢复参数，但源图片不存在，请重新选择。";
        }
        catch (Exception exception) { StatusMessage = $"快照无效，已保留安全默认值：{exception.Message}"; ApplyPresetCore(false); }
    }

    private sealed record Snapshot(string? SourcePath, string Preset, int KernelSize, double Sigma, double Amount,
        double HighBoostA, double MotionLength, double AngleDegrees, double EmbossStrength, int CustomSize,
        double[] CustomCoefficients, string Border, double ConstantBorder, string Normalization, double ExplicitDivisor,
        double Bias, string Channel, string GradientOutput, int AnalysisEdge, bool ShowPhase, int ProbeX, int ProbeY, string Convention);
}
