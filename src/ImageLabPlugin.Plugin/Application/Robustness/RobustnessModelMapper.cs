using ImageLabPlugin.Domain.Fingerprinting;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Watermarking;

namespace ImageLabPlugin.Application.Robustness;

/// <summary>隔离 Robustness 与 Watermarking/Fingerprinting 的同级领域模型。</summary>
internal static class RobustnessModelMapper
{
    public static RobustnessProfileId ToRobustnessProfile(EmbeddingProfileId value) => value switch
    {
        EmbeddingProfileId.Stealth => RobustnessProfileId.Stealth,
        EmbeddingProfileId.Balanced => RobustnessProfileId.Balanced,
        EmbeddingProfileId.Robust => RobustnessProfileId.Robust,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static EmbeddingProfileId ToEmbeddingProfile(RobustnessProfileId value) => value switch
    {
        RobustnessProfileId.Stealth => EmbeddingProfileId.Stealth,
        RobustnessProfileId.Balanced => EmbeddingProfileId.Balanced,
        RobustnessProfileId.Robust => EmbeddingProfileId.Robust,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static RobustnessDetectionStatus ToRobustnessStatus(WatermarkDetectionStatus value) =>
        (RobustnessDetectionStatus)(int)value;

    public static RobustnessIntegrityStatus ToRobustnessIntegrity(IntegrityStatus value) =>
        (RobustnessIntegrityStatus)(int)value;

    public static RobustnessFingerprintObservation ToRobustnessObservation(FingerprintObservation value)
    {
        var algorithmId = new RobustnessFingerprintAlgorithmId(value.AlgorithmId.Value);
        return new(
            algorithmId,
            new(algorithmId, value.Reference.Bits),
            new(algorithmId, value.Candidate.Bits),
            new(value.Distance.Distance));
    }
}
