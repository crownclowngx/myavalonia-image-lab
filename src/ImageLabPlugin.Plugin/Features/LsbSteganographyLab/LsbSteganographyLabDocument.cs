using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.LsbSteganography;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Steganography;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.LsbSteganographyLab;

/// <summary>LSB 教学实验 Document：管理配方、Session、generation、取消、快照和 Bitmap 所有权。</summary>
/// <remarks>
/// Document 不执行 Frame 拼装、槽位生成、像素位运算、统计公式、扰动或文件写入。配方改变会立即使结果过期；
/// 每次异步提交都核对 generation、会话引用和 ClosingToken，底层迟到结果不能覆盖新状态。
/// </remarks>
internal sealed partial class LsbSteganographyLabDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private readonly IPrepareLsbExperimentUseCase _prepare;
    private readonly IEstimateLsbCapacityUseCase _estimate;
    private readonly IEmbedAndAnalyzeLsbUseCase _embed;
    private readonly ILoadLsbPayloadUseCase _loadPayload;
    private readonly IRunLsbFragilityUseCase _fragility;
    private readonly IInspectLsbPixelUseCase _inspectPixel;
    private readonly IExportLsbImageUseCase _exportImage;
    private readonly IExportLsbReportUseCase _exportReport;
    private readonly IImageFileDialog _imageDialog;
    private readonly IPayloadFileDialog _payloadDialog;
    private readonly ILsbReportFileDialog _reportDialog;
    private readonly IImageCodec _codec;
    private readonly IDocumentLifetime _lifetime;
    private DocumentPresentationState _presentation = new("LSB 隐写与统计实验");
    private LsbExperimentSession? _session;
    private LsbPayload? _binaryPayload;
    private CancellationTokenSource? _operationCancellation;
    private long _generation;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;
    private bool _disposed;

    public LsbSteganographyLabDocument(
        IPrepareLsbExperimentUseCase prepare,
        IEstimateLsbCapacityUseCase estimate,
        IEmbedAndAnalyzeLsbUseCase embed,
        ILoadLsbPayloadUseCase loadPayload,
        IInspectLsbPixelUseCase inspectPixel,
        IRunLsbFragilityUseCase fragility,
        IExportLsbImageUseCase exportImage,
        IExportLsbReportUseCase exportReport,
        IImageFileDialog imageDialog,
        IPayloadFileDialog payloadDialog,
        ILsbReportFileDialog reportDialog,
        IImageCodec codec,
        IDocumentLifetime lifetime)
    {
        _prepare = prepare; _estimate = estimate; _embed = embed; _loadPayload = loadPayload; _inspectPixel = inspectPixel; _fragility = fragility;
        _exportImage = exportImage; _exportReport = exportReport; _imageDialog = imageDialog; _payloadDialog = payloadDialog;
        _reportDialog = reportDialog; _codec = codec; _lifetime = lifetime;
    }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _payloadKind = "文本";
    [ObservableProperty] private string _payloadText = string.Empty;
    [ObservableProperty] private string _binaryPayloadPath = string.Empty;
    [ObservableProperty] private string _selectedChannel = "RGB";
    [ObservableProperty] private int _bitPlane;
    [ObservableProperty] private string _placement = "顺序";
    [ObservableProperty] private string _seedText = "1";
    [ObservableProperty] private string _statisticsScope = "全图可用槽位";
    [ObservableProperty] private string _selectedFragility = "JPEG 95";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "请选择 PNG/JPEG 载体；透明与半透明像素不会成为槽位。";
    [ObservableProperty] private string _capacitySummary = "尚未预检";
    [ObservableProperty] private string _frameSummary = "尚未写入";
    [ObservableProperty] private string _statisticsSummary = "尚无统计结果";
    [ObservableProperty] private string _adjacencySummary = "尚无邻接结果";
    [ObservableProperty] private string _fragilitySummary = "尚未运行受控扰动";
    [ObservableProperty] private int _probeX;
    [ObservableProperty] private int _probeY;
    [ObservableProperty] private string _probeSummary = "写入后输入原图坐标，查看 RGBA、Frame 区域、通道 bit 与字节差。";
    private Bitmap? _coverPreview;
    private Bitmap? _stegoPreview;
    private Bitmap? _placementPreview;
    private Bitmap? _bitBeforePreview;
    private Bitmap? _bitAfterPreview;
    private Bitmap? _attackPreview;

    public Bitmap? CoverPreview => _coverPreview;
    public Bitmap? StegoPreview => _stegoPreview;
    public Bitmap? PlacementPreview => _placementPreview;
    public Bitmap? BitBeforePreview => _bitBeforePreview;
    public Bitmap? BitAfterPreview => _bitAfterPreview;
    public Bitmap? AttackPreview => _attackPreview;

    public IReadOnlyList<string> PayloadKindOptions { get; } = ["文本", "二进制"];
    public IReadOnlyList<string> ChannelOptions { get; } = ["R", "G", "B", "RGB"];
    public IReadOnlyList<int> BitOptions { get; } = [0, 1];
    public IReadOnlyList<string> PlacementOptions { get; } = ["顺序", "伪随机"];
    public IReadOnlyList<string> StatisticsScopeOptions { get; } = ["全图可用槽位", "本次选择槽位", "顺序连续前缀"];
    public IReadOnlyList<string> FragilityOptions { get; } = ["JPEG 95", "JPEG 80", "JPEG 60", "缩放 75% 往返", "缩放 50% 往返", "高斯轻度", "高斯中度", "中值 3×3"];
    public string PrimaryNotice => LsbSteganographyHelpCatalog.PrimaryNotice;
    public string SeedNotice => LsbSteganographyHelpCatalog.SeedNotice;
    public string StatisticsNotice => LsbSteganographyHelpCatalog.StatisticsNotice;
    public string CrcNotice => LsbSteganographyHelpCatalog.CrcNotice;
    public bool HasCarrier => _session is not null;
    public bool HasVerifiedResult => _session?.HasVerifiedStego == true;
    public bool IsTextPayload => PayloadKind == "文本";
    public bool IsBinaryPayload => !IsTextPayload;
    public bool IsPseudoRandom => Placement == "伪随机";
    public bool IsDirty => _revision != _acceptedRevision;
    public DocumentPresentationState Presentation => _presentation;

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
            _presentation = new(string.IsNullOrWhiteSpace(activation.Title) ? "LSB 隐写与统计实验" : activation.Title);
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
    private async Task SelectBinaryPayloadAsync()
    {
        var path = await _payloadDialog.PickPayloadAsync(_lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var payload = await _loadPayload.ExecuteAsync(path, _lifetime.ClosingToken).ConfigureAwait(true);
            ReplaceBinaryPayload(payload); BinaryPayloadPath = path; PayloadKind = "二进制"; UpdateCapacity();
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand]
    private async Task PrepareAsync()
    {
        if (string.IsNullOrWhiteSpace(SourcePath)) { StatusMessage = "请先选择 PNG/JPEG 图片。"; return; }
        var recipe = TryResolveRecipe(); if (recipe is null) return;
        var generation = BeginOperation("正在解码 RGBA8888 并扫描 Alpha=255 可用像素…");
        var token = _operationCancellation!.Token;
        try
        {
            var prepared = await _prepare.ExecuteAsync(SourcePath, recipe.Value, token).ConfigureAwait(true);
            if (!CanCommit(generation)) { prepared.Session.Dispose(); return; }
            ReplaceSession(prepared.Session);
            ReplaceBitmap(ref _coverPreview, await CreateBitmapAsync(prepared.Session.SourceImage, token).ConfigureAwait(true), nameof(CoverPreview));
            CapacitySummary = FormatCapacity(prepared.EmptyPayloadCapacity);
            StatusMessage = $"载体已准备：{prepared.Session.SourceImage.Size.Width}×{prepared.Session.SourceImage.Size.Height}；不透明像素 {prepared.Session.Layout.OpaquePixelCount:N0}。请确认 Payload 后写入。";
            OnPropertyChanged(nameof(HasCarrier));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { if (CanCommit(generation)) StatusMessage = "载体准备已取消。"; }
        catch (Exception exception) { if (CanCommit(generation)) StatusMessage = exception.Message; }
        finally { EndOperation(generation); }
    }

    [RelayCommand]
    private async Task EmbedAsync()
    {
        var session = _session;
        if (session is null) { StatusMessage = "请先准备载体并完成容量预检。"; return; }
        var recipe = TryResolveRecipe(); if (recipe is null) return;
        LsbPayload? temporary = null;
        try
        {
            var payload = IsTextPayload ? temporary = LsbPayload.FromText(PayloadText) : _binaryPayload;
            if (payload is null) { StatusMessage = "请选择不超过 64 KiB 的二进制 Payload。"; return; }
            var capacity = _estimate.Execute(session, recipe.Value, payload.Bytes.Length);
            CapacitySummary = FormatCapacity(capacity);
            if (!capacity.Fits) { StatusMessage = "容量不足，已在生成位置和复制图片前阻断。"; return; }
            var generation = BeginOperation("正在写入、内存回读、PNG 真实回读并计算统计…");
            var token = _operationCancellation!.Token;
            try
            {
                var result = await _embed.ExecuteAsync(session, payload, recipe.Value, ResolveScope(), token).ConfigureAwait(true);
                if (!CanCommit(generation) || !ReferenceEquals(session, _session)) return;
                await ReplaceResultBitmapsAsync(session, result.Preview, token).ConfigureAwait(true);
                FrameSummary = $"{result.SelfCheck.Status}；Frame {session.Frame.Length:N0} B；槽位 {result.Facts.FrameBits:N0}；变化 {result.Facts.ChangedSlots:N0}，未变化 {result.Facts.UnchangedSlots:N0}；PSNR-RGB {(result.Facts.PsnrRgbDb?.ToString("F3") ?? "∞")} dB。";
                var channels = string.Join("；", result.Statistics.ByChannel.Select(item => $"{item.Key}:n={item.Value.Cover.SampleCount:N0}, one {F(item.Value.Cover.Distribution.OneRatio)}→{F(item.Value.Stego.Distribution.OneRatio)}, p {F(item.Value.Cover.PairOfValues.PValue)}→{F(item.Value.Stego.PairOfValues.PValue)}"));
                StatisticsSummary = $"Scope={result.Statistics.Cover.Scope}；聚合样本 {result.Statistics.Cover.SampleCount:N0}；one ratio {F(result.Statistics.Cover.Distribution.OneRatio)} → {F(result.Statistics.Stego.Distribution.OneRatio)}；χ² {result.Statistics.Cover.PairOfValues.Value:F4} → {result.Statistics.Stego.PairOfValues.Value:F4}；p {F(result.Statistics.Cover.PairOfValues.PValue)} → {F(result.Statistics.Stego.PairOfValues.PValue)}。分通道：{channels}。";
                AdjacencySummary = $"水平 transition {F(result.Statistics.Cover.Horizontal.TransitionRate)} → {F(result.Statistics.Stego.Horizontal.TransitionRate)}；垂直 {F(result.Statistics.Cover.Vertical.TransitionRate)} → {F(result.Statistics.Stego.Vertical.TransitionRate)}。";
                StatusMessage = "写入与两级自检完成；现在可以原子导出 PNG 或运行受控脆弱性实验。";
                OnPropertyChanged(nameof(HasVerifiedResult));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { if (CanCommit(generation)) StatusMessage = "写入已取消；没有提交半结果。"; }
            catch (Exception exception) { if (CanCommit(generation)) StatusMessage = exception.Message; }
            finally { EndOperation(generation); }
        }
        finally { temporary?.Dispose(); }
    }

    [RelayCommand]
    private async Task RunFragilityAsync()
    {
        var session = _session;
        if (session?.HasVerifiedStego != true) { StatusMessage = "请先完成一次自检通过的写入。"; return; }
        var generation = BeginOperation("正在从同一 stego 基线运行受控扰动…");
        var token = _operationCancellation!.Token;
        try
        {
            var result = await _fragility.ExecuteAsync(session, ResolveFragility(), token).ConfigureAwait(true);
            if (!CanCommit(generation) || !ReferenceEquals(session, _session)) return;
            ReplaceBitmap(ref _attackPreview, await CreateBitmapAsync(result.Image, token).ConfigureAwait(true), nameof(AttackPreview));
            FragilitySummary = $"{result.Preset}；Frame={result.Extraction.Status}；BER {F(result.FrameBer.Ratio)} ({result.FrameBer.ErrorBits}/{result.FrameBer.ComparedBits})；PSNR-RGB {F(result.PsnrRgbDb)} dB。";
            StatusMessage = "受控脆弱性实验完成；结果只适用于当前图片、配方和预设。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { if (CanCommit(generation)) StatusMessage = "扰动已取消。"; }
        catch (Exception exception) { if (CanCommit(generation)) StatusMessage = exception.Message; }
        finally { EndOperation(generation); }
    }

    [RelayCommand] private Task ExportPngAsync() => ExportImageCoreAsync();
    [RelayCommand] private Task ExportJsonAsync() => ExportReportCoreAsync("json");
    [RelayCommand] private Task ExportCsvAsync() => ExportReportCoreAsync("csv");
    [RelayCommand] private void Cancel() => _operationCancellation?.Cancel();
    [RelayCommand]
    private void InspectPixel()
    {
        var session = _session;
        if (session?.HasVerifiedStego != true) { StatusMessage = "请先完成当前配方写入再使用像素探针。"; return; }
        ProbeX = Math.Clamp(ProbeX, 0, session.SourceImage.Size.Width - 1);
        ProbeY = Math.Clamp(ProbeY, 0, session.SourceImage.Size.Height - 1);
        var value = _inspectPixel.Execute(session, ProbeX, ProbeY);
        var channels = string.Join("；", value.Channels.Select(item => $"{item.Channel}:{item.State}, frameBit={item.FrameBitIndex?.ToString() ?? "-"}, message={item.MessageBit?.ToString() ?? "-"}, bit {item.BeforeBit}→{item.AfterBit}, Δ={item.Delta}"));
        ProbeSummary = $"({value.X},{value.Y}) eligible={value.IsEligible}；Cover RGBA=({value.Cover.Red},{value.Cover.Green},{value.Cover.Blue},{value.Cover.Alpha})；Stego RGBA=({value.Stego.Red},{value.Stego.Green},{value.Stego.Blue},{value.Stego.Alpha})；{channels}";
    }
    [RelayCommand]
    private void RegenerateSeed()
    {
        Span<byte> bytes = stackalloc byte[8]; RandomNumberGenerator.Fill(bytes);
        SeedText = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes).ToString();
    }

    private async Task ExportImageCoreAsync()
    {
        var session = _session; if (session?.HasVerifiedStego != true) { StatusMessage = "没有通过自检的 PNG 可导出。"; return; }
        var path = await _imageDialog.PickOutputImageAsync("lsb-stego.png", _lifetime.ClosingToken).ConfigureAwait(true); if (string.IsNullOrWhiteSpace(path)) return;
        try { var result = await _exportImage.ExecuteAsync(session, path, _lifetime.ClosingToken).ConfigureAwait(true); StatusMessage = $"已原子导出并回读验证 PNG：{result.OutputPath}（{result.EncodedBytes:N0} B）。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    private async Task ExportReportCoreAsync(string format)
    {
        var session = _session; if (session?.HasVerifiedStego != true) { StatusMessage = "没有完整结果可导出报告。"; return; }
        var path = format == "json" ? await _reportDialog.PickLsbJsonOutputAsync("lsb-experiment.json", _lifetime.ClosingToken).ConfigureAwait(true) : await _reportDialog.PickLsbCsvOutputAsync("lsb-experiment.csv", _lifetime.ClosingToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        try { var result = await _exportReport.ExecuteAsync(session, path, format, _lifetime.ClosingToken).ConfigureAwait(true); StatusMessage = $"已导出不含 Payload、Frame 与绝对路径的 {result.Format.ToUpperInvariant()} 报告。"; }
        catch (Exception exception) { StatusMessage = exception.Message; }
    }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.SerializeToElement(new Snapshot(SourcePath, PayloadKind, SelectedChannel, BitPlane, Placement, SeedText, StatisticsScope, SelectedFragility));
        return ValueTask.FromResult(new DocumentSaveSnapshot(new(_revision), new(SnapshotSchema, payload)));
    }

    public void AcceptChanges(DocumentRevision savedRevision)
    {
        var before = IsDirty; if (savedRevision.Value == _revision) _acceptedRevision = _revision;
        if (before != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_generation; CancelOperation(); ReplaceSession(null); ReplaceBinaryPayload(null);
        ReplaceBitmap(ref _coverPreview, null, nameof(CoverPreview)); ReplaceBitmap(ref _stegoPreview, null, nameof(StegoPreview));
        ReplaceBitmap(ref _placementPreview, null, nameof(PlacementPreview)); ReplaceBitmap(ref _bitBeforePreview, null, nameof(BitBeforePreview));
        ReplaceBitmap(ref _bitAfterPreview, null, nameof(BitAfterPreview)); ReplaceBitmap(ref _attackPreview, null, nameof(AttackPreview));
    }

    partial void OnSourcePathChanged(string value) { if (!_restoring) { ReplaceSession(null); ClearRuntimeBitmaps(); StatusMessage = "载体路径已改变；请显式点击“准备载体”。"; ConfigurationChanged(); OnPropertyChanged(nameof(HasCarrier)); } }
    partial void OnPayloadKindChanged(string value) { OnPropertyChanged(nameof(IsTextPayload)); OnPropertyChanged(nameof(IsBinaryPayload)); if (!_restoring) ConfigurationChanged(); }
    partial void OnPayloadTextChanged(string value) { if (!_restoring) ConfigurationChanged(); }
    partial void OnBinaryPayloadPathChanged(string value) { if (!_restoring) ConfigurationChanged(); }
    partial void OnSelectedChannelChanged(string value) { if (!_restoring) ConfigurationChanged(); }
    partial void OnBitPlaneChanged(int value) { if (value is not (0 or 1)) BitPlane = Math.Clamp(value, 0, 1); else if (!_restoring) ConfigurationChanged(); }
    partial void OnPlacementChanged(string value) { OnPropertyChanged(nameof(IsPseudoRandom)); if (!_restoring) ConfigurationChanged(); }
    partial void OnSeedTextChanged(string value) { if (!_restoring) ConfigurationChanged(); }
    partial void OnStatisticsScopeChanged(string value) { if (!_restoring) ConfigurationChanged(); }
    partial void OnSelectedFragilityChanged(string value) { if (!_restoring) { ReplaceBitmap(ref _attackPreview, null, nameof(AttackPreview)); FragilitySummary = "预设已改变，尚未运行。"; MarkChanged(); } }

    private void ConfigurationChanged()
    {
        ++_generation; CancelOperation(); _session?.InvalidateResults(); ClearResultBitmaps(); FrameSummary = "配方或 Payload 已改变，旧结果已过期"; StatisticsSummary = "尚无当前配方统计"; AdjacencySummary = "尚无当前配方邻接结果"; FragilitySummary = "尚未运行受控扰动"; ProbeSummary = "当前配方尚未写入，探针结果已过期。"; UpdateCapacity(); OnPropertyChanged(nameof(HasVerifiedResult)); MarkChanged();
    }

    private void UpdateCapacity()
    {
        if (_session is null) { CapacitySummary = "尚未准备载体"; return; }
        var recipe = TryResolveRecipe(false); if (recipe is null) { CapacitySummary = "配方无效"; return; }
        var length = IsTextPayload ? Encoding.UTF8.GetByteCount(PayloadText) : _binaryPayload?.Bytes.Length ?? 0;
        if (length > LsbPayload.MaximumBytes) { CapacitySummary = "Payload 超过 65,536 字节硬上限"; return; }
        CapacitySummary = FormatCapacity(_estimate.Execute(_session, recipe.Value, length));
    }

    private LsbRecipe? TryResolveRecipe(bool report = true)
    {
        if (!ulong.TryParse(SeedText, out var seed)) { if (report) StatusMessage = "seed 必须是 0..18,446,744,073,709,551,615 的无符号整数。"; return null; }
        var recipe = new LsbRecipe(SelectedChannel switch { "R" => LsbChannelStrategy.Red, "G" => LsbChannelStrategy.Green, "B" => LsbChannelStrategy.Blue, _ => LsbChannelStrategy.RgbRoundRobin }, BitPlane, Placement == "伪随机" ? LsbPlacementKind.PseudoRandom : LsbPlacementKind.Sequential, seed);
        try { recipe.Validate(); return recipe; } catch (Exception exception) { if (report) StatusMessage = exception.Message; return null; }
    }

    private LsbStatisticsScope ResolveScope() => StatisticsScope switch { "本次选择槽位" => LsbStatisticsScope.SelectedSlots, "顺序连续前缀" => LsbStatisticsScope.SequentialPrefix, _ => LsbStatisticsScope.EligibleImage };
    private LsbFragilityPreset ResolveFragility() => SelectedFragility switch { "JPEG 80" => LsbFragilityPreset.Jpeg80, "JPEG 60" => LsbFragilityPreset.Jpeg60, "缩放 75% 往返" => LsbFragilityPreset.Scale75, "缩放 50% 往返" => LsbFragilityPreset.Scale50, "高斯轻度" => LsbFragilityPreset.GaussianLight, "高斯中度" => LsbFragilityPreset.GaussianMedium, "中值 3×3" => LsbFragilityPreset.Median3, _ => LsbFragilityPreset.Jpeg95 };

    private async Task ReplaceResultBitmapsAsync(LsbExperimentSession session, LsbPreviewProjection preview, CancellationToken token)
    {
        ReplaceBitmap(ref _stegoPreview, await CreateBitmapAsync(session.StegoImage!, token).ConfigureAwait(true), nameof(StegoPreview));
        ReplaceBitmap(ref _placementPreview, await CreateBitmapAsync(preview.Placement, token).ConfigureAwait(true), nameof(PlacementPreview));
        ReplaceBitmap(ref _bitBeforePreview, await CreateBitmapAsync(preview.BitBefore, token).ConfigureAwait(true), nameof(BitBeforePreview));
        ReplaceBitmap(ref _bitAfterPreview, await CreateBitmapAsync(preview.BitAfter, token).ConfigureAwait(true), nameof(BitAfterPreview));
    }

    private async Task<Bitmap> CreateBitmapAsync(PixelImage image, CancellationToken token)
    {
        var bytes = await _codec.EncodeAsync(image, ImageOutputFormat.Png, 100, token).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, false); return new Bitmap(stream);
    }

    private long BeginOperation(string status) { CancelOperation(); _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken); IsBusy = true; StatusMessage = status; return ++_generation; }
    private void EndOperation(long generation) { if (generation == _generation) IsBusy = false; }
    private bool CanCommit(long generation) => generation == _generation && !_disposed && !_lifetime.IsClosing;
    private void CancelOperation() { _operationCancellation?.Cancel(); _operationCancellation?.Dispose(); _operationCancellation = null; IsBusy = false; }
    private void ReplaceSession(LsbExperimentSession? value) { var previous = _session; _session = value; previous?.Dispose(); }
    private void ReplaceBinaryPayload(LsbPayload? value) { var previous = _binaryPayload; _binaryPayload = value; previous?.Dispose(); }
    private void ReplaceBitmap(ref Bitmap? field, Bitmap? value, string property) { var previous = field; field = value; OnPropertyChanged(property); previous?.Dispose(); }
    private void ClearRuntimeBitmaps() { ReplaceBitmap(ref _coverPreview, null, nameof(CoverPreview)); ClearResultBitmaps(); }
    private void ClearResultBitmaps() { ReplaceBitmap(ref _stegoPreview, null, nameof(StegoPreview)); ReplaceBitmap(ref _placementPreview, null, nameof(PlacementPreview)); ReplaceBitmap(ref _bitBeforePreview, null, nameof(BitBeforePreview)); ReplaceBitmap(ref _bitAfterPreview, null, nameof(BitAfterPreview)); ReplaceBitmap(ref _attackPreview, null, nameof(AttackPreview)); }
    private static string FormatCapacity(LsbCapacity value) => $"opaque {value.OpaquePixelCount:N0}；slots {value.EligibleSlots:N0}；Frame 开销 20 B；Payload 容量 {value.PayloadCapacityBytes:N0} B；V1 有效上限 {value.EffectivePayloadLimitBytes:N0} B；需要 {value.RequiredBits:N0} bit；{(value.Fits ? "可写入" : "容量不足")}；{value.BitsPerPixel:F4} bit/pixel；{value.BitsPerSlot:F4} bit/slot。";
    private static string F(double? value) => value is null || !double.IsFinite(value.Value) ? "N/A" : value.Value.ToString("F6");
    private void MarkChanged() { if (_restoring) return; var before = IsDirty; _revision++; if (before != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema) { StatusMessage = $"不支持 schema {content.SchemaVersion}；已使用安全默认值，且不会自动读取或运行。"; return; }
        var value = content.Payload.Deserialize<Snapshot>(); if (value is null) return;
        SourcePath = value.SourcePath ?? string.Empty; PayloadKind = PayloadKindOptions.Contains(value.PayloadKind) ? value.PayloadKind : "文本";
        SelectedChannel = ChannelOptions.Contains(value.Channel) ? value.Channel : "RGB"; BitPlane = value.BitPlane is 0 or 1 ? value.BitPlane : 0;
        Placement = PlacementOptions.Contains(value.Placement) ? value.Placement : "顺序"; SeedText = ulong.TryParse(value.Seed, out _) ? value.Seed : "1";
        StatisticsScope = StatisticsScopeOptions.Contains(value.Scope) ? value.Scope : "全图可用槽位"; SelectedFragility = FragilityOptions.Contains(value.Fragility) ? value.Fragility : "JPEG 95";
        PayloadText = string.Empty; BinaryPayloadPath = string.Empty;
        StatusMessage = "已恢复路径与轻量配方；Payload、Frame、像素和统计未持久化，请显式准备与运行。";
    }

    private sealed record Snapshot(string? SourcePath, string PayloadKind, string Channel, int BitPlane, string Placement, string Seed, string Scope, string Fragility);
}
