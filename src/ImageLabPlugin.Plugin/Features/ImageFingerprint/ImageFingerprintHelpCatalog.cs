using ImageLabPlugin.Domain.Fingerprinting;

namespace ImageLabPlugin.Features.ImageFingerprint;

/// <summary>集中维护面向用户的算法名称和限制，避免 View 与 Document 各写一套措辞。</summary>
internal static class ImageFingerprintHelpCatalog
{
    public static string DisplayName(FingerprintAlgorithmId id) => id == FingerprintAlgorithmId.AverageHash ? "aHash" : id == FingerprintAlgorithmId.DifferenceHash ? "dHash" : "pHash";

    public static string DecisionText(FingerprintDecision value) => value switch
    {
        FingerprintDecision.ExactFingerprintMatch => "摘要完全相同",
        FingerprintDecision.NearUnderReferencePolicy => "参考策略下接近",
        FingerprintDecision.NotNearUnderReferencePolicy => "参考策略下不接近",
        _ => "不可比较"
    };

    public static string OverviewText(FingerprintOverview value) => value switch
    {
        FingerprintOverview.ConsistentlyNear => "一致接近",
        FingerprintOverview.ConsistentlyNotNear => "一致不接近",
        FingerprintOverview.Incomplete => "结果不完整",
        _ => "结果分歧，需要查看图片和算法限制"
    };
}

internal sealed record FingerprintAlgorithmRow(
    string Name,
    string AlgorithmId,
    string ReferenceHex,
    string CandidateHex,
    int Distance,
    string Similarity,
    string Decision,
    int Threshold,
    string Limitation,
    ulong ReferenceBits,
    ulong CandidateBits)
{
    public ulong XorBits => ReferenceBits ^ CandidateBits;
}
