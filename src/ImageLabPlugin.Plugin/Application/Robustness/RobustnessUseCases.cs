using System.Security.Cryptography;
using System.Text;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.Watermarking;
using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Watermarking;
using ImageLabPlugin.Infrastructure.Watermarking;
using ImageLabPlugin.Domain.Fingerprinting;

namespace ImageLabPlugin.Application.Robustness;

internal sealed class PrepareRobustnessBaselineUseCase(
    IImageCodec codec,
    WatermarkFrameProtocol protocol,
    FrequencyWatermarkCarrier carrier,
    IExtractWatermarkUseCase extractor) : IPrepareRobustnessBaselineUseCase
{
    public async Task<RobustnessBaselineSession> ExecuteAsync(PrepareRobustnessBaselineRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePath);
        if (request.Profiles.Count == 0) throw new ArgumentException("至少选择一个 Profile。", nameof(request));
        var original = await codec.DecodeAsync(request.SourcePath, token).ConfigureAwait(false); var payloadCopy = request.Payload.ToArray();
        var profiles = new Dictionary<EmbeddingProfileId, ControlledWatermarkBaseline>();
        try
        {
            foreach (var profile in request.Profiles.Distinct().Order())
            {
                token.ThrowIfCancellationRequested(); using var payload = new WatermarkPayload(payloadCopy, request.ContentType);
                var capacity = carrier.Estimate(original, profile, payloadCopy.Length, !string.IsNullOrEmpty(request.Password));
                if (!capacity.Fits) throw new InvalidOperationException($"{EmbeddingProfile.Resolve(profile).DisplayName} Profile 容量不足：最多 {capacity.MaximumPayloadBytes} 字节。");
                var frame = protocol.Encode(payload, profile, request.Password);
                try
                {
                    var watermarked = carrier.Embed(original, frame, token); var selfCheck = extractor.Extract(watermarked, request.Password, token);
                    if (selfCheck.Status is not (WatermarkDetectionStatus.RecoveredIntegrityValid or WatermarkDetectionStatus.RecoveredWithCorrections) || !selfCheck.Payload.Span.SequenceEqual(payloadCopy))
                        throw new InvalidOperationException($"{EmbeddingProfile.Resolve(profile).DisplayName} Profile 未扰动基线回读失败：{selfCheck.Summary}");
                    profiles.Add(profile, new(profile, watermarked, frame, selfCheck)); frame = null!;
                }
                finally
                {
                    if (frame is not null) { CryptographicOperations.ZeroMemory(frame.EncodedHeader); CryptographicOperations.ZeroMemory(frame.EncodedData); CryptographicOperations.ZeroMemory(frame.MappingKey); }
                }
            }
            var digest = Convert.ToHexString(SHA256.HashData(payloadCopy))[..16].ToLowerInvariant();
            return new(Path.GetFileName(request.SourcePath), original, payloadCopy, string.IsNullOrEmpty(request.Password) ? [] : Encoding.UTF8.GetBytes(request.Password), profiles, digest);
        }
        catch
        {
            foreach (var value in profiles.Values) value.Dispose(); CryptographicOperations.ZeroMemory(payloadCopy); throw;
        }
    }
}

internal sealed class PlanRobustnessExperimentUseCase(RobustnessExperimentPlanner planner) : IPlanRobustnessExperimentUseCase
{
    public RobustnessExecutionPlan Execute(RobustnessRecipe recipe, IReadOnlyList<EmbeddingProfileId> profiles) => planner.Plan(recipe, profiles);
}

internal sealed class RunRobustnessExperimentUseCase(
    PerturbationChainExecutor chain,
    IWatermarkDiagnosticReader diagnostics,
    FullReferenceQualityAnalyzer quality,
    IFingerprintObservationProbe? fingerprintProbe = null) : IRunRobustnessExperimentUseCase
{
    public async Task<RobustnessExperimentSession> ExecuteAsync(RobustnessBaselineSession baseline, RobustnessExecutionPlan plan, IProgress<RobustnessProgress>? progress, CancellationToken token)
        => await ExecuteAsync(baseline, plan, [], progress, token).ConfigureAwait(false);

    public async Task<RobustnessExperimentSession> ExecuteAsync(
        RobustnessBaselineSession baseline,
        RobustnessExecutionPlan plan,
        IReadOnlyList<FingerprintAlgorithmId> fingerprintAlgorithms,
        IProgress<RobustnessProgress>? progress,
        CancellationToken token)
    {
        baseline.ThrowIfDisposed(); var results = new List<RobustnessCaseResult>(plan.Cases.Count); var password = baseline.GetPassword();
        try
        {
            try
            {
                foreach (var planned in plan.Cases)
                {
                    token.ThrowIfCancellationRequested(); progress?.Report(new(results.Count, plan.Cases.Count, planned.Key));
                    var controlled = baseline.Profiles[planned.Key.Profile];
                    try
                    {
                        var executed = await chain.ExecuteAsync(controlled.Image, planned, plan.Recipe.ExperimentSeed, plan.Recipe.ProbeEachStep,
                            (image, _, _) => diagnostics.Read(image, controlled, baseline.Payload, password, token), token).ConfigureAwait(false);
                        var final = executed.Observations.LastOrDefault()?.Diagnostic ?? diagnostics.Read(executed.Image, controlled, baseline.Payload, password, token);
                        results.Add(new(planned.Key, true, final, executed.Observations, executed.FirstFailureStep, executed.RecoveredAfterFailure,
                            Measure(controlled.Image, executed.Image, token), Measure(baseline.Original, executed.Image, token), LocalQualityGridAnalyzer.Analyze(controlled.Image, executed.Image, token),
                            FingerprintObservations: fingerprintAlgorithms.Count == 0 || fingerprintProbe is null
                                ? null
                                : fingerprintProbe.Observe(controlled.Image, executed.Image, fingerprintAlgorithms, token)));
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception exception)
                    {
                        var failed = new WatermarkDiagnosticResult(false, WatermarkDetectionStatus.UnrecoverableDamage, IntegrityStatus.NotChecked, false, null, null, RobustnessFailureReason.OperatorFailed, exception.GetType().Name);
                        results.Add(new(planned.Key, true, failed, [], null, false, new(null, QualityUnavailableReason.OperatorFailed), new(null, QualityUnavailableReason.OperatorFailed), [], OperatorError: exception.Message));
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return new(BuildReport(isComplete: false));
            }
            progress?.Report(new(results.Count, plan.Cases.Count, null));
            return new(BuildReport(isComplete: true));

            RobustnessExperimentReport BuildReport(bool isComplete)
            {
                var recipeFacts = new RobustnessRecipeFacts(plan.Recipe.SchemaVersion,
                    plan.Recipe.Steps.Select(step => new RobustnessStepFact(step.StepId, step.Kind.ToStableId(), step.Enabled, step.Parameters.ToString() ?? string.Empty)).ToArray(),
                    plan.Recipe.Scan.StepId, plan.Recipe.Scan.ParameterId, plan.Recipe.Scan.Values.Expand(), plan.Recipe.TrialCount,
                    plan.Cases.Select(value => value.Key.Profile).Distinct().Order().ToArray(), plan.Recipe.ProbeEachStep);
                return new(1, plan.RecipeHash, DateTimeOffset.UtcNow, isComplete, plan.Recipe.ExperimentSeed, "SHA-256/SplitMix64-v1", baseline.SourceName, baseline.PayloadLength, baseline.PayloadDigestId, recipeFacts, results.ToArray(), RobustnessResultAggregator.Aggregate(results));
            }
        }
        finally { password = null; }
    }

    private QualityMeasurement Measure(PixelImage reference, PixelImage candidate, CancellationToken token) => reference.Size == candidate.Size
        ? new(quality.Analyze(reference, candidate, token), null)
        : new(null, QualityUnavailableReason.SizeMismatch);
}

/// <summary>复用已登记的三种无状态算法，为鲁棒性案例追加只读观测。</summary>
internal sealed class FingerprintObservationProbe(
    IEnumerable<IImageFingerprintAlgorithm> algorithms,
    FingerprintDistanceCalculator distanceCalculator) : IFingerprintObservationProbe
{
    private readonly IReadOnlyDictionary<FingerprintAlgorithmId, IImageFingerprintAlgorithm> _algorithms = algorithms.ToDictionary(value => value.Id);

    public IReadOnlyList<FingerprintObservation> Observe(PixelImage reference, PixelImage candidate, IReadOnlyList<FingerprintAlgorithmId> algorithms, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference); ArgumentNullException.ThrowIfNull(candidate); ArgumentNullException.ThrowIfNull(algorithms);
        var result = new List<FingerprintObservation>(algorithms.Count);
        foreach (var id in algorithms.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_algorithms.TryGetValue(id, out var algorithm)) throw new ArgumentException($"未登记指纹算法 {id}。", nameof(algorithms));
            var left = algorithm.Compute(reference, cancellationToken); var right = algorithm.Compute(candidate, cancellationToken);
            result.Add(new(id, left, right, distanceCalculator.Calculate(left, right)));
        }
        return result;
    }
}
