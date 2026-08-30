using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.Robustness;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Watermarking;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.RobustnessLab;

internal sealed partial class RobustnessStepItem : ObservableObject
{
    public RobustnessStepItem(string stepId, string kindId, bool enabled, string parameterId, decimal value)
    { StepId = stepId; _kindId = kindId; _enabled = enabled; _parameterId = parameterId; _value = value; }
    public string StepId { get; }
    [ObservableProperty] private string _kindId;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _parameterId;
    [ObservableProperty] private decimal _value;
    public string Summary => $"{KindId} / {ParameterId}={Value}";
    partial void OnKindIdChanged(string value) { var defaults = RobustnessLabDocument.DefaultFor(value); ParameterId = defaults.ParameterId; Value = defaults.Value; OnPropertyChanged(nameof(Summary)); }
    partial void OnParameterIdChanged(string value) => OnPropertyChanged(nameof(Summary));
    partial void OnValueChanged(decimal value) => OnPropertyChanged(nameof(Summary));
}

/// <summary>鲁棒性实验室 Document：只拥有配方、Session、取消、generation、快照和 UI 派生摘要。</summary>
/// <remarks>
/// 像素扰动、水印 BER、质量计算、JSON/CSV 与图片编解码都委托给窄用例。快照只保存路径和非敏感配方；
/// 密码、内联 Payload、恢复内容、图片像素和实验结果不会进入 Host 布局。任何配方变化都会取消运行并使旧结果失效。
/// </remarks>
internal sealed partial class RobustnessLabDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private readonly IPrepareRobustnessBaselineUseCase _prepare;
    private readonly IPlanRobustnessExperimentUseCase _plan;
    private readonly IRunRobustnessExperimentUseCase _run;
    private readonly IExportRobustnessReportUseCase _export;
    private readonly IImageFileDialog _images;
    private readonly IRobustnessReportFileDialog _reports;
    private readonly IDocumentLifetime _lifetime;
    private DocumentPresentationState _presentation = new("鲁棒性实验室");
    private RobustnessBaselineSession? _baseline; private RobustnessExperimentSession? _session; private CancellationTokenSource? _cancellation;
    private long _generation, _revision, _acceptedRevision; private bool _restoring, _disposed;

    public RobustnessLabDocument(IPrepareRobustnessBaselineUseCase prepare, IPlanRobustnessExperimentUseCase plan, IRunRobustnessExperimentUseCase run,
        IExportRobustnessReportUseCase export, IImageFileDialog images, IRobustnessReportFileDialog reports, IDocumentLifetime lifetime)
    {
        _prepare = prepare; _plan = plan; _run = run; _export = export; _images = images; _reports = reports; _lifetime = lifetime;
        Steps.CollectionChanged += (_, args) =>
        {
            if (args.OldItems is not null) foreach (RobustnessStepItem item in args.OldItems) item.PropertyChanged -= OnStepPropertyChanged;
            if (args.NewItems is not null) foreach (RobustnessStepItem item in args.NewItems) item.PropertyChanged += OnStepPropertyChanged;
            RecipeChanged();
        };
    }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _payloadText = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _useStealth;
    [ObservableProperty] private bool _useBalanced = true;
    [ObservableProperty] private bool _useRobust;
    [ObservableProperty] private string _selectedKindId = "jpeg-reencode";
    [ObservableProperty] private RobustnessStepItem? _selectedStep;
    [ObservableProperty] private decimal _scanStart = 95m;
    [ObservableProperty] private decimal _scanEnd = 75m;
    [ObservableProperty] private decimal _scanStep = 5m;
    [ObservableProperty] private int _trialCount = 1;
    [ObservableProperty] private long _experimentSeed = 20260830;
    [ObservableProperty] private bool _probeEachStep = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _completedCases;
    [ObservableProperty] private int _totalCases;
    [ObservableProperty] private string _statusMessage = "请选择载体图片，输入 Payload，并配置扰动链。";
    [ObservableProperty] private string _preflightSummary = "尚未预检。";
    [ObservableProperty] private string _resultSummary = "运行完成后显示成功率、BER 与首次失败位置。";
    [ObservableProperty] private IReadOnlyList<RobustnessCurvePoint> _curvePoints = [];

    public ObservableCollection<RobustnessStepItem> Steps { get; } = [];
    public IReadOnlyList<string> KindOptions { get; } = Enum.GetValues<PerturbationKind>().Select(value => value.ToStableId()).ToArray();
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasResult => _session is not null;
    public event EventHandler? PresentationChanged;
    public event EventHandler? IsDirtyChanged;

    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); _restoring = true;
        try
        {
            if (activation is RestoreDocumentActivation restore) Restore(restore.RestoredContent);
            if (Steps.Count == 0) AddDefaultStep("jpeg-reencode");
            _presentation = new(string.IsNullOrWhiteSpace(activation.Title) ? "鲁棒性实验室" : activation.Title); PresentationChanged?.Invoke(this, EventArgs.Empty); _revision = _acceptedRevision = 0;
        }
        finally { _restoring = false; }
        return ValueTask.CompletedTask;
    }

    [RelayCommand] private async Task SelectSourceAsync() { var path = await _images.PickImageAsync(_lifetime.ClosingToken).ConfigureAwait(true); if (!string.IsNullOrWhiteSpace(path)) SourcePath = path; }
    [RelayCommand] private void AddStep() { AddDefaultStep(SelectedKindId); SelectedStep = Steps[^1]; }
    [RelayCommand] private void RemoveStep(RobustnessStepItem? item) { if (item is not null) Steps.Remove(item); }
    [RelayCommand] private void CopyStep(RobustnessStepItem? item) { if (item is null || Steps.Count >= RobustnessLimits.MaximumSteps) return; var index = Steps.IndexOf(item); Steps.Insert(index + 1, new(Guid.NewGuid().ToString("N"), item.KindId, item.Enabled, item.ParameterId, item.Value)); }
    [RelayCommand] private void MoveUp(RobustnessStepItem? item) { if (item is null) return; var index = Steps.IndexOf(item); if (index > 0) Steps.Move(index, index - 1); }
    [RelayCommand] private void MoveDown(RobustnessStepItem? item) { if (item is null) return; var index = Steps.IndexOf(item); if (index >= 0 && index < Steps.Count - 1) Steps.Move(index, index + 1); }
    [RelayCommand] private void Cancel() => _cancellation?.Cancel();

    [RelayCommand]
    private void Preflight()
    {
        try { var profiles = GetProfiles(); var recipe = BuildRecipe(); var plan = _plan.Execute(recipe, profiles); TotalCases = plan.Cases.Count; PreflightSummary = $"{profiles.Count} Profile × {recipe.Scan.Values.Expand().Count} 扫描点 × {recipe.TrialCount} trial = {plan.Cases.Count} 案例；配方 {plan.RecipeHash}"; }
        catch (Exception exception) { PreflightSummary = $"预检失败：{exception.Message}"; }
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (string.IsNullOrWhiteSpace(SourcePath)) { StatusMessage = "请先选择载体图片。"; return; }
        var generation = ++_generation; CancelAndDispose(); _cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken); var current = _cancellation; var token = current.Token;
        InvalidateResult(disposeBaseline: true); IsBusy = true; StatusMessage = "正在建立受控基线…";
        try
        {
            var profiles = GetProfiles(); var recipe = BuildRecipe(); var plan = _plan.Execute(recipe, profiles); TotalCases = plan.Cases.Count; CompletedCases = 0;
            var baseline = await _prepare.ExecuteAsync(new(SourcePath, Encoding.UTF8.GetBytes(PayloadText), PayloadContentType.Text, profiles, string.IsNullOrEmpty(Password) ? null : Password), token).ConfigureAwait(true);
            if (!CanCommit(generation)) { baseline.Dispose(); return; }
            _baseline = baseline; StatusMessage = "基线回读通过，正在串行执行扫描…";
            var progress = new Progress<RobustnessProgress>(value => { if (generation == _generation) { CompletedCases = value.CompletedCases; TotalCases = value.TotalCases; } });
            var session = await _run.ExecuteAsync(baseline, plan, progress, token).ConfigureAwait(true);
            if (!CanCommit(generation)) { session.Dispose(); return; }
            _session = session; CurvePoints = session.Report.Curves; OnPropertyChanged(nameof(HasResult));
            var successes = session.Report.Cases.Count(value => value.FinalDiagnostic?.Success == true); var firstFailures = session.Report.Cases.Count(value => value.FirstObservedUnrecoverableStep is not null);
            ResultSummary = $"完成 {session.Report.Cases.Count} 案例，成功 {successes}；观察到首次失败位置 {firstFailures} 案例。选中曲线点可结合下方等价表格复核。";
            StatusMessage = session.Report.IsComplete
                ? "实验完成；结果只保存在当前 Document Session，可导出版本化 JSON/CSV。"
                : "实验已取消；已完成案例保留为不完整报告，未完成 trial 未进入成功率分母。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { if (!_lifetime.IsClosing && generation == _generation) StatusMessage = "实验已取消；未完成 trial 未进入成功率分母。"; }
        catch (Exception exception) { if (generation == _generation) StatusMessage = $"实验失败：{exception.Message}"; }
        finally { if (ReferenceEquals(_cancellation, current)) IsBusy = false; }
    }

    [RelayCommand] private async Task ExportJsonAsync() { var report = _session?.Report; if (report is null) return; var path = await _reports.PickJsonOutputAsync($"robustness-{report.RecipeHash}.json", _lifetime.ClosingToken); if (path is not null) { try { await _export.ExportJsonAsync(report, path, _lifetime.ClosingToken); StatusMessage = "JSON 报告已原子导出。"; } catch (Exception e) { StatusMessage = $"导出失败：{e.Message}"; } } }
    [RelayCommand] private async Task ExportCsvAsync() { var report = _session?.Report; if (report is null) return; var path = await _reports.PickCsvOutputAsync($"robustness-{report.RecipeHash}.csv", _lifetime.ClosingToken); if (path is not null) { try { await _export.ExportCsvAsync(report, path, _lifetime.ClosingToken); StatusMessage = "CSV 案例表已原子导出。"; } catch (Exception e) { StatusMessage = $"导出失败：{e.Message}"; } } }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var steps = Steps.Select(value => new StepSnapshot(value.StepId, value.KindId, value.Enabled, value.ParameterId, value.Value)).ToArray();
        var payload = JsonSerializer.SerializeToElement(new Snapshot(SourcePath, UseStealth, UseBalanced, UseRobust, ScanStart, ScanEnd, ScanStep, TrialCount, ExperimentSeed, ProbeEachStep, steps));
        return ValueTask.FromResult(new DocumentSaveSnapshot(new(_revision), new(SnapshotSchema, payload)));
    }
    public void AcceptChanges(DocumentRevision savedRevision) { var was = IsDirty; if (savedRevision.Value == _revision) _acceptedRevision = _revision; if (was != IsDirty) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }
    public void Dispose() { if (_disposed) return; _disposed = true; ++_generation; CancelAndDispose(); InvalidateResult(disposeBaseline: true); Password = string.Empty; PayloadText = string.Empty; }

    partial void OnSourcePathChanged(string value) => RecipeChanged(); partial void OnPayloadTextChanged(string value) => RecipeChanged(); partial void OnPasswordChanged(string value) => RecipeChanged();
    partial void OnUseStealthChanged(bool value) => RecipeChanged(); partial void OnUseBalancedChanged(bool value) => RecipeChanged(); partial void OnUseRobustChanged(bool value) => RecipeChanged();
    partial void OnScanStartChanged(decimal value) => RecipeChanged(); partial void OnScanEndChanged(decimal value) => RecipeChanged(); partial void OnScanStepChanged(decimal value) => RecipeChanged(); partial void OnTrialCountChanged(int value) => RecipeChanged(); partial void OnExperimentSeedChanged(long value) => RecipeChanged(); partial void OnProbeEachStepChanged(bool value) => RecipeChanged();

    private RobustnessRecipe BuildRecipe()
    {
        if (Steps.Count == 0) throw new InvalidOperationException("至少添加一个扰动步骤。"); var target = SelectedStep is not null && Steps.Contains(SelectedStep) ? SelectedStep : Steps[0];
        var steps = Steps.Select(ToDomainStep).ToArray(); RobustnessScan scan = ScanEnd >= ScanStart ? new DecimalRangeScan(ScanStart, ScanEnd, ScanStep) : new ExplicitValueScan(Enumerable.Range(0, checked((int)((ScanStart - ScanEnd) / ScanStep) + 1)).Select(i => ScanStart - (i * ScanStep)).ToArray());
        return new(1, steps, new(target.StepId, target.ParameterId, scan), TrialCount, unchecked((ulong)ExperimentSeed), ProbeEachStep);
    }
    private static PerturbationStep ToDomainStep(RobustnessStepItem item)
    { var kind = PerturbationKindIds.Parse(item.KindId); var defaults = CreateParameters(kind); var step = new PerturbationStep(item.StepId, kind, item.Enabled, defaults); return PerturbationParameterEditor.WithScannedValue(step, item.ParameterId, item.Value); }
    private IReadOnlyList<EmbeddingProfileId> GetProfiles() { var values = new List<EmbeddingProfileId>(); if (UseStealth) values.Add(EmbeddingProfileId.Stealth); if (UseBalanced) values.Add(EmbeddingProfileId.Balanced); if (UseRobust) values.Add(EmbeddingProfileId.Robust); if (values.Count == 0) throw new InvalidOperationException("至少选择一个 Profile。"); return values; }
    private void AddDefaultStep(string kindId) { if (Steps.Count >= RobustnessLimits.MaximumSteps) return; var defaults = DefaultFor(kindId); Steps.Add(new(Guid.NewGuid().ToString("N"), kindId, true, defaults.ParameterId, defaults.Value)); }
    private void OnStepPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => RecipeChanged();
    internal static (string ParameterId, decimal Value) DefaultFor(string kindId) => PerturbationKindIds.Parse(kindId) switch
    {
        PerturbationKind.JpegReencode => ("quality", 95), PerturbationKind.Scale => ("scale-x", 1), PerturbationKind.GaussianNoise => ("sigma", 0), PerturbationKind.SaltPepperNoise => ("ratio", 0),
        PerturbationKind.DeterministicPixel => ("amplitude", 0), PerturbationKind.GaussianBlur => ("sigma", 0), PerturbationKind.MedianBlur => ("kernel-size", 3), PerturbationKind.UnsharpMask => ("amount", 0),
        PerturbationKind.Crop => ("left", 0), PerturbationKind.Pad => ("left", 0), PerturbationKind.Translate => ("dx", 0), PerturbationKind.Rotate => ("degrees", 0), PerturbationKind.Perspective => ("top-left-x", 0),
        PerturbationKind.Brightness => ("offset", 0), PerturbationKind.Contrast => ("factor", 1), PerturbationKind.Gamma => ("gamma", 1), PerturbationKind.Saturation => ("factor", 1), PerturbationKind.ColorBias => ("red", 0), _ => throw new ArgumentOutOfRangeException()
    };
    private static PerturbationParameters CreateParameters(PerturbationKind kind) => kind switch
    {
        PerturbationKind.JpegReencode => new JpegParameters(), PerturbationKind.Scale => new ScaleParameters(), PerturbationKind.GaussianNoise => new GaussianNoiseParameters(), PerturbationKind.SaltPepperNoise => new SaltPepperParameters(),
        PerturbationKind.DeterministicPixel => new DeterministicPixelParameters(), PerturbationKind.GaussianBlur => new GaussianBlurParameters(), PerturbationKind.MedianBlur => new MedianBlurParameters(), PerturbationKind.UnsharpMask => new UnsharpMaskParameters(),
        PerturbationKind.Crop => new CropParameters(), PerturbationKind.Pad => new PadParameters(), PerturbationKind.Translate => new TranslateParameters(), PerturbationKind.Rotate => new RotateParameters(), PerturbationKind.Perspective => new PerspectiveParameters(),
        PerturbationKind.Brightness => new BrightnessParameters(), PerturbationKind.Contrast => new ContrastParameters(), PerturbationKind.Gamma => new GammaParameters(), PerturbationKind.Saturation => new SaturationParameters(), PerturbationKind.ColorBias => new ColorBiasParameters(), _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    private bool CanCommit(long generation) => generation == _generation && !_disposed && !_lifetime.IsClosing;
    private void RecipeChanged() { if (_restoring) return; ++_generation; CancelAndDispose(); InvalidateResult(disposeBaseline: true); MarkChanged(); StatusMessage = "配方已变化；旧结果已失效，请重新预检并运行。"; }
    private void InvalidateResult(bool disposeBaseline) { _session?.Dispose(); _session = null; if (disposeBaseline) { _baseline?.Dispose(); _baseline = null; } CurvePoints = []; CompletedCases = TotalCases = 0; OnPropertyChanged(nameof(HasResult)); }
    private void MarkChanged() { if (_restoring) return; var was = IsDirty; _revision++; if (!was) IsDirtyChanged?.Invoke(this, EventArgs.Empty); }
    private void CancelAndDispose() { _cancellation?.Cancel(); _cancellation?.Dispose(); _cancellation = null; }
    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema) { StatusMessage = $"不支持 schema {content.SchemaVersion}；已保留安全空配方。"; return; }
        var value = content.Payload.Deserialize<Snapshot>(); if (value is null) return; SourcePath = value.SourcePath ?? string.Empty; UseStealth = value.UseStealth; UseBalanced = value.UseBalanced; UseRobust = value.UseRobust;
        ScanStart = value.ScanStart; ScanEnd = value.ScanEnd; ScanStep = value.ScanStep; TrialCount = Math.Clamp(value.TrialCount, 1, RobustnessLimits.MaximumTrials); ExperimentSeed = value.ExperimentSeed; ProbeEachStep = value.ProbeEachStep;
        Steps.Clear(); foreach (var step in value.Steps ?? []) Steps.Add(new(step.StepId ?? Guid.NewGuid().ToString("N"), step.KindId ?? "unsupported", step.Enabled, step.ParameterId ?? string.Empty, step.Value));
        Password = PayloadText = string.Empty; StatusMessage = "已恢复非敏感配方；请重新输入 Payload 和密码后显式运行。";
    }
    private sealed record Snapshot(string? SourcePath, bool UseStealth, bool UseBalanced, bool UseRobust, decimal ScanStart, decimal ScanEnd, decimal ScanStep, int TrialCount, long ExperimentSeed, bool ProbeEachStep, StepSnapshot[]? Steps);
    private sealed record StepSnapshot(string? StepId, string? KindId, bool Enabled, string? ParameterId, decimal Value);
}
