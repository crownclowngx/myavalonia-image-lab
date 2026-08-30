namespace ImageLabPlugin.Domain.Fingerprinting;

internal enum FingerprintDecision
{
    ExactFingerprintMatch,
    NearUnderReferencePolicy,
    NotNearUnderReferencePolicy,
    NotComparable
}

internal enum FingerprintOverview
{
    ConsistentlyNear,
    Divergent,
    ConsistentlyNotNear,
    Incomplete
}

/// <summary>把距离映射为版本化参考结论；阈值是可解释参考线，不是普适真理或来源概率。</summary>
internal sealed class FingerprintDecisionPolicy
{
    public const string PolicyId = "fingerprint-reference-policy-v1";
    private static readonly IReadOnlyDictionary<FingerprintAlgorithmId, int> Thresholds =
        new Dictionary<FingerprintAlgorithmId, int>
        {
            [FingerprintAlgorithmId.AverageHash] = 8,
            [FingerprintAlgorithmId.DifferenceHash] = 12,
            [FingerprintAlgorithmId.PerceptualHash] = 12
        };

    public int GetThreshold(FingerprintAlgorithmId algorithmId) => Thresholds.TryGetValue(algorithmId, out var value)
        ? value
        : throw new ArgumentException($"策略未校准算法 {algorithmId}。", nameof(algorithmId));

    public FingerprintDecision Decide(FingerprintAlgorithmId algorithmId, FingerprintDistance? distance)
    {
        if (distance is null) return FingerprintDecision.NotComparable;
        if (distance.Value.Distance == 0) return FingerprintDecision.ExactFingerprintMatch;
        return distance.Value.Distance <= GetThreshold(algorithmId)
            ? FingerprintDecision.NearUnderReferencePolicy
            : FingerprintDecision.NotNearUnderReferencePolicy;
    }

    public FingerprintOverview Summarize(IEnumerable<FingerprintDecision> decisions)
    {
        var values = decisions.ToArray();
        if (values.Length == 0 || values.Contains(FingerprintDecision.NotComparable)) return FingerprintOverview.Incomplete;
        if (values.All(value => value is FingerprintDecision.ExactFingerprintMatch or FingerprintDecision.NearUnderReferencePolicy)) return FingerprintOverview.ConsistentlyNear;
        if (values.All(value => value == FingerprintDecision.NotNearUnderReferencePolicy)) return FingerprintOverview.ConsistentlyNotNear;
        return FingerprintOverview.Divergent;
    }
}
