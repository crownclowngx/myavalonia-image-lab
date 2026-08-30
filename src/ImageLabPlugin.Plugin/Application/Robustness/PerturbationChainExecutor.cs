using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Robustness.Operators;

namespace ImageLabPlugin.Application.Robustness;

internal sealed record PerturbationChainResult(PixelImage Image, IReadOnlyList<RobustnessObservation> Observations, string? FirstFailureStep, bool RecoveredAfterFailure);

/// <summary>只负责按用户顺序调用显式登记的 Strategy，并在前缀边界触发诊断。</summary>
internal sealed class PerturbationChainExecutor
{
    private readonly IReadOnlyDictionary<PerturbationKind, IImagePerturbationOperator> _operators;
    public PerturbationChainExecutor(IEnumerable<IImagePerturbationOperator> operators)
    {
        var values = operators.ToArray();
        var duplicates = values.GroupBy(value => value.Kind).Where(group => group.Count() != 1).Select(group => group.Key.ToStableId()).ToArray();
        if (duplicates.Length > 0) throw new InvalidOperationException($"扰动 Strategy 重复登记：{string.Join(", ", duplicates)}");
        _operators = values.ToDictionary(value => value.Kind);
    }

    public async Task<PerturbationChainResult> ExecuteAsync(PixelImage baseline, RobustnessPlannedCase plannedCase, ulong experimentSeed, bool probeEachStep,
        Func<PixelImage, PerturbationStep, int, WatermarkDiagnosticResult> probe, CancellationToken token)
    {
        var current = baseline.Clone(); var observations = new List<RobustnessObservation>(); string? first = null; var recovered = false; var enabledIndex = 0;
        foreach (var step in plannedCase.Steps.Where(value => value.Enabled))
        {
            token.ThrowIfCancellationRequested();
            if (!_operators.TryGetValue(step.Kind, out var implementation)) throw new InvalidOperationException($"未登记扰动 Strategy：{step.Kind.ToStableId()}");
            current = await implementation.ApplyAsync(current, step.Parameters, new(experimentSeed, plannedCase.Key, step.StepId, step.Kind), token).ConfigureAwait(false);
            enabledIndex++;
            if (!probeEachStep && enabledIndex < plannedCase.Steps.Count(value => value.Enabled)) continue;
            var diagnostic = probe(current, step, enabledIndex - 1); observations.Add(new(step.StepId, enabledIndex - 1, diagnostic));
            if (!diagnostic.Success && first is null) first = step.StepId;
            else if (diagnostic.Success && first is not null) recovered = true;
        }
        return new(current, observations, probeEachStep ? first : null, recovered);
    }
}
