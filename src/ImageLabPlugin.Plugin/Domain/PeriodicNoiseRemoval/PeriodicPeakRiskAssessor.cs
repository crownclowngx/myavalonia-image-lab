namespace ImageLabPlugin.Domain.PeriodicNoiseRemoval;

/// <summary>把可复现的频率位置与邻域事实翻译为风险原因和等级。</summary>
/// <remarks>
/// 本服务不判断峰的设备来源，也不修改配方。风险只说明“靠近主体结构、采样极限、峰不够尖锐或邻域过密”等事实，
/// 供自动建议保守筛选和用户人工复核共同使用。
/// </remarks>
internal sealed class PeriodicPeakRiskAssessor
{
    public (PeriodicPeakRiskLevel Level, PeriodicPeakRiskReason Reasons) Assess(
        PeriodicFrequency frequency, double prominence, double compactness, int denseNeighborCount,
        PeriodicNoiseDetectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var reasons = PeriodicPeakRiskReason.None;
        if (frequency.Radius < settings.DcExclusionRadius * 2d) reasons |= PeriodicPeakRiskReason.NearDc;
        if (Math.Abs(frequency.Fx) >= 0.46d || Math.Abs(frequency.Fy) >= 0.46d)
            reasons |= PeriodicPeakRiskReason.NearNyquist;
        if (compactness < 0.55d) reasons |= PeriodicPeakRiskReason.BroadPeakOrRidge;
        if (denseNeighborCount >= 4) reasons |= PeriodicPeakRiskReason.DenseNeighborhood;
        if (prominence < settings.ProminenceThreshold * 1.5d) reasons |= PeriodicPeakRiskReason.LowProminence;
        if (PeriodicFrequency.ToroidalDistance(frequency, frequency.Conjugate()) <= 1e-12)
            reasons |= PeriodicPeakRiskReason.SelfConjugate;

        var high = PeriodicPeakRiskReason.NearNyquist | PeriodicPeakRiskReason.BroadPeakOrRidge |
            PeriodicPeakRiskReason.DenseNeighborhood | PeriodicPeakRiskReason.SelfConjugate;
        var level = (reasons & high) != 0 ? PeriodicPeakRiskLevel.High :
            reasons == PeriodicPeakRiskReason.None ? PeriodicPeakRiskLevel.Low : PeriodicPeakRiskLevel.Medium;
        return (level, reasons);
    }
}
