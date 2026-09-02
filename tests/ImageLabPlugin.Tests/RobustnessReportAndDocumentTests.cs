using System.Text.Json;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.Robustness;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Shared.Perturbations;
using ImageLabPlugin.Domain.Watermarking;
using ImageLabPlugin.Features.RobustnessLab;
using ImageLabPlugin.Infrastructure.Robustness;
using ImageLabPlugin.Domain.Shared.Imaging;
using MyAvaloniaManagement.PluginSdk;
using Xunit;
using System.Diagnostics;

namespace ImageLabPlugin.Tests;

public sealed class RobustnessReportAndDocumentTests
{
    [Fact]
    public void 报告与CSV不泄漏路径Payload密码或MappingKey()
    {
        var diagnostic = new WatermarkDiagnosticResult(true, RobustnessDetectionStatus.RecoveredIntegrityValid, RobustnessIntegrityStatus.Valid, true,
            new(new(0, 24), new(0, 8), 0, 1, 1), new(new(0, 24), new(0, 8), 0, .9, .8), RobustnessFailureReason.None, "ok");
        var quality = new QualityMeasurement(null, QualityUnavailableReason.SizeMismatch);
        var item = new RobustnessCaseResult(new(RobustnessProfileId.Balanced, 0, 1m, 0), true, diagnostic, [], null, false, quality, quality, []);
        var recipe = new RobustnessRecipeFacts(1, [new("s", "brightness", true, "BrightnessParameters { Offset = 1 }")], "s", "offset", [1m], 1, [RobustnessProfileId.Balanced], true);
        var report = new RobustnessExperimentReport(1, "abc", DateTimeOffset.UnixEpoch, true, 7, "SplitMix64", "D:/secret/carrier.png", 12, "digest-id", recipe, [item], RobustnessResultAggregator.Aggregate([item]));
        var serializer = new RobustnessReportSerializer(); var json = serializer.SerializeJson(report); var csv = serializer.SerializeCsv(report);
        Assert.Contains("carrier.png", json, StringComparison.Ordinal); Assert.DoesNotContain("D:/secret", json, StringComparison.OrdinalIgnoreCase);
        foreach (var secret in new[] { "payload-content", "password", "mappingKey", "nonce", "salt" }) { Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain(secret, csv, StringComparison.OrdinalIgnoreCase); }
        Assert.StartsWith("schema_version,recipe_hash", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void 聚合排除未完成案例并保留NA()
    {
        var quality = new QualityMeasurement(null, QualityUnavailableReason.SizeMismatch);
        var success = Diagnostic(true); var failure = Diagnostic(false);
        var cases = new[]
        {
            new RobustnessCaseResult(new(RobustnessProfileId.Stealth, 0, 1, 0), true, success, [], null, false, quality, quality, []),
            new RobustnessCaseResult(new(RobustnessProfileId.Stealth, 0, 1, 1), true, failure, [], "x", false, quality, quality, []),
            new RobustnessCaseResult(new(RobustnessProfileId.Stealth, 0, 1, 2), false, null, [], null, false, quality, quality, [])
        };
        var point = Assert.Single(RobustnessResultAggregator.Aggregate(cases)); Assert.Equal(2, point.CompletedTrials); Assert.Equal(.5, point.SuccessRate); Assert.Null(cases[0].AttackOnlyQuality.Metrics);
    }

    [Fact]
    public void 局部网格覆盖边缘且尺寸不同时返回空而非伪零()
    {
        var bytes = Enumerable.Repeat((byte)20, 17 * 9 * 4).ToArray(); for (var i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
        var source = new PixelImage(new ImageSize(17, 9), bytes); var same = LocalQualityGridAnalyzer.Analyze(source, source.Clone(), default);
        Assert.Equal(16 * 9, same.Count); Assert.All(same, cell => Assert.Equal(0, cell.MeanAbsoluteErrorRgb));
        var different = new PixelImage(new ImageSize(1, 1), [0, 0, 0, 255]); Assert.Empty(LocalQualityGridAnalyzer.Analyze(source, different, default));
    }

    [Fact]
    public async Task 快照仅保存非敏感配方且恢复后不自动运行()
    {
        var prepare = new NeverPrepare(); using var source = CreateDocument(prepare); await source.InitializeAsync(new NewDocumentActivation("test"), default);
        source.SourcePath = "D:/images/carrier.png"; source.PayloadText = "payload-secret"; source.Password = "password-secret"; source.UseRobust = true; source.ExperimentSeed = 123;
        var snapshot = await source.CaptureSaveSnapshotAsync(default); var json = snapshot.Content.Payload.GetRawText();
        Assert.Contains("carrier.png", json, StringComparison.Ordinal); Assert.Contains("jpeg-reencode", json, StringComparison.Ordinal); Assert.DoesNotContain("payload-secret", json, StringComparison.Ordinal); Assert.DoesNotContain("password-secret", json, StringComparison.Ordinal);
        using var restored = CreateDocument(prepare); await restored.InitializeAsync(new RestoreDocumentActivation("restored", snapshot.Content), default);
        Assert.Empty(restored.PayloadText); Assert.Empty(restored.Password); Assert.False(restored.HasResult); Assert.Equal(0, prepare.Calls); Assert.False(restored.IsDirty);
    }

    [Fact]
    public async Task 步骤参数变化推进Revision且未知Kind可见阻断预检()
    {
        using var document = CreateDocument(new NeverPrepare()); await document.InitializeAsync(new NewDocumentActivation("test"), default); var snapshot = await document.CaptureSaveSnapshotAsync(default); document.AcceptChanges(snapshot.Revision);
        document.Steps[0].Value = 90; Assert.True(document.IsDirty);
        var payload = JsonSerializer.SerializeToElement(new { SourcePath = "x.png", UseStealth = false, UseBalanced = true, UseRobust = false, ScanStart = 1, ScanEnd = 1, ScanStep = 1, TrialCount = 1, ExperimentSeed = 1, ProbeEachStep = true, Steps = new[] { new { StepId = "u", KindId = "future-operator", Enabled = true, ParameterId = "x", Value = 1 } } });
        using var unknown = CreateDocument(new NeverPrepare()); await unknown.InitializeAsync(new RestoreDocumentActivation("unknown", new DocumentContent(1, payload)), default); unknown.PreflightCommand.Execute(null);
        Assert.Contains("不支持", unknown.PreflightSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 配方变化拒绝忽略取消的迟到基线()
    {
        var late = new LatePrepare(); using var document = CreateDocument(late); await document.InitializeAsync(new NewDocumentActivation("late"), default);
        document.SourcePath = "first.png"; var operation = document.RunCommand.ExecuteAsync(null); await late.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        document.SourcePath = "second.png"; var baseline = new RobustnessBaselineSession("first.png", new PixelImage(new ImageSize(1, 1), [0, 0, 0, 255]), [], [], new Dictionary<EmbeddingProfileId, ControlledWatermarkBaseline>(), "digest");
        late.Complete(baseline); await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(document.HasResult); Assert.Contains("配方已变化", document.StatusMessage, StringComparison.Ordinal); Assert.Throws<ObjectDisposedException>(baseline.ThrowIfDisposed);
    }

    [Fact]
    public async Task 同步CPU型用例不会阻塞运行命令且取消保持可交互()
    {
        var blocking = new BlockingPrepare(); using var document = CreateDocument(blocking);
        await document.InitializeAsync(new NewDocumentActivation("background"), default); document.SourcePath = "source.png";

        var stopwatch = Stopwatch.StartNew();
        var operation = document.RunCommand.ExecuteAsync(null);
        stopwatch.Stop();
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // ExecuteAsync 必须在同步用例完成前立即交还调用线程；否则 Avalonia Dispatcher 会被冻结。
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500), $"运行命令返回耗时 {stopwatch.Elapsed}。");
        Assert.True(document.IsBusy); Assert.True(document.IsPreparingBaseline); Assert.False(document.IsRecipeEditable);
        Assert.Contains("后台", document.OperationStage, StringComparison.Ordinal);

        document.CancelCommand.Execute(null);
        await operation.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(document.IsBusy); Assert.False(document.IsPreparingBaseline); Assert.True(document.IsRecipeEditable);
        Assert.Contains("取消", document.OperationStage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 中文攻击帮助覆盖全部算子且选择后同步参数解释()
    {
        var expectedIds = Enum.GetValues<PerturbationKind>().Select(value => value.ToStableId()).Order().ToArray();
        var attacks = RobustnessLabHelpCatalog.Attacks;
        Assert.Equal(expectedIds, attacks.Select(value => value.KindId).Order());
        Assert.Equal(attacks.Count, attacks.Select(value => value.KindId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(attacks, attack =>
        {
            Assert.False(string.IsNullOrWhiteSpace(attack.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(attack.Description));
            Assert.False(string.IsNullOrWhiteSpace(attack.Purpose));
            Assert.False(string.IsNullOrWhiteSpace(attack.Caution));
            Assert.NotEmpty(attack.Parameters);
            Assert.Equal(attack.Parameters.Count, attack.Parameters.Select(value => value.ParameterId).Distinct(StringComparer.Ordinal).Count());
            var defaults = RobustnessLabDocument.DefaultFor(attack.KindId);
            Assert.Contains(attack.Parameters, value => value.ParameterId == defaults.ParameterId && value.DefaultValue == defaults.Value);
            var domainStep = new PerturbationStep("help", attack.Kind, true, RobustnessLabDocument.CreateParameters(attack.Kind));
            Assert.All(attack.Parameters, parameter =>
                PerturbationParameterEditor.WithScannedValue(domainStep, parameter.ParameterId, parameter.DefaultValue));
        });

        using var document = CreateDocument(new NeverPrepare());
        await document.InitializeAsync(new NewDocumentActivation("help"), default);
        var step = Assert.IsType<RobustnessStepItem>(document.SelectedStep);
        Assert.Contains("JPEG", step.Summary, StringComparison.Ordinal);

        step.SelectedAttack = Assert.Single(attacks, value => value.Kind == PerturbationKind.Crop);
        step.SelectedParameter = Assert.Single(step.ParameterOptions, value => value.ParameterId == "bottom");
        Assert.Equal("crop", step.KindId); Assert.Equal("bottom", step.ParameterId); Assert.Equal(0m, step.Value);
        Assert.Contains("底部裁剪", step.Summary, StringComparison.Ordinal);
        Assert.Contains("像素", step.ParameterHelp.UnitAndRange, StringComparison.Ordinal);
        Assert.Contains("尺寸", step.AttackHelp.Caution, StringComparison.Ordinal);
    }

    private static WatermarkDiagnosticResult Diagnostic(bool success) => new(success, success ? RobustnessDetectionStatus.RecoveredIntegrityValid : RobustnessDetectionStatus.UnrecoverableDamage,
        success ? RobustnessIntegrityStatus.Valid : RobustnessIntegrityStatus.Invalid, success, null, null, success ? RobustnessFailureReason.None : RobustnessFailureReason.DataUnrecoverable, "test");

    private static RobustnessLabDocument CreateDocument(IPrepareRobustnessBaselineUseCase prepare) => new(prepare,
        new PlanRobustnessExperimentUseCase(new(new())), new NeverRun(), new NeverExport(), new NullImageDialog(), new NullReportDialog(), new TestLifetime());
    private sealed class NeverPrepare : IPrepareRobustnessBaselineUseCase { public int Calls { get; private set; } public Task<RobustnessBaselineSession> ExecuteAsync(PrepareRobustnessBaselineRequest request, CancellationToken cancellationToken) { Calls++; throw new InvalidOperationException(); } }
    private sealed class LatePrepare : IPrepareRobustnessBaselineUseCase
    {
        private readonly TaskCompletionSource<RobustnessBaselineSession> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<RobustnessBaselineSession> ExecuteAsync(PrepareRobustnessBaselineRequest request, CancellationToken cancellationToken) { Started.TrySetResult(); return _completion.Task; }
        public void Complete(RobustnessBaselineSession session) => _completion.SetResult(session);
    }
    private sealed class BlockingPrepare : IPrepareRobustnessBaselineUseCase
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<RobustnessBaselineSession> ExecuteAsync(PrepareRobustnessBaselineRequest request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("测试未请求取消。");
        }
    }
    private sealed class NeverRun : IRunRobustnessExperimentUseCase { public Task<RobustnessExperimentSession> ExecuteAsync(RobustnessBaselineSession baseline, RobustnessExecutionPlan plan, IProgress<RobustnessProgress>? progress, CancellationToken cancellationToken) => throw new InvalidOperationException(); }
    private sealed class NeverExport : IExportRobustnessReportUseCase
    {
        public Task ExportJsonAsync(RobustnessExperimentReport report, string path, CancellationToken cancellationToken) => throw new InvalidOperationException(); public Task ExportCsvAsync(RobustnessExperimentReport report, string path, CancellationToken cancellationToken) => throw new InvalidOperationException(); public string CreateJson(RobustnessExperimentReport report) => throw new InvalidOperationException(); public string CreateCsv(RobustnessExperimentReport report) => throw new InvalidOperationException();
    }
    private sealed class NullImageDialog : IImageFileDialog { public Task<string?> PickImageAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null); public Task<string?> PickOutputImageAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null); }
    private sealed class NullReportDialog : IRobustnessReportFileDialog { public Task<string?> PickJsonOutputAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null); public Task<string?> PickCsvOutputAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null); }
    private sealed class TestLifetime : IDocumentLifetime { public CancellationToken ClosingToken => default; public bool IsClosing => false; }
}
