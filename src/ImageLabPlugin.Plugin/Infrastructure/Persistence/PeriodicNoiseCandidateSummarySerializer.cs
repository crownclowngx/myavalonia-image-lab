using System.Text.Json;
using ImageLabPlugin.Application.PeriodicNoiseRemoval;
using ImageLabPlugin.Domain.PeriodicNoiseRemoval;

namespace ImageLabPlugin.Infrastructure.Persistence;

/// <summary>把候选依据写成独立、只读且有界的 JSON 摘要。</summary>
/// <remarks>
/// 摘要包含 Session 指纹、候选分数、突出度、风险和建议状态，只用于复核，不可作为配方重放；源图片字节、路径、FFT、
/// Bitmap 与逐 bin 遮罩均不会序列化，避免报告越权成为第二种持久化协议。
/// </remarks>
internal sealed class PeriodicNoiseCandidateSummarySerializer : IPeriodicNoiseCandidateSummarySerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public byte[] Serialize(PeriodicNoiseSession session, PeriodicNoiseDetectionResult detection)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(detection);
        session.ThrowIfDisposed();
        var suggested = detection.Suggestions.Select(item => item.CanonicalFrequency).ToHashSet();
        var candidates = detection.Candidates.Select((item, index) => new CandidateDto(index + 1,
            item.CanonicalFrequency.Fx, item.CanonicalFrequency.Fy, item.ConjugateFrequency.Fx,
            item.ConjugateFrequency.Fy, item.RobustScore, item.Prominence, item.LocalCompactness,
            item.RiskLevel.ToString(), item.RiskReasons.ToString(), suggested.Contains(item.CanonicalFrequency)))
            .ToArray();
        return JsonSerializer.SerializeToUtf8Bytes(new SummaryDto(PeriodicNoiseRecipe.ProductId, 1,
            session.SessionFingerprint, session.Spectrum.PaddedWidth, session.Spectrum.PaddedHeight,
            candidates), Options);
    }

    private sealed record SummaryDto(string ProductId, int SchemaVersion, string SessionFingerprint,
        int SpectrumWidth, int SpectrumHeight, CandidateDto[] Candidates);
    private sealed record CandidateDto(int Number, double Fx, double Fy, double ConjugateFx, double ConjugateFy,
        double RobustScore, double Prominence, double LocalCompactness, string RiskLevel, string RiskReasons,
        bool Suggested);
}
