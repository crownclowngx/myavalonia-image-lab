using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Shared.Perturbations;

namespace ImageLabPlugin.Tests;

internal static class PerturbationTestContext
{
    public static PerturbationExecutionContext From(
        ulong seed,
        RobustnessCaseKey key,
        string stepId,
        PerturbationKind kind) =>
        PerturbationSeedDeriver.FromCanonicalFacts(
            seed,
            (byte)key.Profile,
            key.CanonicalValue,
            key.TrialIndex,
            stepId,
            kind);
}
