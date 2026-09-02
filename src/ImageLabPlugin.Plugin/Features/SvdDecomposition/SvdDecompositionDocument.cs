using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.SvdDecomposition;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.SvdDecomposition;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.SvdDecomposition;

/// <summary>SVD Decomposition 的多实例 Document：只管理用户意图、代次、取消、快照与 Bitmap。</summary>
/// <remarks>
/// Jacobi、矩阵重建、能量、颜色和序列化全部委托给窄用例。source generation 防止旧源图提交，
/// projection generation 为 Rank/分量变化提供 latest-wins；即使底层任务较晚返回，也不能覆盖新参数。
/// 每个 DI Scope 拥有独立 Session、缓存、取消源和 Bitmap，关闭后释放并阻断结果提交。
/// </remarks>
internal sealed partial class SvdDecompositionDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private readonly IPrepareSvdSessionUseCase _prepare;
    private readonly IDecomposeSvdUseCase _decompose;
    private readonly IReconstructSvdRankUseCase _reconstruct;
    private readonly IProjectSvdComponentUseCase _projectComponent;
    private readonly ICompareSvdStrategiesUseCase _compare;
    private readonly IExportSvdImageUseCase _exportImage;
    private readonly IExportSvdReportUseCase _exportReport;
    private readonly IImageFileDialog _imageDialog;
    private readonly ISvdFileDialog _svdDialog;
    private readonly IImageCodec _codec;
    private readonly IDocumentLifetime _lifetime;
    private DocumentPresentationState _presentation = new("奇异值分解重建");
    private SvdSession? _session;
    private SvdDecompositionSet? _decomposition;
    private SvdRankResult? _rankResult;
    private SvdComponentProjection? _component;
    private SvdStrategyComparison? _comparison;
    private CancellationTokenSource? _sourceCancellation;
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _projectionCancellation;
    private long _sourceGeneration;
    private long _projectionGeneration;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;
    private bool _disposed;

    public SvdDecompositionDocument(
        IPrepareSvdSessionUseCase prepare,
        IDecomposeSvdUseCase decompose,
        IReconstructSvdRankUseCase reconstruct,
        IProjectSvdComponentUseCase projectComponent,
        ICompareSvdStrategiesUseCase compare,
        IExportSvdImageUseCase exportImage,
        IExportSvdReportUseCase exportReport,
        IImageFileDialog imageDialog,
        ISvdFileDialog svdDialog,
        IImageCodec codec,
        IDocumentLifetime lifetime)
    {
        _prepare = prepare;
        _decompose = decompose;
        _reconstruct = reconstruct;
        _projectComponent = projectComponent;
        _compare = compare;
        _exportImage = exportImage;
        _exportReport = exportReport;
        _imageDialog = imageDialog;
        _svdDialog = svdDialog;
        _codec = codec;
        _lifetime = lifetime;
    }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private int _analysisMaximumEdge = 128;
    [ObservableProperty] private string _selectedStrategy = "单通道";
    [ObservableProperty] private string _selectedChannel = "Y";
    [ObservableProperty] private int _rank;
    [ObservableProperty] private int _rankMaximum = 1;
    [ObservableProperty] private int _componentIndex;
    [ObservableProperty] private int _componentMaximum;
    [ObservableProperty] private int _selectedFactorChannelIndex;
    [ObservableProperty] private bool _showLogSingularValues = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "请选择 PNG/JPEG 图片并建立 128 或 256 最大边的分析代理。";
    [ObservableProperty] private string _sessionSummary = "尚未载入";
    [ObservableProperty] private string _decompositionSummary = "尚未分解";
    [ObservableProperty] private string _rankSummary = "k=0；尚无重建结果";
    [ObservableProperty] private string _componentSummary = "尚无分量";
    [ObservableProperty] private IReadOnlyList<string> _factorChannelOptions = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<double> _singularValues = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<double> _cumulativeEnergy = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<string> _comparisonRows = Array.Empty<string>();
    [ObservableProperty] private Bitmap? _sourcePreview;
    [ObservableProperty] private Bitmap? _resultPreview;
    [ObservableProperty] private Bitmap? _componentPreview;

    public IReadOnlyList<int> AnalysisEdgeOptions { get; } = [128, 256];
    public IReadOnlyList<string> StrategyOptions { get; } = ["单通道", "RGB 独立", "YCbCr 独立"];
    public IReadOnlyList<string> ChannelOptions { get; } = ["R", "G", "B", "Y", "Cb", "Cr"];
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasSession => _session is not null;
    public bool HasDecomposition => _decomposition is not null;
    public bool HasCurrentResult => _rankResult is not null && _session is not null &&
        StringComparer.Ordinal.Equals(_rankResult.RecipeFingerprint, CurrentRecipeFingerprint());

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
            _presentation = new(string.IsNullOrWhiteSpace(activation.Title) ? "奇异值分解重建" : activation.Title);
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
    private async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || !File.Exists(SourcePath))
        { StatusMessage = "请选择存在的 PNG 或 JPEG 图片。"; return; }
        InvalidateSource("正在解码源图并建立抗混叠分析代理…");
        _sourceCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _sourceCancellation;
        var token = current.Token;
        var generation = ++_sourceGeneration;
        IsBusy = true;
        try
        {
            var session = await _prepare.ExecuteAsync(SourcePath, AnalysisMaximumEdge, token).ConfigureAwait(true);
            var bitmap = await CreateBitmapAsync(session.AnalysisProxy, token).ConfigureAwait(true);
            if (!CanCommitSource(generation)) { session.Dispose(); bitmap.Dispose(); return; }
            ReplaceSession(session);
            ReplaceSourcePreview(bitmap);
            RankMaximum = Math.Min(session.AnalysisProxy.Size.Width, session.AnalysisProxy.Size.Height);
            Rank = Math.Clamp(Rank, 0, RankMaximum);
            ComponentMaximum = Math.Max(0, RankMaximum - 1);
            ComponentIndex = Math.Clamp(ComponentIndex, 0, ComponentMaximum);
            SessionSummary = $"原图 {session.SourceImage.Size.Width}×{session.SourceImage.Size.Height}；" +
                $"分析代理 {session.AnalysisProxy.Size.Width}×{session.AnalysisProxy.Size.Height}；最大边档位 {session.AnalysisMaximumEdge}";
            StatusMessage = "分析代理已建立；载图不会自动分解，请确认策略后点击“开始分解”。";
            OnPropertyChanged(nameof(HasSession));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { if (!_lifetime.IsClosing && generation == _sourceGeneration) StatusMessage = "载入已取消。"; }
        catch (Exception exception) { if (generation == _sourceGeneration) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _sourceCancellation)) IsBusy = false; }
    }

    [RelayCommand]
    private async Task DecomposeAsync()
    {
        var session = _session;
        if (session is null) { StatusMessage = "请先载入图片并建立分析代理。"; return; }
        CancelAndDispose(ref _operationCancellation);
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _operationCancellation;
        var token = current.Token;
        IsBusy = true;
        StatusMessage = "正在后台执行有界单边 Jacobi SVD；Rank 变化不会重新分解。";
        try
        {
            var decomposition = await _decompose.ExecuteAsync(session, ResolveStrategy(), ResolveChannel(), token).ConfigureAwait(true);
            if (!ReferenceEquals(session, _session) || _disposed || _lifetime.IsClosing) return;
            _decomposition = decomposition;
            FactorChannelOptions = decomposition.Channels.Select(item => DisplayChannel(item.Channel)).ToArray();
            SelectedFactorChannelIndex = Math.Clamp(SelectedFactorChannelIndex, 0, decomposition.Channels.Count - 1);
            RefreshCurve();
            var diagnostics = decomposition.Channels.Select(item => item.Factors.Diagnostics).ToArray();
            DecompositionSummary = $"{decomposition.Channels.Count} 个矩阵；数值秩 " +
                string.Join(" / ", decomposition.Channels.Select(item => new SingularValueEnergyAnalyzer().Analyze(item.Factors).NumericRank)) +
                $"；sweep {string.Join(" / ", diagnostics.Select(item => item.Sweeps))}；全部收敛";
            OnPropertyChanged(nameof(HasDecomposition));
            await ReconstructAndProjectAsync(debounce: false).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { if (!_lifetime.IsClosing && ReferenceEquals(session, _session)) StatusMessage = "分解已取消；未缓存未完成结果。"; }
        catch (Exception exception) { if (ReferenceEquals(session, _session)) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _operationCancellation)) IsBusy = false; }
    }

    private async Task ReconstructAndProjectAsync(bool debounce)
    {
        var session = _session;
        var decomposition = _decomposition;
        if (session is null || decomposition is null) return;
        CancelAndDispose(ref _projectionCancellation);
        _projectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _projectionCancellation;
        var token = current.Token;
        var generation = ++_projectionGeneration;
        try
        {
            if (debounce) await Task.Delay(100, token).ConfigureAwait(true);
            var rank = Math.Clamp(Rank, 0, RankMaximum);
            var componentIndex = Math.Clamp(ComponentIndex, 0, ComponentMaximum);
            var factorIndex = Math.Clamp(SelectedFactorChannelIndex, 0, decomposition.Channels.Count - 1);
            var result = await _reconstruct.ExecuteAsync(session, decomposition, rank, token).ConfigureAwait(true);
            var component = await _projectComponent.ExecuteAsync(decomposition, factorIndex, componentIndex, token).ConfigureAwait(true);
            var resultBitmap = await CreateBitmapAsync(result.Image, token).ConfigureAwait(true);
            Bitmap? componentBitmap = null;
            try { componentBitmap = await CreateBitmapAsync(component.Preview, token).ConfigureAwait(true); }
            catch { resultBitmap.Dispose(); throw; }
            if (!CanCommitProjection(session, decomposition, generation))
            { resultBitmap.Dispose(); componentBitmap.Dispose(); return; }
            _rankResult = result;
            _component = component;
            ReplaceResultPreview(resultBitmap);
            ReplaceComponentPreview(componentBitmap);
            var psnr = double.IsPositiveInfinity(result.Quality.PsnrRgbDb) ? "∞（像素误差为 0）" : $"{result.Quality.PsnrRgbDb:F2} dB";
            var maximumTheoretical = result.MatrixErrors.Max(item => item.TheoreticalFrobeniusError);
            var maximumDirect = result.MatrixErrors.Max(item => item.DirectFrobeniusError);
            var maximumRelative = result.MatrixErrors.Max(item => item.RelativeFrobeniusError);
            RankSummary = $"k={rank}；聚合保留能量 {FormatPercent(result.AggregateRetainedEnergy)}；" +
                $"矩阵理论/直接误差上限 {maximumTheoretical:G5}/{maximumDirect:G5}（相对 {maximumRelative:P3}）；" +
                $"RGB RMSE {result.Quality.RootMeanSquareErrorRgb:F4}；PSNR-RGB {psnr}；SSIM-Y {result.Quality.GlobalSsimLuma:F6}；" +
                $"裁切像素 {result.Clipping.ClippedPixels:N0}";
            ComponentSummary = $"{DisplayChannel(component.Channel)} 第 {component.ComponentIndex + 1} 项；" +
                $"σ={component.SingularValue:G6}；能量 {FormatPercent(component.EnergyShare)}；" +
                $"raw [{component.RawMinimum:G5}, {component.RawMaximum:G5}]；对称显示尺度 {component.DisplayScale:G5}";
            StatusMessage = $"Rank-k 与分量投影已更新；结果尺寸 {result.Image.Size.Width}×{result.Image.Size.Height}，仅代表分析代理。";
            OnPropertyChanged(nameof(HasCurrentResult));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { if (generation == _projectionGeneration) StatusMessage = exception.Message; }
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        var session = _session;
        if (session is null) { StatusMessage = "请先载入分析代理。"; return; }
        CancelAndDispose(ref _operationCancellation);
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _operationCancellation;
        var token = current.Token;
        IsBusy = true;
        StatusMessage = "正在按 Y、RGB、YCbCr 固定顺序串行比较…";
        try
        {
            var comparison = await _compare.ExecuteAsync(session, Rank, token).ConfigureAwait(true);
            if (!ReferenceEquals(session, _session)) return;
            _comparison = comparison;
            ComparisonRows = comparison.Cases.Select(item =>
                $"{DisplayStrategy(item.Strategy),-10} | 矩阵 {item.MatrixCount} | k={item.CommonRank} | 能量 {FormatPercent(item.RetainedEnergy),8} | " +
                $"PSNR-RGB {(double.IsPositiveInfinity(item.Quality.PsnrRgbDb) ? "∞" : item.Quality.PsnrRgbDb.ToString("F2"))} | SSIM-Y {item.Quality.GlobalSsimLuma:F5}").ToArray();
            StatusMessage = comparison.CompletionStatus == SvdComparisonCompletionStatus.Complete
                ? "固定三策略比较完成；表格保持产品顺序，不自动宣布最佳策略。"
                : "比较已取消；仅保留取消前完成的有序案例。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { if (ReferenceEquals(session, _session)) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _operationCancellation)) IsBusy = false; }
    }

    [RelayCommand] private void Cancel() => CancelAllOperations();

    [RelayCommand]
    private async Task ExportImageAsync()
    {
        var session = _session;
        var result = _rankResult;
        if (session is null || result is null || !HasCurrentResult)
        { StatusMessage = "当前 Rank 结果不存在或已过期，禁止导出。"; return; }
        var path = await _svdDialog.PickProxyPngOutputAsync(
            $"{Path.GetFileNameWithoutExtension(SourcePath)}.svd-proxy-k{Rank}.png", _lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!ReferenceEquals(session, _session) || !ReferenceEquals(result, _rankResult) || !HasCurrentResult)
        { StatusMessage = "选择输出路径期间结果已改变，请重新导出当前 Rank。"; return; }
        CancelAndDispose(ref _operationCancellation);
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _operationCancellation;
        var token = current.Token;
        IsBusy = true;
        try
        {
            await _exportImage.ExecuteAsync(session, result, CurrentRecipeFingerprint(), path, token).ConfigureAwait(true);
            StatusMessage = $"已导出分析代理 PNG：{result.Image.Size.Width}×{result.Image.Size.Height}；它不是压缩图片。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { if (!_lifetime.IsClosing && ReferenceEquals(session, _session)) StatusMessage = "PNG 导出已取消；未报告成功。"; }
        catch (Exception exception) { if (ReferenceEquals(session, _session)) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _operationCancellation)) IsBusy = false; }
    }

    [RelayCommand] private Task ExportJsonAsync() => ExportReportCoreAsync(csv: false);
    [RelayCommand] private Task ExportCsvAsync() => ExportReportCoreAsync(csv: true);

    private async Task ExportReportCoreAsync(bool csv)
    {
        var session = _session;
        var decomposition = _decomposition;
        var rankResult = _rankResult;
        if (session is null || decomposition is null || rankResult is null || !HasCurrentResult)
        { StatusMessage = "当前分解或 Rank 结果已过期，禁止导出报告。"; return; }
        var suggested = $"{Path.GetFileNameWithoutExtension(SourcePath)}.svd-report.{(csv ? "csv" : "json")}";
        var path = csv
            ? await _svdDialog.PickSvdCsvOutputAsync(suggested, _lifetime.ClosingToken).ConfigureAwait(true)
            : await _svdDialog.PickSvdJsonOutputAsync(suggested, _lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!ReferenceEquals(session, _session) || !ReferenceEquals(decomposition, _decomposition) ||
            !ReferenceEquals(rankResult, _rankResult) || !HasCurrentResult)
        { StatusMessage = "选择输出路径期间结果已改变，请重新导出当前报告。"; return; }
        var report = CreateReport();
        CancelAndDispose(ref _operationCancellation);
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _operationCancellation;
        var token = current.Token;
        IsBusy = true;
        try
        {
            await _exportReport.ExecuteAsync(report, path, csv, token).ConfigureAwait(true);
            StatusMessage = $"已导出 {(csv ? "CSV" : "JSON")} 实验报告；报告明确标注分析代理和非压缩器边界。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { if (!_lifetime.IsClosing && ReferenceEquals(session, _session)) StatusMessage = "报告导出已取消；未报告成功。"; }
        catch (Exception exception) { if (ReferenceEquals(session, _session)) StatusMessage = exception.Message; }
        finally { if (ReferenceEquals(current, _operationCancellation)) IsBusy = false; }
    }

    internal void SelectCurvePoint(int index)
    {
        if (_decomposition is null) return;
        ComponentIndex = Math.Clamp(index, 0, ComponentMaximum);
        Rank = Math.Clamp(index + 1, 0, RankMaximum);
    }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.SerializeToElement(new Snapshot(SourcePath, AnalysisMaximumEdge,
            SelectedStrategy, SelectedChannel, Rank, ComponentIndex, SelectedFactorChannelIndex,
            ShowLogSingularValues, SvdRecipeFingerprint.NumericProtocol));
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
        ++_sourceGeneration; ++_projectionGeneration;
        CancelAllOperations();
        ReplaceSession(null);
        ReplaceSourcePreview(null);
        ReplaceResultPreview(null);
        ReplaceComponentPreview(null);
    }

    partial void OnSourcePathChanged(string value) { if (!_restoring) { InvalidateSource("源图路径已改变，请重新载入。", clearPath: false); MarkChanged(); } }
    partial void OnAnalysisMaximumEdgeChanged(int value)
    {
        if (!AnalysisEdgeOptions.Contains(value)) { AnalysisMaximumEdge = 128; return; }
        if (!_restoring) { InvalidateSource("代理档位已改变，请重新载入。", clearPath: false); MarkChanged(); }
    }
    partial void OnSelectedStrategyChanged(string value) { if (!_restoring) { InvalidateRecipe(); MarkChanged(); } }
    partial void OnSelectedChannelChanged(string value) { if (!_restoring) { InvalidateRecipe(); MarkChanged(); } }
    partial void OnRankChanged(int value)
    {
        if (_restoring) return;
        if (value < 0 || value > RankMaximum) { Rank = Math.Clamp(value, 0, RankMaximum); return; }
        _comparison = null; ComparisonRows = [];
        MarkChanged(); OnPropertyChanged(nameof(HasCurrentResult)); _ = ReconstructAndProjectAsync(debounce: true);
    }
    partial void OnComponentIndexChanged(int value)
    {
        if (_restoring) return;
        if (value < 0 || value > ComponentMaximum) { ComponentIndex = Math.Clamp(value, 0, ComponentMaximum); return; }
        MarkChanged(); _ = ReconstructAndProjectAsync(debounce: true);
    }
    partial void OnSelectedFactorChannelIndexChanged(int value)
    { if (!_restoring) { RefreshCurve(); MarkChanged(); _ = ReconstructAndProjectAsync(debounce: true); } }
    partial void OnShowLogSingularValuesChanged(bool value) { if (!_restoring) MarkChanged(); }

    private void RefreshCurve()
    {
        if (_decomposition is null || _decomposition.Channels.Count == 0)
        { SingularValues = []; CumulativeEnergy = []; return; }
        var index = Math.Clamp(SelectedFactorChannelIndex, 0, _decomposition.Channels.Count - 1);
        var factors = _decomposition.Channels[index].Factors;
        var energy = new SingularValueEnergyAnalyzer().Analyze(factors);
        SingularValues = factors.SingularValues.ToArray();
        CumulativeEnergy = energy.Samples.Select(item => item.CumulativeEnergy).ToArray();
    }

    private SvdExperimentReport CreateReport()
    {
        var session = _session!;
        return new("image-lab.svd-report/1", SvdRecipeFingerprint.NumericProtocol, session.SourcePath,
            session.SourceImage.Size, session.AnalysisProxy.Size, session.AnalysisMaximumEdge, _decomposition!, _rankResult!,
            _component, _comparison,
            ["所有图片指标只比较当前分析代理。", "本工具解释低秩近似，不是图片文件压缩器。",
             "重复或近似相等奇异值的单分量方向不唯一；相应子空间重建才是稳定事实。"], DateTimeOffset.UtcNow);
    }

    private void InvalidateRecipe()
    {
        CancelAndDispose(ref _operationCancellation);
        CancelAndDispose(ref _projectionCancellation);
        ++_projectionGeneration;
        _decomposition = null; _rankResult = null; _component = null; _comparison = null;
        DecompositionSummary = "策略已改变；请开始分解（已完成策略可从 Session 缓存命中）。";
        RankSummary = "当前结果已过期"; ComponentSummary = "当前结果已过期";
        ComparisonRows = []; FactorChannelOptions = []; SingularValues = []; CumulativeEnergy = [];
        ReplaceResultPreview(null);
        ReplaceComponentPreview(null);
        OnPropertyChanged(nameof(HasDecomposition)); OnPropertyChanged(nameof(HasCurrentResult));
    }

    private void InvalidateSource(string status, bool clearPath = false)
    {
        ++_sourceGeneration; ++_projectionGeneration;
        CancelAllOperations();
        ReplaceSession(null);
        _decomposition = null; _rankResult = null; _component = null; _comparison = null;
        if (clearPath) SourcePath = string.Empty;
        ReplaceSourcePreview(null);
        ReplaceResultPreview(null);
        ReplaceComponentPreview(null);
        SessionSummary = "尚未载入"; DecompositionSummary = "尚未分解";
        RankSummary = "k=0；尚无重建结果"; ComponentSummary = "尚无分量";
        ComparisonRows = []; FactorChannelOptions = []; SingularValues = []; CumulativeEnergy = [];
        StatusMessage = status;
        OnPropertyChanged(nameof(HasSession)); OnPropertyChanged(nameof(HasDecomposition)); OnPropertyChanged(nameof(HasCurrentResult));
    }

    private string CurrentRecipeFingerprint() => _session is null ? string.Empty :
        SvdRecipeFingerprint.Create(_session.ProxyFingerprint, ResolveStrategy(), ResolveChannel(), Rank);

    private bool CanCommitSource(long generation) => generation == _sourceGeneration && !_disposed && !_lifetime.IsClosing;
    private bool CanCommitProjection(SvdSession session, SvdDecompositionSet decomposition, long generation) =>
        generation == _projectionGeneration && ReferenceEquals(session, _session) && ReferenceEquals(decomposition, _decomposition) && !_disposed && !_lifetime.IsClosing;

    private async Task<Bitmap> CreateBitmapAsync(PixelImage image, CancellationToken token)
    {
        var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, writable: false);
        return new Bitmap(stream);
    }

    private void ReplaceSession(SvdSession? replacement) { var previous = _session; _session = replacement; previous?.Dispose(); }
    private void ReplaceSourcePreview(Bitmap? replacement)
    { var previous = SourcePreview; SourcePreview = replacement; previous?.Dispose(); }
    private void ReplaceResultPreview(Bitmap? replacement)
    { var previous = ResultPreview; ResultPreview = replacement; previous?.Dispose(); }
    private void ReplaceComponentPreview(Bitmap? replacement)
    { var previous = ComponentPreview; ComponentPreview = replacement; previous?.Dispose(); }
    private void CancelAllOperations()
    { CancelAndDispose(ref _sourceCancellation); CancelAndDispose(ref _operationCancellation); CancelAndDispose(ref _projectionCancellation); }
    private static void CancelAndDispose(ref CancellationTokenSource? source)
    { source?.Cancel(); source?.Dispose(); source = null; }

    private void MarkChanged()
    {
        if (_restoring) return;
        var wasDirty = IsDirty; _revision++;
        if (wasDirty != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema)
        { StatusMessage = $"不支持 schema {content.SchemaVersion}，已使用安全默认值。"; return; }
        var value = content.Payload.Deserialize<Snapshot>();
        if (value is null) return;
        SourcePath = value.SourcePath ?? string.Empty;
        AnalysisMaximumEdge = AnalysisEdgeOptions.Contains(value.AnalysisMaximumEdge) ? value.AnalysisMaximumEdge : 128;
        SelectedStrategy = StrategyOptions.Contains(value.Strategy) ? value.Strategy : "单通道";
        SelectedChannel = ChannelOptions.Contains(value.Channel) ? value.Channel : "Y";
        Rank = Math.Max(0, value.Rank); ComponentIndex = Math.Max(0, value.ComponentIndex);
        SelectedFactorChannelIndex = Math.Max(0, value.FactorChannelIndex);
        ShowLogSingularValues = value.ShowLogSingularValues;
        StatusMessage = File.Exists(SourcePath)
            ? "已恢复路径和轻量参数；请显式重新载入并分解。"
            : "已恢复轻量参数，但源图片不存在，请重新选择。";
    }

    private SvdColorStrategy ResolveStrategy() => SelectedStrategy switch
    { "RGB 独立" => SvdColorStrategy.IndependentRgb, "YCbCr 独立" => SvdColorStrategy.IndependentYCbCr, _ => SvdColorStrategy.SingleChannel };
    private ImageChannel ResolveChannel() => SelectedChannel switch
    { "R" => ImageChannel.Red, "G" => ImageChannel.Green, "B" => ImageChannel.Blue, "Cb" => ImageChannel.ChromaBlue, "Cr" => ImageChannel.ChromaRed, _ => ImageChannel.Luma };
    private static string DisplayChannel(ImageChannel channel) => channel switch
    { ImageChannel.Red => "R", ImageChannel.Green => "G", ImageChannel.Blue => "B", ImageChannel.Luma => "Y", ImageChannel.ChromaBlue => "Cb", ImageChannel.ChromaRed => "Cr", _ => channel.ToString() };
    private static string DisplayStrategy(SvdColorStrategy strategy) => strategy switch
    { SvdColorStrategy.SingleChannel => "Y 单通道", SvdColorStrategy.IndependentRgb => "RGB 独立", SvdColorStrategy.IndependentYCbCr => "YCbCr 独立", _ => strategy.ToString() };
    private static string FormatPercent(double? value) => value is null ? "不适用" : $"{value:P2}";

    private sealed record Snapshot(string? SourcePath, int AnalysisMaximumEdge, string Strategy, string Channel,
        int Rank, int ComponentIndex, int FactorChannelIndex, bool ShowLogSingularValues, string NumericProtocol);
}
